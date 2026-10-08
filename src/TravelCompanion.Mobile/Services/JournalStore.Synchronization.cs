namespace TravelCompanion.Mobile.Services;

public enum JournalSynchronizationState { Idle, Synchronizing, Pending, Offline, Failed }

public sealed partial class JournalStore
{
    private sealed class SynchronizationStatus
    {
        public int ActiveCycles;
        public JournalSynchronizationState State;
        public JournalSynchronizationState Outcome;
    }

    private sealed class SynchronizationRequest { public bool Repeat; }

    private readonly object synchronizationStateGate = new();
    private readonly Dictionary<JournalScope, SynchronizationStatus> synchronizationStates = [];
    private readonly Dictionary<JournalScope, SynchronizationRequest> synchronizationRequests = [];
#if ANDROID || IOS || MACCATALYST || WINDOWS
    private bool connectivityObserverStarted;
    private bool connectivityCheckRunning;
#endif

    public event EventHandler<JournalScope>? SynchronizationChanged;

    public JournalSynchronizationState GetSynchronizationState(JournalScope scope)
    {
        if (!IsCurrent(scope)) return JournalSynchronizationState.Idle;
        lock (synchronizationStateGate)
            return synchronizationStates.TryGetValue(scope, out var status)
                ? status.State : JournalSynchronizationState.Idle;
    }

    // One runner per context; further requests only ask it for one subsequent cycle.
    // Callers confirm encrypted local storage first and never await remote work here.
    public void RequestSynchronization(JournalScope scope)
    {
        if (!IsCurrent(scope)) return;
        EnsureConnectivityObserver();
        SynchronizationRequest request;
        lock (synchronizationStateGate)
        {
            if (synchronizationRequests.TryGetValue(scope, out var existing))
            {
                existing.Repeat = true;
                return;
            }
            request = new();
            synchronizationRequests.Add(scope, request);
            var status = StatusLocked(scope);
            status.State = status.ActiveCycles > 0
                ? JournalSynchronizationState.Synchronizing : JournalSynchronizationState.Pending;
            PruneSynchronizationStatesLocked(scope);
        }
        PublishSynchronization(scope);
        _ = Task.Run(() => RunRequestedSynchronizationAsync(scope, request));
    }

    private async Task RunRequestedSynchronizationAsync(JournalScope scope, SynchronizationRequest request)
    {
        try
        {
            while (IsCurrent(scope))
            {
                await SyncAsync(scope, CancellationToken.None, null).ConfigureAwait(false);
                lock (synchronizationStateGate)
                {
                    // A new action or reconnection can request one more cycle even
                    // after failure. Without that request, failures never retry themselves.
                    if (!IsCurrent(scope) || !request.Repeat
                        || Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                    {
                        FinishSynchronizationRequestLocked(scope, request);
                        break;
                    }
                    request.Repeat = false;
                }
            }
        }
        catch (OperationCanceledException) when (!IsCurrent(scope)) { }
        catch (Exception exception)
        {
            RecordSynchronizationFailure(exception);
            lock (synchronizationStateGate)
                StatusLocked(scope).Outcome = JournalSynchronizationState.Failed;
        }
        finally
        {
            lock (synchronizationStateGate)
                FinishSynchronizationRequestLocked(scope, request);
            PublishSynchronization(scope);
        }
    }

    private void FinishSynchronizationRequestLocked(JournalScope scope, SynchronizationRequest request)
    {
        if (!synchronizationRequests.TryGetValue(scope, out var current) || !ReferenceEquals(current, request)) return;
        synchronizationRequests.Remove(scope);
        var status = StatusLocked(scope);
        status.State = status.ActiveCycles > 0
            ? JournalSynchronizationState.Synchronizing : status.Outcome;
        PruneSynchronizationStatesLocked(scope);
    }

    private void BeginSynchronization(JournalScope scope)
    {
        EnsureConnectivityObserver();
        lock (synchronizationStateGate)
        {
            var status = StatusLocked(scope);
            status.ActiveCycles++;
            status.State = JournalSynchronizationState.Synchronizing;
        }
        PublishSynchronization(scope);
    }

    private void CompleteSynchronization(JournalScope scope, JournalSynchronizationState outcome)
    {
        lock (synchronizationStateGate)
        {
            var status = StatusLocked(scope);
            status.ActiveCycles--;
            status.Outcome = outcome;
            status.State = status.ActiveCycles > 0 || synchronizationRequests.ContainsKey(scope)
                ? JournalSynchronizationState.Synchronizing : outcome;
        }
        PublishSynchronization(scope);
    }

    private SynchronizationStatus StatusLocked(JournalScope scope)
    {
        if (!synchronizationStates.TryGetValue(scope, out var status))
            synchronizationStates.Add(scope, status = new());
        return status;
    }

    private void PruneSynchronizationStatesLocked(JournalScope current)
    {
        foreach (var obsolete in synchronizationStates.Where(item => item.Key != current
                     && item.Value.ActiveCycles == 0 && !synchronizationRequests.ContainsKey(item.Key))
                     .Select(item => item.Key).ToArray())
            synchronizationStates.Remove(obsolete);
    }

    private void PublishSynchronization(JournalScope scope)
    {
        if (!IsCurrent(scope) || SynchronizationChanged is not { } handlers) return;
        foreach (EventHandler<JournalScope> handler in handlers.GetInvocationList())
        {
            try { handler(this, scope); }
            catch (Exception exception) { RecordSynchronizationFailure(exception, "journal_sync_observer_failed"); }
        }
    }

    private static bool HasPendingSynchronization(IEnumerable<JournalMemory> entries) => entries.Any(entry =>
        !entry.IsDraft && !entry.HasConflict
        && (entry.Pending is not null || entry.FreePending is not null || entry.DeletePending is not null));

    private static void RecordSynchronizationFailure(Exception exception, string name = "journal_sync_failed")
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        ClientDiagnostics.Record(name, new()
        {
            Area = "journal",
            Status = exception is HttpRequestException { StatusCode: { } status } ? (int)status : null,
            Canceled = exception is OperationCanceledException
        }, exception);
#endif
    }

    private void EnsureConnectivityObserver()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        lock (synchronizationStateGate)
        {
            if (connectivityObserverStarted) return;
            connectivityObserverStarted = true;
        }
        // The store is a singleton. A single observer also recovers Journal when
        // another domain's coordinator replay fails before it reaches this store.
        Connectivity.Current.ConnectivityChanged += OnJournalConnectivityChanged;
#endif
    }

#if ANDROID || IOS || MACCATALYST || WINDOWS
    private void OnJournalConnectivityChanged(object? sender, Microsoft.Maui.Networking.ConnectivityChangedEventArgs args)
    {
        if (args.NetworkAccess != NetworkAccess.Internet) return;
        lock (synchronizationStateGate)
        {
            if (connectivityCheckRunning) return;
            connectivityCheckRunning = true;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                if (!sessions.HasSession || sessions.CurrentUserId is null || sessions.CurrentTripId is null) return;
                var scope = Scope();
                if (HasPendingSynchronization(await ReadLocalSnapshotAsync(scope, CancellationToken.None).ConfigureAwait(false))
                    && IsCurrent(scope)) RequestSynchronization(scope);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { RecordSynchronizationFailure(exception); }
            finally { lock (synchronizationStateGate) connectivityCheckRunning = false; }
        });
    }
#endif
}

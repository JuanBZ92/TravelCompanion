namespace TravelCompanion.Mobile.Services;

// One scope owns the entire request, including token lookup and optional GPS.
// Platform callbacks may ignore cancellation; every await also checks identity.
public sealed class AssistantRequestScope : IDisposable
{
    private readonly AuthSessionService sessions;
    private readonly Func<DateOnly> selectedDate;
    private readonly Func<int>? operationVersion;
    private readonly int? version;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Guid? user;
    private readonly Guid? trip;
    private readonly long contextVersion;

    public AssistantRequestScope(AuthSessionService sessions, Func<DateOnly> selectedDate, Func<int>? operationVersion = null)
    {
        this.sessions = sessions;
        this.selectedDate = selectedDate;
        this.operationVersion = operationVersion;
        version = operationVersion?.Invoke();
        user = sessions.CurrentUserId;
        trip = sessions.CurrentTripId;
        contextVersion = sessions.ContextVersion;
        Date = selectedDate();
        Token = cancellation.Token;
    }

    public DateOnly Date { get; }
    public Guid? TripId => trip;
    public CancellationToken Token { get; }
    public bool ExplicitlyCancelled { get; private set; }
    public bool HasCurrentContext => sessions.HasSession && sessions.ContextVersion == contextVersion
        && sessions.CurrentUserId == user && sessions.CurrentTripId == trip && selectedDate() == Date
        && (operationVersion is null || operationVersion() == version);
    public bool CanPublish => HasCurrentContext && !Token.IsCancellationRequested;

    public void Verify()
    {
        if (!CanPublish) throw new OperationCanceledException(Token);
    }

    public async Task<T> AwaitAsync<T>(Func<CancellationToken, Task<T>> work)
    {
        Verify();
        var result = await work(Token).WaitAsync(Token);
        Verify();
        return result;
    }

    public void SetNetworkTimeout(TimeSpan timeout) => cancellation.CancelAfter(timeout);
    public void Cancel() { ExplicitlyCancelled = true; cancellation.Cancel(); }
    public void Dispose() => cancellation.Dispose();
}

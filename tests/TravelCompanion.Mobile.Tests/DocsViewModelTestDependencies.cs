namespace TravelCompanion.Mobile.Services;

// Network boundary only; document tests execute the production ViewModel and personal stores.
public sealed class OfflineSyncCoordinator
{
    public int VersionRequests { get; private set; }
    public int SynchronizeTriggers { get; private set; }
    public void TriggerSynchronize() => SynchronizeTriggers++;
    public Func<CancellationToken, Task> Synchronize { get; set; } = _ => Task.CompletedTask;
    public Task SynchronizeVersionsAsync(string token, bool force, CancellationToken cancellationToken = default)
    {
        VersionRequests++;
        return Synchronize(cancellationToken);
    }
}

namespace TravelCompanion.Mobile.Services;

internal sealed class SerialSaveQueue
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RunAsync(Func<Task> save)
    {
        await _gate.WaitAsync();
        try { await save(); }
        finally { _gate.Release(); }
    }
}

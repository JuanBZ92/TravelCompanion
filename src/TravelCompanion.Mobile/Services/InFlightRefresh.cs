namespace TravelCompanion.Mobile.Services;

// Coordinates one active refresh per store. Cache identity and invalidation policy belong to the caller.
internal sealed class InFlightRefresh<T>
{
    private readonly object _lock = new();
    private Task<T>? _task;
    private string? _key;
    private CancellationTokenSource? _cancellation;

    public async Task<T> RunAsync(
        string key,
        Func<CancellationToken, Task<T>> refresh,
        CancellationToken cancellationToken = default,
        Action? onJoined = null)
    {
        Task<T> task;
        lock (_lock)
        {
            if (_task is null || _task.IsCompleted || !string.Equals(_key, key, StringComparison.Ordinal))
            {
                // A new context does not cancel a previous request; stores reject stale responses themselves.
                _cancellation?.Dispose();
                _cancellation = new CancellationTokenSource();
                _key = key;
                _task = refresh(_cancellation.Token);
            }
            else
            {
                onJoined?.Invoke();
            }
            task = _task;
        }

        try
        {
            // A caller may stop waiting without cancelling work shared by other callers.
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (task.IsCompleted)
            {
                lock (_lock)
                {
                    if (ReferenceEquals(_task, task))
                    {
                        Clear();
                    }
                }
            }
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _cancellation?.Cancel();
        }
    }

    public void CancelAndDetach()
    {
        lock (_lock)
        {
            _cancellation?.Cancel();
            Clear();
        }
    }

    // Called only while holding _lock.
    private void Clear()
    {
        _task = null;
        _key = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }
}

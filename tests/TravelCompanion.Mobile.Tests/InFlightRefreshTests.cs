using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class InFlightRefreshTests
{
    [Fact]
    public async Task Concurrent_callers_for_the_same_context_share_one_request()
    {
        var refresh = new InFlightRefresh<int>();
        var response = PendingResponse();
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var requests = 0;
        var joined = 0;
        void Entered()
        {
            if (Interlocked.Increment(ref entered) == 16) allEntered.SetResult();
        }
        Task<int> Fetch(CancellationToken ct)
        {
            Interlocked.Increment(ref requests);
            Entered();
            return response.Task;
        }

        // Each caller enters from a worker thread while the shared response is still pending.
        var callers = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => refresh.RunAsync("session:trip:locale", Fetch,
                onJoined: () =>
                {
                    Interlocked.Increment(ref joined);
                    Entered();
                }))).ToArray();
        // Complete only after all callers have entered the coordinator, without timing-based sleeps.
        await allEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        response.SetResult(42);
        var results = await Task.WhenAll(callers).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(results, value => Assert.Equal(42, value));
        Assert.Equal(1, requests);
        Assert.Equal(15, joined);
    }

    [Fact]
    public async Task Cancelling_one_waiter_does_not_cancel_the_shared_request()
    {
        var refresh = new InFlightRefresh<int>();
        var response = PendingResponse();
        using var waiterCancellation = new CancellationTokenSource();
        CancellationToken requestToken = default;
        var first = refresh.RunAsync("context", ct =>
        {
            requestToken = ct;
            return response.Task;
        }, waiterCancellation.Token);
        var second = refresh.RunAsync("context", _ => throw new InvalidOperationException("Duplicate request"));

        waiterCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(requestToken.IsCancellationRequested);
        Assert.False(second.IsCompleted);

        response.SetResult(7);
        Assert.Equal(7, await second);
    }

    [Theory]
    [InlineData("other-user:trip:es:0")]
    [InlineData("user:other-trip:es:0")]
    [InlineData("user:trip:en:0")]
    [InlineData("user:trip:es:1")]
    public async Task Changed_context_starts_a_request_and_old_completion_cannot_clear_it(string newContext)
    {
        var refresh = new InFlightRefresh<int>();
        var oldResponse = PendingResponse();
        var newResponse = PendingResponse();
        CancellationToken oldToken = default;
        var oldCall = refresh.RunAsync("user:trip:es:0", ct =>
        {
            oldToken = ct;
            return oldResponse.Task;
        });
        var newCall = refresh.RunAsync(newContext, _ => newResponse.Task);

        Assert.False(oldToken.IsCancellationRequested);
        oldResponse.SetResult(1);
        Assert.Equal(1, await oldCall);
        var joined = refresh.RunAsync(newContext, _ => throw new InvalidOperationException("Lost active request"));
        newResponse.SetResult(2);

        Assert.Equal(2, await newCall);
        Assert.Equal(2, await joined);
    }

    [Fact]
    public async Task Cancel_keeps_the_active_request_until_it_completes()
    {
        var refresh = new InFlightRefresh<int>();
        var response = PendingResponse();
        CancellationToken requestToken = default;
        var first = refresh.RunAsync("context", ct =>
        {
            requestToken = ct;
            return response.Task;
        });

        refresh.Cancel();
        Assert.True(requestToken.IsCancellationRequested);
        var joined = refresh.RunAsync("context", _ => throw new InvalidOperationException("Detached request"));
        response.SetCanceled(requestToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joined);
        Assert.Equal(3, await refresh.RunAsync("context", _ => Task.FromResult(3)));
    }

    [Fact]
    public async Task Cancel_and_detach_allows_an_immediate_refresh_of_the_same_context()
    {
        var refresh = new InFlightRefresh<int>();
        var oldResponse = PendingResponse();
        var newResponse = PendingResponse();
        CancellationToken oldToken = default;
        var oldCall = refresh.RunAsync("context", ct =>
        {
            oldToken = ct;
            return oldResponse.Task;
        });

        refresh.CancelAndDetach();
        Assert.True(oldToken.IsCancellationRequested);
        var newCall = refresh.RunAsync("context", _ => newResponse.Task);
        oldResponse.SetCanceled(oldToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldCall);
        var joined = refresh.RunAsync("context", _ => throw new InvalidOperationException("Lost new request"));
        newResponse.SetResult(4);
        Assert.Equal(4, await newCall);
        Assert.Equal(4, await joined);
    }

    [Fact]
    public async Task Failed_request_propagates_to_waiters_and_allows_retry()
    {
        var refresh = new InFlightRefresh<int>();
        var response = PendingResponse();
        var first = refresh.RunAsync("context", _ => response.Task);
        var second = refresh.RunAsync("context", _ => throw new InvalidOperationException("Duplicate request"));
        var failure = new HttpRequestException("Offline");
        response.SetException(failure);

        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => first));
        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => second));
        Assert.Equal(5, await refresh.RunAsync("context", _ => Task.FromResult(5)));
    }

    [Fact]
    public async Task Completed_request_without_waiters_does_not_block_the_next_refresh()
    {
        var refresh = new InFlightRefresh<int>();
        var response = PendingResponse();
        using var cancellation = new CancellationTokenSource();
        var abandoned = refresh.RunAsync("context", _ => response.Task, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        response.SetResult(1);
        Assert.Equal(2, await refresh.RunAsync("context", _ => Task.FromResult(2)));
    }

    private static TaskCompletionSource<int> PendingResponse() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

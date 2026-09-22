using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class SerialSaveQueueTests
{
    [Fact]
    public async Task Rapid_saves_wait_for_previous_save_and_all_complete_in_order()
    {
        var queue = new SerialSaveQueue();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new List<int>();
        var first = queue.RunAsync(async () => { await release.Task; completed.Add(0); });
        var others = Enumerable.Range(1, 4).Select(index => queue.RunAsync(() =>
        {
            completed.Add(index);
            return Task.CompletedTask;
        })).ToArray();
        Assert.Empty(completed);
        Assert.All(others, task => Assert.False(task.IsCompleted));
        release.SetResult();
        await Task.WhenAll(others.Prepend(first));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, completed);
    }

    [Fact]
    public async Task Failure_does_not_block_next_save()
    {
        var queue = new SerialSaveQueue();
        await Assert.ThrowsAsync<IOException>(() => queue.RunAsync(() => throw new IOException()));
        var saved = false;
        await queue.RunAsync(() => { saved = true; return Task.CompletedTask; });
        Assert.True(saved);
    }
}

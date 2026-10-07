using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class AssistantOperationTests
{
    private static AuthSessionDto Session() => new(Guid.NewGuid(), "assistant@example.test", "Assistant", false,
        "token", Guid.NewGuid());

    [Fact]
    public async Task Cancel_during_delayed_gps_finishes_without_sending_even_if_platform_ignores_token()
    {
        var sessions = new AuthSessionService();
        try
        {
            await sessions.SaveAsync(Session());
            using var scope = new AssistantRequestScope(sessions, () => new(2026, 10, 8));
            var gps = new TaskCompletionSource<GeoPointDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sent = false;
            CancellationToken supplied = default;
            async Task Work()
            {
                await scope.AwaitAsync(token => { supplied = token; return gps.Task; });
                sent = true;
            }
            var task = Work();
            scope.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.True(supplied.IsCancellationRequested);
            gps.SetResult(new(35.6m, 139.7m));
            Assert.False(sent);
            Assert.False(scope.CanPublish);
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData("account")]
    [InlineData("trip")]
    [InlineData("date")]
    [InlineData("page")]
    public async Task Late_response_from_previous_context_is_discarded(string changed)
    {
        var sessions = new AuthSessionService();
        try
        {
            var session = Session();
            await sessions.SaveAsync(session);
            var date = new DateOnly(2026, 10, 8);
            var page = 1;
            using var scope = new AssistantRequestScope(sessions, () => date, () => page);
            var delayed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = scope.AwaitAsync(_ => delayed.Task);
            if (changed == "date") date = date.AddDays(1);
            else if (changed == "page") page++;
            else await sessions.SaveAsync(changed == "trip" ? session with { TripId = Guid.NewGuid() } : Session());
            delayed.SetResult("Old response");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.False(scope.HasCurrentContext);
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_save_keeps_original_date_and_discards_work_after_date_or_page_changes(bool leavePage)
    {
        var sessions = new AuthSessionService();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await sessions.SaveAsync(Session());
            var selected = new DateOnly(2026, 10, 8);
            var page = 1;
            using var scope = new AssistantRequestScope(sessions, () => selected, () => page);
            var queue = new SerialSaveQueue();
            var first = queue.RunAsync(() => release.Task);
            DateOnly? editorDate = null;
            var queued = queue.RunAsync(() =>
            {
                scope.Verify();
                editorDate = scope.Date;
                return Task.CompletedTask;
            });
            if (leavePage) page++;
            else selected = selected.AddDays(1);
            release.SetResult();
            await first;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.Null(editorDate);
            Assert.Equal(new DateOnly(2026, 10, 8), scope.Date);
        }
        finally { release.TrySetResult(); sessions.Clear(); }
    }

    [Fact]
    public async Task Cancelled_operation_does_not_cancel_a_new_operation_for_same_context()
    {
        var sessions = new AuthSessionService();
        try
        {
            await sessions.SaveAsync(Session());
            using var old = new AssistantRequestScope(sessions, () => new(2026, 10, 8));
            old.Cancel();
            using var current = new AssistantRequestScope(sessions, () => new(2026, 10, 8));
            Assert.Equal("New response", await current.AwaitAsync(_ => Task.FromResult("New response")));
            Assert.False(old.CanPublish);
            Assert.True(current.CanPublish);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Cancel_before_token_lookup_does_not_start_work()
    {
        var sessions = new AuthSessionService();
        try
        {
            await sessions.SaveAsync(Session());
            using var scope = new AssistantRequestScope(sessions, () => new(2026, 10, 8));
            scope.Cancel();
            var calls = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.AwaitAsync(_ =>
            {
                calls++;
                return Task.FromResult("token");
            }));
            Assert.Equal(0, calls);
        }
        finally { sessions.Clear(); }
    }
}

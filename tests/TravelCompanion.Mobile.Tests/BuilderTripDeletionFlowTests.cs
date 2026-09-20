using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class BuilderTripDeletionFlowTests
{
    [Fact]
    public async Task Fetches_latest_revision_before_confirmation_and_uses_that_revision()
    {
        var tripId = Guid.NewGuid();
        var steps = new List<string>();
        var result = await BuilderTripDeletionFlow.ExecuteAsync(tripId,
            _ => { steps.Add("fetch"); return Task.FromResult<BuilderTripSetupDto?>(Setup(tripId, 42)); },
            setup => { steps.Add("confirm"); Assert.Equal(42, setup.Revision); return Task.FromResult(true); },
            (request, _) =>
            {
                steps.Add("delete");
                Assert.Equal(new DeleteBuilderTripSetupRequest(tripId, 42), request);
                return Task.CompletedTask;
            }, default);
        Assert.Equal(BuilderTripDeletionResult.Deleted, result);
        Assert.Equal(new[] { "fetch", "confirm", "delete" }, steps);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_or_replaced_trip_is_not_confirmed_or_deleted(bool unavailable)
    {
        var result = await BuilderTripDeletionFlow.ExecuteAsync(Guid.NewGuid(),
            _ => Task.FromResult<BuilderTripSetupDto?>(unavailable ? null : Setup(Guid.NewGuid(), 2)),
            _ => throw new Exception("Must not confirm another trip"),
            (_, _) => throw new Exception("Must not delete another trip"), default);
        Assert.Equal(unavailable ? BuilderTripDeletionResult.Unavailable : BuilderTripDeletionResult.TripChanged, result);
    }

    [Fact]
    public async Task Cancelled_confirmation_does_not_delete()
    {
        var tripId = Guid.NewGuid();
        var result = await BuilderTripDeletionFlow.ExecuteAsync(tripId,
            _ => Task.FromResult<BuilderTripSetupDto?>(Setup(tripId, 2)),
            _ => Task.FromResult(false),
            (_, _) => throw new Exception("Must not delete"), default);
        Assert.Equal(BuilderTripDeletionResult.Cancelled, result);
    }

    [Fact]
    public async Task Network_failure_does_not_fall_back_to_a_cached_revision()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => BuilderTripDeletionFlow.ExecuteAsync(Guid.NewGuid(),
            _ => throw new HttpRequestException("Offline"),
            _ => throw new Exception("Must not confirm offline"),
            (_, _) => throw new Exception("Must not delete offline"), default));
    }

    [Fact]
    public async Task Conflict_after_confirmation_is_not_retried()
    {
        var tripId = Guid.NewGuid();
        var confirmations = 0;
        var deletions = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => BuilderTripDeletionFlow.ExecuteAsync(tripId,
            _ => Task.FromResult<BuilderTripSetupDto?>(Setup(tripId, 2)),
            _ => { confirmations++; return Task.FromResult(true); },
            (_, _) => { deletions++; throw new InvalidOperationException("Revision changed"); }, default));
        Assert.Equal(1, confirmations);
        Assert.Equal(1, deletions);
    }

    [Fact]
    public async Task Cancellation_while_confirmation_is_open_prevents_delete()
    {
        using var cancellation = new CancellationTokenSource();
        var tripId = Guid.NewGuid();
        await Assert.ThrowsAsync<OperationCanceledException>(() => BuilderTripDeletionFlow.ExecuteAsync(tripId,
            _ => Task.FromResult<BuilderTripSetupDto?>(Setup(tripId, 2)),
            _ => { cancellation.Cancel(); return Task.FromResult(true); },
            (_, _) => throw new Exception("Must not delete after cancellation"), cancellation.Token));
    }

    private static BuilderTripSetupDto Setup(Guid tripId, int revision) =>
        new(true, tripId, revision, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 24), "Japan", "Asia/Tokyo", []);
}

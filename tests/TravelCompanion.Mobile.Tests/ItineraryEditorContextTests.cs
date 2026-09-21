using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class ItineraryEditorContextTests
{
    private readonly Guid _tripId = Guid.NewGuid();
    private BuilderTripSetupDto Setup(int revision) =>
        new(true, _tripId, revision, new(2026, 9, 20), new(2026, 9, 24), "Japan", "Asia/Tokyo", []);

    [Fact]
    public async Task Reopening_replaces_old_revision_with_server_revision()
    {
        var context = new ItineraryEditorContext();
        await context.LoadAsync(1, _tripId, () => Task.FromResult<BuilderTripSetupDto?>(Setup(3)), () => 1);
        await context.LoadAsync(1, _tripId, () => Task.FromResult<BuilderTripSetupDto?>(Setup(17)), () => 1);
        Assert.Equal(17, context.Revision);
        Assert.True(context.IsCurrent(1));
    }

    [Fact]
    public async Task Offline_reopen_does_not_authorize_previous_revision()
    {
        var context = new ItineraryEditorContext();
        await context.LoadAsync(1, _tripId, () => Task.FromResult<BuilderTripSetupDto?>(Setup(3)), () => 1);
        await Assert.ThrowsAsync<HttpRequestException>(() => context.LoadAsync(1, _tripId,
            () => throw new HttpRequestException(), () => 1));
        Assert.False(context.IsCurrent(1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Late_response_cannot_authorize_another_session_or_trip(bool sessionChanged)
    {
        var context = new ItineraryEditorContext();
        var setup = await context.LoadAsync(1, sessionChanged ? _tripId : Guid.NewGuid(),
            () => Task.FromResult<BuilderTripSetupDto?>(Setup(17)), () => sessionChanged ? 2 : 1);
        Assert.Null(setup);
        Assert.False(context.IsCurrent(1));
        Assert.False(context.IsCurrent(2));
    }

    [Fact]
    public async Task Missing_setup_does_not_authorize_revision_zero()
    {
        var context = new ItineraryEditorContext();
        await context.LoadAsync(1, _tripId, () => Task.FromResult<BuilderTripSetupDto?>(null), () => 1);
        Assert.False(context.IsCurrent(1));
    }
}

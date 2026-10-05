using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class ScheduleTodayVirtualizationTests
{
    private static readonly DateOnly Date = new(2026, 10, 20);

    [Fact]
    public void Group_exposes_every_large_day_item_with_original_identity_order_and_actions()
    {
        var locations = new[] { Location("First idea"), Location("Second idea") };
        var reservations = Enumerable.Range(0, 100).Select(index => Reservation("Saved plan " + index)).ToArray();
        var group = new ScheduleTodaySectionViewModel(1, Date, "morning", "Morning", "",
            locations, reservations, canAddItem: true);

        Assert.Equal(103, group.Count);
        Assert.Same(locations[0], group[0]);
        Assert.Same(locations[1], group[1]);
        Assert.IsType<TodayReservationHeadingViewModel>(group[2]);
        for (var index = 0; index < reservations.Length; index++) Assert.Same(reservations[index], group[index + 3]);
        Assert.Single(group.OfType<TodayReservationHeadingViewModel>());
        Assert.Equal(reservations.Select(item => item.Item.Id), group.OfType<TodayReservationViewModel>().Select(item => item.Item.Id));
        Assert.All(group.OfType<TodayReservationViewModel>(), item => Assert.True(item.CanEdit));
        Assert.Same(locations, group.Locations);
        Assert.Same(reservations, group.Reservations);
        Assert.True(group.HasContent);
        Assert.True(group.CanAddItem);
        Assert.Equal(Date, group.Date);
        Assert.Equal("morning", group.PeriodKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_group_has_no_fake_rows_and_keeps_its_invitation_and_existing_access(bool canAdd)
    {
        var group = new ScheduleTodaySectionViewModel(1, Date, "afternoon", "Afternoon", "", [], [], canAdd);

        Assert.Empty(group);
        Assert.True(group.IsEmpty);
        Assert.False(group.HasContent);
        Assert.Equal(canAdd, group.CanAddItem);
        Assert.False(string.IsNullOrWhiteSpace(group.EmptyTitle));
        Assert.False(string.IsNullOrWhiteSpace(group.EmptySubtitle));
        Assert.Equal("afternoon", group.PeriodKey);
    }

    [Fact]
    public void Recommendations_without_reservations_do_not_create_a_booking_heading()
    {
        var location = Location("Idea");
        var group = new ScheduleTodaySectionViewModel(1, Date, "night", "Evening", "", [location], []);

        Assert.Same(location, Assert.Single(group));
        Assert.Empty(group.OfType<TodayReservationHeadingViewModel>());
        Assert.False(group.HasReservations);
        Assert.True(group.HasLocations);
    }

    [Fact]
    public void Booking_only_group_keeps_the_supplied_order_and_read_only_booking_permissions()
    {
        var first = Reservation("First supplied", ItineraryItemOwner.Yuku);
        var last = Reservation("Last supplied", ItineraryItemOwner.Yuku);
        var group = new ScheduleTodaySectionViewModel(1, Date, "midday", "Midday", "", [], [first, last]);

        Assert.Collection(group,
            row => Assert.IsType<TodayReservationHeadingViewModel>(row),
            row => Assert.Same(first, row),
            row => Assert.Same(last, row));
        Assert.All(group.OfType<TodayReservationViewModel>(), item => Assert.False(item.CanEdit));
        Assert.False(group.CanAddItem);
        Assert.True(group.HasReservations);
    }

    private static TodayLocationViewModel Location(string title) => new(new RecommendationDto(
        Guid.NewGuid(), Guid.NewGuid(), title, "Culture", "Tokyo", "", [], "medium", 35m, 139m,
        60, null, null, ContentAccessLevel.Free, [], null), null);

    private static TodayReservationViewModel Reservation(string title, ItineraryItemOwner owner = ItineraryItemOwner.Traveler) =>
        new(new ScheduleItemDto(Guid.NewGuid(), null, ReservationType.Event, Date, new(10, 0), null, null,
            title, "Tokyo", "Place", "Address", "", "", null, null, null, null, null, null,
            ScheduleItemKind.ManualEvent, owner));
}

using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Tests;

[Collection("Schedule localization")]
public sealed class ScheduleDayNavigationTests : IDisposable
{
    private readonly string _originalCulture = LocalizationResourceManager.Instance.CurrentCulture.Name;
    private static readonly DateOnly FirstDate = new(2026, 10, 19);

    [Fact]
    public void Adjacent_selection_returns_the_real_day_objects_and_stops_at_both_trip_limits()
    {
        var days = Days(3);

        Assert.Null(ScheduleDayNavigation.Find(days, FirstDate, -1));
        Assert.Same(days[1], ScheduleDayNavigation.Find(days, FirstDate, 1));
        Assert.Same(days[0], ScheduleDayNavigation.Find(days, FirstDate.AddDays(1), -1));
        Assert.Same(days[2], ScheduleDayNavigation.Find(days, FirstDate.AddDays(1), 1));
        Assert.Null(ScheduleDayNavigation.Find(days, FirstDate.AddDays(2), 1));
        Assert.Same(days[2], ScheduleDayNavigation.Find(days, FirstDate.AddDays(2)));
    }

    [Fact]
    public void One_day_and_missing_selection_do_not_invent_neighbors_or_move_outside_the_trip()
    {
        var days = Days(1);

        Assert.Same(days[0], ScheduleDayNavigation.Find(days, FirstDate));
        Assert.Null(ScheduleDayNavigation.Find(days, FirstDate, -1));
        Assert.Null(ScheduleDayNavigation.Find(days, FirstDate, 1));
        Assert.Null(ScheduleDayNavigation.Find(days, null));
        Assert.Null(ScheduleDayNavigation.Find(days, FirstDate.AddDays(1), -1));
        Assert.Null(ScheduleDayNavigation.Find([], FirstDate));
    }

    [Fact]
    public void Center_uses_the_hotel_name_and_falls_back_to_city_without_losing_the_selected_date()
    {
        var day = new ScheduleDayFilterViewModel(FirstDate.AddDays(1), 2, "Tokyo");

        Assert.Equal("20/10 · Yuku Hotel", day.SelectedLabel("  Yuku Hotel  "));
        Assert.Equal("20/10 · Tokyo", day.SelectedLabel(null));
        Assert.Equal("20/10 · Tokyo", day.SelectedLabel("  "));
        day.UpdateCity("Kyoto");
        Assert.Equal("20/10 · Kyoto", day.SelectedLabel(null));
        Assert.Equal("20/10 · Yuku Hotel", day.SelectedLabel("Yuku Hotel"));
    }

    [Fact]
    public void Date_only_and_locked_days_keep_their_date_and_existing_access_indicator()
    {
        var day = new ScheduleDayFilterViewModel(FirstDate, 1, "", isLocked: true);

        Assert.Equal("🔒 19/10", day.SelectedLabel(null));
        Assert.Equal("🔒 19/10 · Hotel", day.SelectedLabel("Hotel"));
        Assert.StartsWith("🔒 ", day.FullDateLabel);
        Assert.True(day.IsLocked);
        Assert.False(day.IsSelected);
    }

    [Theory]
    [InlineData("es", "octubre")]
    [InlineData("en-US", "October")]
    public void Accessible_description_has_the_full_localized_date_and_preserves_the_hotel_name(
        string culture, string month)
    {
        LocalizationResourceManager.Instance.SetCulture(culture);
        var day = new ScheduleDayFilterViewModel(FirstDate, 1, "Tokyo");

        Assert.Contains(month, day.FullDateLabel);
        Assert.Contains("2026", day.FullDateLabel);
        Assert.Equal(day.FullDateLabel + " · ホテル Yuku", day.SelectedDescription("ホテル Yuku"));
        Assert.Equal(day.FullDateLabel + " · Tokyo", day.SelectedDescription(null));
    }

    private static ScheduleDayFilterViewModel[] Days(int count) => Enumerable.Range(0, count)
        .Select(index => new ScheduleDayFilterViewModel(FirstDate.AddDays(index), index + 1, "Tokyo")).ToArray();

    public void Dispose() => LocalizationResourceManager.Instance.SetCulture(_originalCulture);
}

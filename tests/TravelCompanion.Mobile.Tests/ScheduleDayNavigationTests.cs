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

    [Fact]
    public void Thirty_day_trip_accepts_a_direct_choice_without_selecting_intermediate_days()
    {
        var days = Days(30);
        days[0].IsSelected = true;

        Assert.Same(days[29], ScheduleDayNavigation.ResolveChoice(days, days[29]));
        Assert.Same(days[11], ScheduleDayNavigation.ResolveChoice(days, days[11]));
        Assert.Same(days[0], Assert.Single(days, day => day.IsSelected));
        Assert.Equal(FirstDate.AddDays(29), days[29].Date);
        Assert.Same(days[28], ScheduleDayNavigation.Find(days, days[29].Date, -1));
        Assert.Null(ScheduleDayNavigation.Find(days, days[29].Date, 1));
    }

    [Fact]
    public void Choice_rejects_missing_outside_and_old_trip_objects_even_when_the_date_matches()
    {
        var days = Days(30);
        var staleTripDay = new ScheduleDayFilterViewModel(days[29].Date, 30, "Kyoto");
        var outside = new ScheduleDayFilterViewModel(FirstDate.AddDays(30), 31, "Tokyo");

        Assert.Null(ScheduleDayNavigation.ResolveChoice(days, null));
        Assert.Null(ScheduleDayNavigation.ResolveChoice(days, staleTripDay));
        Assert.Null(ScheduleDayNavigation.ResolveChoice(days, outside));
        Assert.Null(ScheduleDayNavigation.ResolveChoice([], days[0]));
        Assert.Same(days[29], ScheduleDayNavigation.ResolveChoice(days.Select(day => day), days[29]));
    }

    [Fact]
    public void Locked_day_remains_navigable_without_losing_its_access_indicator()
    {
        var day = new ScheduleDayFilterViewModel(FirstDate.AddDays(29), 30, "Tokyo", isLocked: true);

        Assert.Same(day, ScheduleDayNavigation.ResolveChoice([day], day));
        day.IsSelected = true;
        day.UpdateStay("Yuku Hotel");
        Assert.True(day.IsLocked);
        Assert.StartsWith("🔒 ", day.DisplayLabel);
        Assert.StartsWith("🔒 ", day.DisplayDescription);
        Assert.Contains("Yuku Hotel", day.DisplayLabel);
    }

    [Fact]
    public void Selected_tile_shows_the_stay_and_collapses_to_date_while_preserving_its_metadata()
    {
        var day = new ScheduleDayFilterViewModel(FirstDate, 1, "Tokyo");
        var collapsedWidth = day.ItemWidth;
        day.UpdateStay("  ホテル Yuku  ");
        Assert.Equal(day.DateLabel, day.DisplayLabel);
        Assert.Equal(day.FullDateLabel, day.DisplayDescription);

        day.IsSelected = true;
        Assert.True(day.ItemWidth > collapsedWidth);
        Assert.Equal(day.DateLabel + " · ホテル Yuku", day.DisplayLabel);
        Assert.Equal(day.FullDateLabel + " · ホテル Yuku", day.DisplayDescription);
        day.IsSelected = false;
        Assert.Equal(collapsedWidth, day.ItemWidth);
        Assert.Equal(day.DateLabel, day.DisplayLabel);
        Assert.Equal(day.FullDateLabel, day.DisplayDescription);
        day.UpdateStay("Kyoto Hotel");
        day.IsSelected = true;
        Assert.Contains("Kyoto Hotel", day.DisplayLabel);
        day.UpdateStay(null);
        day.UpdateCity("Kyoto");
        Assert.Equal(day.DateLabel + " · Kyoto", day.DisplayLabel);
        Assert.Equal(day.FullDateLabel + " · Kyoto", day.DisplayDescription);
    }

    [Fact]
    public void Selection_and_place_changes_notify_the_visible_tile_bindings()
    {
        var day = new ScheduleDayFilterViewModel(FirstDate, 1, "Tokyo");
        var changes = new List<string?>();
        day.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        day.IsSelected = true;
        Assert.Contains(nameof(day.ItemWidth), changes);
        Assert.Contains(nameof(day.DisplayLabel), changes);
        Assert.Contains(nameof(day.DisplayDescription), changes);
        Assert.Contains(nameof(day.BackgroundColor), changes);
        changes.Clear();
        day.UpdateStay("Hotel");
        Assert.Contains(nameof(day.DisplayLabel), changes);
        Assert.Contains(nameof(day.DisplayDescription), changes);
        changes.Clear();
        day.UpdateStay(" Hotel ");
        Assert.Empty(changes);
        day.UpdateStay(null);
        changes.Clear();
        day.UpdateCity("Kyoto");
        Assert.Contains(nameof(day.City), changes);
        Assert.Contains(nameof(day.DisplayLabel), changes);
        Assert.Contains(nameof(day.DisplayDescription), changes);
        Assert.Contains("Kyoto", day.DisplayLabel);
    }

    [Fact]
    public void Map_action_is_available_only_for_the_selected_day_with_an_allowed_stay()
    {
        var day = new ScheduleDayFilterViewModel(FirstDate, 1, "Tokyo");
        var changes = new List<string?>();
        day.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        day.UpdateStay("Hotel", canOpenMap: true);
        Assert.False(day.CanOpenMap);
        day.IsSelected = true;
        Assert.True(day.CanOpenMap);
        Assert.Contains(nameof(day.CanOpenMap), changes);
        changes.Clear();
        day.UpdateStay("Hotel", canOpenMap: false);
        Assert.False(day.CanOpenMap);
        Assert.Contains(nameof(day.CanOpenMap), changes);
        day.UpdateStay("Hotel", canOpenMap: true);
        day.IsSelected = false;
        Assert.False(day.CanOpenMap);
        Assert.Equal(day.DateLabel, day.DisplayLabel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enlarged_text_grows_the_tile_without_changing_its_date_place_selection_or_access(bool selected)
    {
        var day = new ScheduleDayFilterViewModel(FirstDate, 1, "Kyoto", isSelected: selected, isLocked: true);
        day.UpdateStay("ホテル Yuku", canOpenMap: true);
        var originalWidth = day.ItemWidth;
        var originalLabel = day.DisplayLabel;
        var originalDescription = day.DisplayDescription;
        var originalMapAccess = day.CanOpenMap;

        day.UpdateTextScale(3);

        Assert.True(day.ItemWidth > originalWidth);
        Assert.Equal(FirstDate, day.Date);
        Assert.Equal("Kyoto", day.City);
        Assert.True(day.IsLocked);
        Assert.Equal(selected, day.IsSelected);
        Assert.Equal(originalMapAccess, day.CanOpenMap);
        Assert.Equal(originalLabel, day.DisplayLabel);
        Assert.Equal(originalDescription, day.DisplayDescription);
        day.UpdateTextScale(1);
        Assert.Equal(originalWidth, day.ItemWidth);
    }

    [Fact]
    public void Scale_updates_notify_layout_once_and_selection_keeps_the_current_scaled_width()
    {
        var day = new ScheduleDayFilterViewModel(FirstDate, 1, "Tokyo");
        var changes = new List<string?>();
        day.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        day.UpdateTextScale(2);
        Assert.Equal(nameof(day.ItemWidth), Assert.Single(changes));
        changes.Clear();
        day.UpdateTextScale(2);
        Assert.Empty(changes);
        var scaledWidth = day.ItemWidth;
        day.IsSelected = true;
        Assert.True(day.ItemWidth >= scaledWidth);
        day.IsSelected = false;
        Assert.Equal(scaledWidth, day.ItemWidth);
        day.UpdateTextScale(3);
        Assert.True(day.ItemWidth > scaledWidth);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(-2)]
    public void Invalid_or_smaller_text_scales_restore_normal_layout_without_repeated_notifications(double scale)
    {
        var day = new ScheduleDayFilterViewModel(FirstDate, 1, "Tokyo");
        var originalWidth = day.ItemWidth;
        day.UpdateTextScale(2);
        var changes = new List<string?>();
        day.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        day.UpdateTextScale(scale);

        Assert.True(double.IsFinite(day.ItemWidth));
        Assert.Equal(originalWidth, day.ItemWidth);
        Assert.Equal(nameof(day.ItemWidth), Assert.Single(changes));
        changes.Clear();
        day.UpdateTextScale(scale);
        Assert.Empty(changes);
    }

    private static ScheduleDayFilterViewModel[] Days(int count) => Enumerable.Range(0, count)
        .Select(index => new ScheduleDayFilterViewModel(FirstDate.AddDays(index), index + 1, "Tokyo")).ToArray();

    public void Dispose() => LocalizationResourceManager.Instance.SetCulture(_originalCulture);
}

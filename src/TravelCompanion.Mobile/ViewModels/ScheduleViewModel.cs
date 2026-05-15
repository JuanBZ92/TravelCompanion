using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleViewModel(
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore,
    ILogger<ScheduleViewModel> logger) : ViewModelBase, ISessionStateResettable
{
    private const string AllCitiesKey = "All Cities";
    private readonly List<ScheduleItemDto> _allItems = [];
    private readonly Dictionary<string, ScheduleTypeSectionViewModel> _sectionCache = new(StringComparer.Ordinal);
    private ReservationType _selectedType = ReservationType.Event;
    private string _tripTitle = "Your Trip";
    private string? _tripDates;
    private ScheduleItemDto? _selectedItem;
    private ScheduleTypeSectionViewModel? _activeSection;
    private IReadOnlyList<ScheduleDayViewModel> _activeDays = [];

    public ObservableCollection<ScheduleTypeSectionViewModel> TypeSections { get; } = [];
    public ObservableCollection<ScheduleTypeFilterViewModel> TypeFilters { get; } = [];
    public ObservableCollection<CityFilterViewModel> CityFilters { get; } = [];
    public IReadOnlyList<ScheduleDayViewModel> ActiveDays
    {
        get => _activeDays;
        private set => SetProperty(ref _activeDays, value);
    }

    public bool HasScheduleItems => ActiveDays.Count > 0;
    public bool ShowInitialLoading => IsBusy && !HasScheduleItems;
    public bool ShowEmptyState => HasLoaded && !IsBusy && !HasScheduleItems;

    public string TripTitle
    {
        get => _tripTitle;
        set => SetProperty(ref _tripTitle, value);
    }

    public string? TripDates
    {
        get => _tripDates;
        set => SetProperty(ref _tripDates, value);
    }

    public ScheduleItemDto? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public void ResetForNewSession()
    {
        ResetLoadState();
        _allItems.Clear();
        _sectionCache.Clear();
        ActiveDays = [];
        TypeSections.Clear();
        _activeSection = null;
        TypeFilters.Clear();
        CityFilters.Clear();
        _selectedType = ReservationType.Event;
        TripTitle = "Your Trip";
        TripDates = null;
        SelectedItem = null;
    }

    [RelayCommand]
    private Task LoadScheduleAsync()
    {
        return LoadAsync(async ct =>
        {
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            await LoadScheduleLocalFirstAsync(token, ct);
        });
    }

    [RelayCommand]
    private async Task OpenScheduleItemAsync(ScheduleItemDto? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = null;
        await Shell.Current.GoToAsync(
            nameof(ScheduleItemDetailPage),
            new Dictionary<string, object>
            {
                ["ScheduleItem"] = item
            });
    }

    [RelayCommand]
    private void ToggleTypeFilter(ReservationType type)
    {
        var stopwatch = Stopwatch.StartNew();
        _selectedType = type;
        foreach (var filter in TypeFilters)
        {
            filter.IsSelected = filter.Type == type;
        }

        UpdateCityFilters();
        ApplyCityFilter();
        stopwatch.Stop();

        logger.LogInformation(
            "Schedule type filter changed in {ElapsedMs}ms. Type={ReservationType}; VisibleDays={VisibleDays}; VisibleItems={VisibleItems}.",
            stopwatch.Elapsed.TotalMilliseconds,
            type,
            ActiveDays.Count,
            ActiveDays.Sum(day => day.Count));
    }

    [RelayCommand]
    private void ToggleCityFilter(string? cityName)
    {
        if (string.IsNullOrWhiteSpace(cityName))
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var filter = CityFilters.FirstOrDefault(f => f.CityName == cityName);
        if (filter is null)
        {
            return;
        }

        // If clicking "All Cities"
        if (filter.IsAllCities)
        {
            // Deselect all other cities and select "All Cities"
            foreach (var f in CityFilters)
            {
                f.IsSelected = f.IsAllCities;
            }
        }
        else
        {
            // Toggle the clicked city
            filter.IsSelected = !filter.IsSelected;

            // If at least one specific city is selected, deselect "All Cities"
            var allCitiesFilter = CityFilters.FirstOrDefault(f => f.IsAllCities);
            if (allCitiesFilter is not null)
            {
                var anySelected = CityFilters.Any(f => !f.IsAllCities && f.IsSelected);
                allCitiesFilter.IsSelected = !anySelected;
            }
        }

        ApplyCityFilter();
        stopwatch.Stop();

        logger.LogInformation(
            "Schedule city filter changed in {ElapsedMs}ms. City={CityName}; Type={ReservationType}; VisibleDays={VisibleDays}; VisibleItems={VisibleItems}.",
            stopwatch.Elapsed.TotalMilliseconds,
            cityName,
            _selectedType,
            ActiveDays.Count,
            ActiveDays.Sum(day => day.Count));
    }

    [RelayCommand]
    private void ClearFilters()
    {
        foreach (var filter in CityFilters)
        {
            filter.IsSelected = filter.IsAllCities;
        }

        ApplyCityFilter();
    }

    private async Task LoadScheduleLocalFirstAsync(string token, CancellationToken cancellationToken = default)
    {
        var cached = await bootstrapStore.GetCachedAsync(cancellationToken: cancellationToken);
        if (cached is not null)
        {
            ApplyBootstrapSchedule(cached.Value);

            if (bootstrapStore.HasFreshSnapshot())
            {
                StatusMessage = null;
                return;
            }

            StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
        }

        try
        {
            var bootstrap = await bootstrapStore.RefreshAsync(token, cancellationToken: cancellationToken);
            if (bootstrap is null)
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            ApplyBootstrapSchedule(bootstrap);
            StatusMessage = null;
        }
        catch
        {
            if (cached is null)
            {
                throw;
            }

            StatusMessage = $"Offline mode. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
        }
    }

    private void ApplyBootstrapSchedule(MobileBootstrapDto bootstrap)
    {
        if (bootstrap.Schedule is null)
        {
            ApplyEmptySchedule();
            return;
        }

        ApplySchedule(bootstrap.Schedule);
    }

    private void ApplySchedule(TripScheduleDto schedule)
    {
        var stopwatch = Stopwatch.StartNew();
        TripTitle = $"{schedule.DestinationName} for {schedule.TravelerName}";
        TripDates = $"{schedule.StartsOn:MMM d} - {schedule.EndsOn:MMM d, yyyy}";
        _allItems.Clear();
        _allItems.AddRange(schedule.Items);
        _selectedType = GetInitialScheduleType(_allItems);
        UpdateTypeFilters();
        UpdateCityFilters();
        RebuildDefaultTypeSections();
        ApplyCityFilter();
        stopwatch.Stop();

        logger.LogInformation(
            "Schedule applied in {ElapsedMs}ms. SourceItems={SourceItems}; InitialType={ReservationType}; VisibleDays={VisibleDays}; VisibleItems={VisibleItems}.",
            stopwatch.Elapsed.TotalMilliseconds,
            schedule.Items.Count,
            _selectedType,
            ActiveDays.Count,
            ActiveDays.Sum(day => day.Count));
    }

    private void ApplyEmptySchedule()
    {
        TripTitle = "Your Trip";
        TripDates = "No reservations yet.";
        _allItems.Clear();
        _sectionCache.Clear();
        ActiveDays = [];
        TypeSections.Clear();
        _activeSection = null;
        TypeFilters.Clear();
        UpdateTypeFilters();
        CityFilters.Clear();
        CityFilters.Add(new CityFilterViewModel(AllCitiesKey, isSelected: true));
        OnPropertyChanged(nameof(HasScheduleItems));
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void UpdateTypeFilters()
    {
        // Build filter list first to minimize CollectionChanged events
        var types = new[] { ReservationType.Event, ReservationType.Flight, ReservationType.Lodging };
        var newFilters = types.Select(type => new ScheduleTypeFilterViewModel(type, type == _selectedType)).ToList();

        TypeFilters.Clear();
        foreach (var filter in newFilters)
        {
            TypeFilters.Add(filter);
        }
    }

    private void UpdateCityFilters()
    {
        // Preserve current selections
        var currentSelections = CityFilters
            .Where(f => f.IsSelected)
            .Select(f => f.CityName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Build complete filter list first to minimize CollectionChanged events
        var newFilters = new List<CityFilterViewModel>();

        // Add "All Cities" filter
        var allCitiesSelected = currentSelections.Count == 0 ||
                               currentSelections.Contains(AllCitiesKey);
        newFilters.Add(new CityFilterViewModel(AllCitiesKey, isSelected: allCitiesSelected));

        // Add individual city filters
        var now = DateTime.Now;
        var cities = _allItems
            .Where(item => item.Type == _selectedType)
            .Where(item => !IsPast(item, now))
            .Select(item => NormalizeCity(item.City))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

        foreach (var city in cities)
        {
            var isSelected = currentSelections.Contains(city);
            newFilters.Add(new CityFilterViewModel(city, isSelected: isSelected));
        }

        // If we had selections but none exist anymore, select "All Cities"
        if (currentSelections.Count > 0 &&
            !newFilters.Any(f => !f.IsAllCities && f.IsSelected))
        {
            var allCities = newFilters.FirstOrDefault(f => f.IsAllCities);
            if (allCities is not null)
            {
                allCities.IsSelected = true;
            }
        }

        // Clear and rebuild in one pass
        CityFilters.Clear();
        foreach (var filter in newFilters)
        {
            CityFilters.Add(filter);
        }
    }

    private void ApplyCityFilter()
    {
        var selectedCities = CityFilters
            .Where(f => !f.IsAllCities && f.IsSelected)
            .Select(f => f.CityName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // If no specific cities selected or "All Cities" is selected, show all
        var allCitiesSelected = CityFilters
            .FirstOrDefault(f => f.IsAllCities)?
            .IsSelected ?? true;

        ShowScheduleSection(_selectedType, selectedCities, allCitiesSelected);
    }

    private void RebuildDefaultTypeSections()
    {
        _sectionCache.Clear();
        TypeSections.Clear();
        foreach (var type in new[] { ReservationType.Event, ReservationType.Flight, ReservationType.Lodging })
        {
            var section = CreateScheduleSection(type, new HashSet<string>(StringComparer.OrdinalIgnoreCase), allCitiesSelected: true);
            _sectionCache[section.CacheKey] = section;
            TypeSections.Add(section);
        }
    }

    private void ShowScheduleSection(
        ReservationType type,
        IReadOnlySet<string> selectedCities,
        bool allCitiesSelected)
    {
        var section = GetOrCreateScheduleSection(type, selectedCities, allCitiesSelected);
        foreach (var existingSection in TypeSections)
        {
            existingSection.IsVisible = ReferenceEquals(existingSection, section);
        }

        _activeSection = section;
        ActiveDays = section.Days;
        section.IsVisible = true;

        OnPropertyChanged(nameof(HasScheduleItems));
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private ScheduleTypeSectionViewModel GetOrCreateScheduleSection(
        ReservationType type,
        IReadOnlySet<string> selectedCities,
        bool allCitiesSelected)
    {
        var cacheKey = GetSectionCacheKey(type, selectedCities, allCitiesSelected);
        if (_sectionCache.TryGetValue(cacheKey, out var section))
        {
            return section;
        }

        section = CreateScheduleSection(type, selectedCities, allCitiesSelected);
        _sectionCache[cacheKey] = section;
        TypeSections.Add(section);
        return section;
    }

    private ScheduleTypeSectionViewModel CreateScheduleSection(
        ReservationType type,
        IReadOnlySet<string> selectedCities,
        bool allCitiesSelected)
    {
        var now = DateTime.Now;
        var filteredItems = _allItems
            .Where(item => item.Type == type)
            .Where(item => !IsPast(item, now));

        if (!allCitiesSelected && selectedCities.Count > 0)
        {
            filteredItems = filteredItems.Where(item => selectedCities.Contains(NormalizeCity(item.City)));
        }

        var dayGroups = filteredItems
            .GroupBy(item => item.Date)
            .OrderBy(group => group.Key)
            .Select(group => new ScheduleDayViewModel(
                group.Key,
                group.OrderBy(item => item.StartsAt)))
            .ToList();

        return new ScheduleTypeSectionViewModel(
            type,
            GetSectionCacheKey(type, selectedCities, allCitiesSelected),
            dayGroups);
    }

    private static string GetSectionCacheKey(
        ReservationType type,
        IReadOnlySet<string> selectedCities,
        bool allCitiesSelected)
    {
        if (allCitiesSelected || selectedCities.Count == 0)
        {
            return $"{type}|{AllCitiesKey}";
        }

        return $"{type}|{string.Join(";", selectedCities.Order(StringComparer.OrdinalIgnoreCase))}";
    }

    private static string NormalizeCity(string? city)
    {
        return string.IsNullOrWhiteSpace(city) ? "Unknown City" : city.Trim();
    }

    private static ReservationType GetInitialScheduleType(IReadOnlyList<ScheduleItemDto> items)
    {
        var now = DateTime.Now;
        return items
            .Where(item => !IsPast(item, now))
            .OrderBy(item => GetTimelineSortValue(item, now))
            .ThenBy(item => item.StartsAt)
            .Select(item => item.Type)
            .FirstOrDefault(ReservationType.Event);
    }

    private static bool IsPast(ScheduleItemDto item, DateTime now)
    {
        return GetEndDateTime(item) < now;
    }

    private static DateTime GetTimelineSortValue(ScheduleItemDto item, DateTime now)
    {
        var start = GetStartDateTime(item);
        var end = GetEndDateTime(item);
        if (start <= now && end >= now)
        {
            return now;
        }

        return start;
    }

    private static DateTime GetStartDateTime(ScheduleItemDto item)
    {
        return item.Date.ToDateTime(item.StartsAt);
    }

    private static DateTime GetEndDateTime(ScheduleItemDto item)
    {
        var endDate = item.EndsOn ?? item.Date;
        var endTime = item.EndsAt ?? item.StartsAt;
        return endDate.ToDateTime(endTime);
    }

    protected override void OnLoadStateChanged()
    {
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}

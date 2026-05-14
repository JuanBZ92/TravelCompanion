using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleViewModel(
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore) : ViewModelBase
{
    private const string AllCitiesKey = "All Cities";
    private readonly List<ScheduleItemDto> _allItems = [];
    private ReservationType _selectedType = ReservationType.Event;
    private string _tripTitle = "Your Trip";
    private string? _tripDates;
    private ScheduleItemDto? _selectedItem;

    public ObservableCollection<ScheduleDayViewModel> Days { get; } = [];
    public ObservableCollection<ScheduleTypeFilterViewModel> TypeFilters { get; } = [];
    public ObservableCollection<CityFilterViewModel> CityFilters { get; } = [];

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

    [RelayCommand]
    private Task LoadScheduleAsync()
    {
        return LoadAsync(async () =>
        {
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            await LoadScheduleLocalFirstAsync(token);
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
        _selectedType = type;
        foreach (var filter in TypeFilters)
        {
            filter.IsSelected = filter.Type == type;
        }

        UpdateCityFilters();
        ApplyCityFilter();
    }

    [RelayCommand]
    private void ToggleCityFilter(string? cityName)
    {
        if (string.IsNullOrWhiteSpace(cityName))
        {
            return;
        }

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

    private async Task LoadScheduleLocalFirstAsync(string token)
    {
        var cached = await bootstrapStore.GetCachedAsync();
        if (cached is not null)
        {
            ApplyBootstrapSchedule(cached.Value);
            StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
        }

        try
        {
            var bootstrap = await bootstrapStore.RefreshAsync(token);
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
        TripTitle = $"{schedule.DestinationName} for {schedule.TravelerName}";
        TripDates = $"{schedule.StartsOn:MMM d} - {schedule.EndsOn:MMM d, yyyy}";
        _allItems.Clear();
        _allItems.AddRange(schedule.Items);
        UpdateTypeFilters();
        UpdateCityFilters();
        ApplyCityFilter();
    }

    private void ApplyEmptySchedule()
    {
        TripTitle = "Your Trip";
        TripDates = "No reservations yet.";
        _allItems.Clear();
        Days.Clear();
        TypeFilters.Clear();
        UpdateTypeFilters();
        CityFilters.Clear();
        CityFilters.Add(new CityFilterViewModel(AllCitiesKey, isSelected: true));
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
        var cities = _allItems
            .Where(item => item.Type == _selectedType)
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

        // Filter without intermediate ToList() allocations
        var itemsOfSelectedType = _allItems.Where(item => item.Type == _selectedType);

        var filteredItems = allCitiesSelected || selectedCities.Count == 0
            ? itemsOfSelectedType
            : itemsOfSelectedType.Where(item => selectedCities.Contains(NormalizeCity(item.City)));

        // Group and build day list before updating collection
        var dayGroups = filteredItems
            .GroupBy(item => item.Date)
            .OrderBy(group => group.Key)
            .Select(group => new ScheduleDayViewModel(
                group.Key,
                group.OrderBy(item => item.StartsAt)))
            .ToList(); // Only materialize final grouped result

        // Clear and rebuild - still multiple events but unavoidable without ObservableRangeCollection
        Days.Clear();
        foreach (var day in dayGroups)
        {
            Days.Add(day);
        }
    }

    private static string NormalizeCity(string? city)
    {
        return string.IsNullOrWhiteSpace(city) ? "Unknown City" : city.Trim();
    }
}

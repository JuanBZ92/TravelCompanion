using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleViewModel(
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore) : ViewModelBase
{
    private const string AllCities = "Todas las ciudades";
    private readonly List<ScheduleItemDto> _allItems = [];
    private string _tripTitle = "Tu viaje";
    private string? _tripDates;
    private string _selectedCity = AllCities;
    private ScheduleItemDto? _selectedItem;

    public ObservableCollection<ScheduleDayViewModel> Days { get; } = [];
    public ObservableCollection<string> Cities { get; } = [AllCities];

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

    public string SelectedCity
    {
        get => _selectedCity;
        set
        {
            if (SetProperty(ref _selectedCity, value))
            {
                ApplyCityFilter();
            }
        }
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

            StatusMessage = $"Modo offline. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
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
        TripTitle = $"{schedule.DestinationName} para {schedule.TravelerName}";
        TripDates = $"{schedule.StartsOn:MMM d} - {schedule.EndsOn:MMM d, yyyy}";
        _allItems.Clear();
        _allItems.AddRange(schedule.Items);
        UpdateCities();
        ApplyCityFilter();
    }

    private void ApplyEmptySchedule()
    {
        TripTitle = "Tu viaje";
        TripDates = "Todavia no hay reservas asignadas.";
        _allItems.Clear();
        Days.Clear();
        Cities.Clear();
        Cities.Add(AllCities);
        SelectedCity = AllCities;
    }

    private void UpdateCities()
    {
        var currentSelection = SelectedCity;
        Cities.Clear();
        Cities.Add(AllCities);

        foreach (var city in _allItems
            .Select(item => NormalizeCity(item.City))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            Cities.Add(city);
        }

        SelectedCity = Cities.Contains(currentSelection, StringComparer.OrdinalIgnoreCase)
            ? currentSelection
            : AllCities;
    }

    private void ApplyCityFilter()
    {
        var selectedCity = NormalizeCity(SelectedCity);
        var filteredItems = selectedCity == AllCities
            ? _allItems
            : _allItems
                .Where(item => string.Equals(
                    NormalizeCity(item.City),
                    selectedCity,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

        Days.Clear();
        foreach (var group in filteredItems.GroupBy(item => item.Date).OrderBy(group => group.Key))
        {
            Days.Add(new ScheduleDayViewModel(
                group.Key,
                group.OrderBy(item => item.StartsAt)));
        }
    }

    private static string NormalizeCity(string? city)
    {
        return string.IsNullOrWhiteSpace(city) ? "Sin ciudad" : city.Trim();
    }
}

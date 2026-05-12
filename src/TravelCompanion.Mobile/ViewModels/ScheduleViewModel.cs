using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleViewModel(TravelCompanionApiClient apiClient) : ViewModelBase
{
    private string _tripTitle = "Tu viaje";
    private string? _tripDates;
    private ScheduleItemDto? _selectedItem;

    public ObservableCollection<ScheduleDayViewModel> Days { get; } = [];

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
            var schedule = await apiClient.GetDemoScheduleAsync();
            if (schedule is null)
            {
                return;
            }

            TripTitle = $"{schedule.DestinationName} para {schedule.TravelerName}";
            TripDates = $"{schedule.StartsOn:MMM d} - {schedule.EndsOn:MMM d, yyyy}";

            Days.Clear();
            foreach (var group in schedule.Items.GroupBy(item => item.Date).OrderBy(group => group.Key))
            {
                Days.Add(new ScheduleDayViewModel(
                    group.Key,
                    group.OrderBy(item => item.StartsAt)));
            }
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
}

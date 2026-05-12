using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleViewModel(TravelCompanionApiClient apiClient) : ViewModelBase
{
    private string _tripTitle = "Tu viaje";
    private string? _tripDates;

    public ObservableCollection<ScheduleItemDto> Items { get; } = [];

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

            Items.Clear();
            foreach (var item in schedule.Items)
            {
                Items.Add(item);
            }
        });
    }
}

using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleItemDetailViewModel : ViewModelBase, IQueryAttributable
{
    private ScheduleItemDto? _scheduleItem;

    public ScheduleItemDto? ScheduleItem
    {
        get => _scheduleItem;
        set => SetProperty(ref _scheduleItem, value);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("ScheduleItem", out var value) && value is ScheduleItemDto item)
        {
            ScheduleItem = item;
        }
    }

    [RelayCommand]
    private async Task OpenMapsAsync()
    {
        if (ScheduleItem is null || string.IsNullOrWhiteSpace(ScheduleItem.Address))
        {
            return;
        }

        var options = new MapLaunchOptions
        {
            Name = ScheduleItem.LocationName,
            NavigationMode = NavigationMode.Walking
        };

        var placemark = new Placemark
        {
            Thoroughfare = ScheduleItem.Address
        };

        await Map.Default.OpenAsync(placemark, options);
    }
}

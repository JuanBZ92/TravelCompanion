using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleItemDetailViewModel : ViewModelBase, IQueryAttributable
{
    private ScheduleItemDto? _scheduleItem;

    public ScheduleItemDto? ScheduleItem
    {
        get => _scheduleItem;
        set
        {
            if (SetProperty(ref _scheduleItem, value))
            {
                OnPropertyChanged(nameof(ReservationReferenceLabel));
                OnPropertyChanged(nameof(ReservationTimeLabel));
                OnPropertyChanged(nameof(AddressText));
                OnPropertyChanged(nameof(NotesText));
                OnPropertyChanged(nameof(HasNotes));
                OnPropertyChanged(nameof(HasAddress));
            }
        }
    }

    public string ReservationReferenceLabel
    {
        get
        {
            if (ScheduleItem is null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(ScheduleItem.ConfirmationCode)
                ? ScheduleItem.LocationName
                : $"Codigo {ScheduleItem.ConfirmationCode}";
        }
    }

    public string ReservationTimeLabel
    {
        get
        {
            if (ScheduleItem is null)
            {
                return string.Empty;
            }

            var date = ScheduleItem.Date.ToString("dddd d MMMM");
            return ScheduleItem.HasEnd
                ? $"{date} · {ScheduleItem.StartsAt:HH\\:mm} - {ScheduleItem.EndDisplay.Replace("Hasta: ", string.Empty).Replace("Horario de llegada: ", string.Empty)}"
                : $"{date} · {ScheduleItem.StartsAt:HH\\:mm}";
        }
    }

    public string AddressText => ScheduleItem is null || string.IsNullOrWhiteSpace(ScheduleItem.Address)
        ? "Direccion no disponible"
        : ScheduleItem.Address;

    public string NotesText => ScheduleItem?.Notes ?? string.Empty;
    public bool HasNotes => !string.IsNullOrWhiteSpace(NotesText);
    public bool HasAddress => ScheduleItem is not null && !string.IsNullOrWhiteSpace(ScheduleItem.Address);

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

        await GoogleMapsLauncher.OpenAsync(ScheduleItem.Address);
    }
}

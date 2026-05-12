using System.Collections.ObjectModel;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class ScheduleDayViewModel(DateOnly date, IEnumerable<ScheduleItemDto> items)
    : ObservableCollection<ScheduleItemDto>(items)
{
    public DateOnly Date { get; } = date;
    public string DayLabel => Date.ToString("dddd, MMM d");
    public string CountLabel => Count == 1 ? "1 reserva" : $"{Count} reservas";
}

using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class ScheduleTimelineItemViewModel
{
    public ScheduleTimelineItemViewModel(ScheduleItemDto item)
    {
        Item = item;
    }

    public ScheduleItemDto Item { get; }

    public string TimeOfDayLabel => Item.Type == ReservationType.Flight
        ? "Salida"
        : Item.StartsAt.Hour switch
        {
            < 12 => "Mañana",
            < 15 => "Mediodia",
            < 20 => "Tarde",
            _ => "Noche"
        };

    public string TypeBadge => Item.Type switch
    {
        ReservationType.Flight => "VUELO",
        ReservationType.Lodging => "HOTEL",
        _ => "PLAN"
    };

    public string Title => Item.Title;
    public string TimeLabel => Item.HasEnd
        ? $"{Item.StartsAt:HH\\:mm} - {Item.EndLabel}"
        : $"{Item.StartsAt:HH\\:mm}";

    public string MetaLine
    {
        get
        {
            var city = string.IsNullOrWhiteSpace(Item.City) ? null : Item.City.Trim();
            return string.IsNullOrWhiteSpace(city)
                ? TimeLabel
                : $"{TimeLabel} · {city}";
        }
    }

    public string Body
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Item.Notes))
            {
                return Item.Notes.Trim();
            }

            if (!string.IsNullOrWhiteSpace(Item.SecondaryDetail))
            {
                return Item.SecondaryDetail.Trim();
            }

            return Item.MainDetail.Trim();
        }
    }

    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    public string DetailLine => Item.Type == ReservationType.Flight
        ? Item.MainDetail
        : Item.SecondaryDetail;

    public bool HasDetailLine => !string.IsNullOrWhiteSpace(DetailLine);

    public string ActionTitle => Item.Type == ReservationType.Flight
        ? Item.MainDetail
        : Item.LocationName;

    public bool HasActionTitle => !string.IsNullOrWhiteSpace(ActionTitle);

    public bool HasConfirmationCode => !string.IsNullOrWhiteSpace(Item.ConfirmationCode);
    public string ConfirmationLine => $"Codigo: {Item.ConfirmationCode}";
}

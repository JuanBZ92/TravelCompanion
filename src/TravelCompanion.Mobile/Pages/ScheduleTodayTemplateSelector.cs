using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public sealed class ScheduleTodayTemplateSelector : DataTemplateSelector
{
    public DataTemplate LocationTemplate { get; set; } = null!;
    public DataTemplate ReservationTemplate { get; set; } = null!;
    public DataTemplate ReservationHeadingTemplate { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item switch
    {
        TodayLocationViewModel => LocationTemplate,
        TodayReservationViewModel => ReservationTemplate,
        TodayReservationHeadingViewModel => ReservationHeadingTemplate,
        _ => throw new ArgumentException("Unknown itinerary row type.", nameof(item))
    };
}

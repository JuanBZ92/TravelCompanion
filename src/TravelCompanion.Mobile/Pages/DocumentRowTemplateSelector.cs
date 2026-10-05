using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public sealed class DocumentRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate PersonalDocument { get; set; } = null!;
    public DataTemplate IncludedDocument { get; set; } = null!;
    public DataTemplate FlightSummary { get; set; } = null!;
    public DataTemplate FlightJourney { get; set; } = null!;
    public DataTemplate FlightLeg { get; set; } = null!;
    public DataTemplate Hotel { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item switch
    {
        LocalDocumentItemViewModel => PersonalDocument,
        DocumentItemViewModel => IncludedDocument,
        FlightSummaryItemViewModel => FlightSummary,
        FlightJourneyItemViewModel => FlightJourney,
        FlightLegItemViewModel => FlightLeg,
        HotelItemViewModel => Hotel,
        _ => throw new ArgumentException("Unknown document row.", nameof(item))
    };
}

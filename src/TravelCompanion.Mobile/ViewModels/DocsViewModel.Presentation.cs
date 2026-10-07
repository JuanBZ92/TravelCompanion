using System.Collections.ObjectModel;
using System.Windows.Input;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class DocsViewModel
{
    private ObservableCollection<DocumentListGroup> _documentGroups = [];
    public ObservableCollection<DocumentListGroup> DocumentGroups
    {
        get => _documentGroups;
        private set => SetProperty(ref _documentGroups, value);
    }

    private void RebuildDocumentGroups()
    {
        var personal = LocalDocumentGroups.Select(group =>
            new DocumentListGroup(group.Name, group.Documents));
        var included = new List<DocumentListGroup>();
        if (Journeys.Count > 0)
        {
            var flights = new List<object>
            {
                new FlightSummaryItemViewModel(FlightAirline, FlightPassenger, FlightConfirmationCode, CopyCommand)
            };
            flights.AddRange(Journeys);
            if (SelectedJourney is { } journey) flights.AddRange(journey.Legs);
            included.Add(new(Text("UxFlightHeading"), flights));
        }
        included.Add(new(Text("UxHotelDocumentsHeading"), HotelDocuments));
        included.Add(new(Text("UxOtherDocumentsHeading"), OtherDocuments));
        included.Add(new(Text("UxHotelsHeading"), Hotels));
        // Replacing one observable source publishes the complete snapshot once.
        // Hundreds of documents do not generate hundreds of layout updates.
        DocumentGroups = new(DocumentListPresentation.Build(personal, included,
            SelectedCategory.HasValue, sessionService.HasCuratedDocs, sessionService.HasKnownValidAccess));
        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyBody));
    }
}

public sealed record FlightSummaryItemViewModel(
    string Airline, string Passenger, string? ConfirmationCode, ICommand CopyCommand)
{
    public bool HasConfirmationCode => !string.IsNullOrWhiteSpace(ConfirmationCode);
    public string CopyCodeText => Services.LocalizationResourceManager.Instance["CopyConfirmationCode"];
}

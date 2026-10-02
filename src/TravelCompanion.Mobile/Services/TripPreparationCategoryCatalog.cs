namespace TravelCompanion.Mobile.Services;

public sealed record TripPreparationCategoryDefinition(string Key, LocalDocumentCategory Category, string ResourceSuffix);

public static class TripPreparationCategoryCatalog
{
    public static IReadOnlyList<TripPreparationCategoryDefinition> All { get; } =
    [
        new("transport", LocalDocumentCategory.Transport, "Transport"),
        new("accommodation", LocalDocumentCategory.Accommodation, "Accommodation"),
        new("reservations", LocalDocumentCategory.Reservations, "Reservations"),
        new("travel-documents", LocalDocumentCategory.TravelDocuments, "TravelDocuments")
    ];

    public static TripPreparationCategoryDefinition ForKey(string key) => All.Single(item => item.Key == key);
    public static string? PreparationKey(LocalDocumentCategory category) =>
        All.FirstOrDefault(item => item.Category == category)?.Key;
}

public static class TripPreparationOrganizationPolicy
{
    public static int DocumentCount(LocalDocumentCategory category, IEnumerable<LocalTripDocument> documents) =>
        documents.Count(item => item.SourceUrl is null && (item.Category ?? LocalDocumentCategory.Other) == category);

    public static bool IsOrganized(PreparationManualState manualState, int documentCount) =>
        documentCount > 0 || manualState != PreparationManualState.Pending;
}

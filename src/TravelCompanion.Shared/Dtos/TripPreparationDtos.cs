namespace TravelCompanion.Shared.Dtos;

public static class TripPreparationKeys
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
        { "transport", "accommodation", "reservations", "travel-documents" });
}

public sealed record TripPreparationItemDto(string Key, bool Completed, int Revision);
public sealed record SaveTripPreparationItemRequest(bool Completed, int ExpectedRevision);

public static class EditorialReviewPolicy
{
    public static bool NeedsReview(DateOnly? reviewedOn, DateOnly today) =>
        reviewedOn is null || reviewedOn > today || reviewedOn < today.AddDays(-90);
}

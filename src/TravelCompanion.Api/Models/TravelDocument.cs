using TravelCompanion.Shared;

namespace TravelCompanion.Api.Models;

public sealed class TravelDocument
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public string? ExternalId { get; set; }
    public TravelDocumentCategory Category { get; set; } = TravelDocumentCategory.Other;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

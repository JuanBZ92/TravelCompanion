namespace TravelCompanion.Api.Models;

public sealed class JournalFreeEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TripId { get; set; }
    public string Title { get; set; } = "";
    public string Place { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Notes { get; set; } = "";
    public int Revision { get; set; }
    public Guid MutationId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool Deleted { get; set; }
}

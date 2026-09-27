namespace TravelCompanion.Api.Models;

// ActivityId deliberately has no foreign key: removing a plan must not remove a memory.
public sealed class JournalNote
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TripId { get; set; }
    public Guid ActivityId { get; set; }
    public string Title { get; set; } = "";
    public string City { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Notes { get; set; } = "";
    public int Revision { get; set; }
    public Guid MutationId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

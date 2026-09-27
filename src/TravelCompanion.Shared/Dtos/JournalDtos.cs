using System.ComponentModel.DataAnnotations;

namespace TravelCompanion.Shared.Dtos;

public sealed record JournalNoteDto(Guid ActivityId, Guid TripId, string Title, string City,
    DateOnly Date, string Notes, int Revision, DateTimeOffset UpdatedAt);

public sealed record SaveJournalNoteRequest(
    [param: MaxLength(2000)] string Notes,
    [param: Range(0, int.MaxValue)] int ExpectedRevision,
    Guid MutationId);

public sealed record JournalSaveResult(bool Saved, JournalNoteDto Entry);

using System.ComponentModel.DataAnnotations;

namespace TravelCompanion.Shared.Dtos;

public sealed record JournalFreeEntryDto(Guid Id, Guid TripId, string Title, string Place,
    DateOnly Date, string Notes, int Revision, DateTimeOffset UpdatedAt, bool Deleted);

public sealed record SaveJournalFreeEntryRequest(
    [param: MaxLength(120)] string Title,
    [param: MaxLength(200)] string Place,
    DateOnly Date,
    [param: MaxLength(2000)] string Notes,
    [param: Range(0, int.MaxValue)] int ExpectedRevision,
    Guid MutationId);

public sealed record DeleteJournalFreeEntryRequest(int ExpectedRevision, Guid MutationId);
public sealed record JournalFreeSaveResult(bool Saved, JournalFreeEntryDto Entry);

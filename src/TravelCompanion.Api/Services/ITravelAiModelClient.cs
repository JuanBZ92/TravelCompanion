using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface ITravelAiModelClient
{
    Task<TravelAiModelResult?> CreateStructuredResponseAsync(
        TravelAiModelRequest request,
        CancellationToken cancellationToken);
}

public sealed record TravelAiModelRequest(
    string ConversationId,
    string Intent,
    string UserMessage,
    string? Locale,
    TravelPreferenceProfile Profile,
    TravelPlanningContext PlanningContext,
    IReadOnlyList<Reservation> Reservations,
    IReadOnlyList<TravelCardDto> RankedCards,
    IReadOnlyList<string> SuggestedReplies);

public sealed record TravelAiModelResult(
    string Message,
    IReadOnlyList<string> SuggestedReplies);

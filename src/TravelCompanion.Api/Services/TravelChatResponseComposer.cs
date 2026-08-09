using System.Globalization;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface ITravelChatResponseComposer
{
    TravelChatResponse CreateHelpResponse(string conversationId, string? locale = null);

    TravelChatResponse MissingContext(
        string conversationId,
        string field,
        string message,
        IReadOnlyList<string> suggestions);

    IReadOnlyList<string> CreatePreferenceSuggestions(IReadOnlyList<string> missingFields, string? locale = null);

    TravelCardDto ToRecommendationCard(ScoredRecommendation scored, TravelPlanningContext context);

    string CreateAssistantMessage(
        string city,
        (TimeOnly Start, TimeOnly End, int AvailableMinutes) planningWindow,
        IReadOnlyList<ScoredRecommendation> ranked,
        string responseMode,
        string? locale = null);

    IReadOnlyList<string> CreateSuggestedReplies(string responseMode, string? locale = null);
}

public sealed class TravelChatResponseComposer(ITravelAssistantTextProvider textProvider) : ITravelChatResponseComposer
{
    public TravelChatResponse CreateHelpResponse(string conversationId, string? locale = null)
    {
        return new TravelChatResponse(
            conversationId,
            textProvider.HelpMessage(locale),
            TravelChatIntents.Help,
            [],
            textProvider.HelpReplies(locale),
            null);
    }

    public TravelChatResponse MissingContext(
        string conversationId,
        string field,
        string message,
        IReadOnlyList<string> suggestions)
    {
        return new TravelChatResponse(
            conversationId,
            message,
            TravelChatIntents.Plan,
            [],
            suggestions,
            new MissingContextDto(field, message, suggestions));
    }

    public IReadOnlyList<string> CreatePreferenceSuggestions(IReadOnlyList<string> missingFields, string? locale = null)
    {
        return textProvider.PreferenceSuggestions(missingFields, locale);
    }

    public TravelCardDto ToRecommendationCard(ScoredRecommendation scored, TravelPlanningContext context)
    {
        var recommendation = scored.Recommendation;
        return new TravelCardDto(
            "recommendation",
            recommendation.Title,
            FormatSubtitle(scored, recommendation),
            recommendation.Description,
            context.WindowStart?.ToString("HH:mm", CultureInfo.InvariantCulture),
            CalculateEndTime(context.WindowStart, recommendation.SuggestedDurationMinutes),
            recommendation.PriceLevel,
            scored.DistanceKm,
            scored.WalkingMinutes,
            scored.PositiveReasons.Take(3).ToList(),
            scored.NegativeReasons.ToList(),
            recommendation.Id.ToString(),
            null)
        {
            Tags = recommendation.Tags.ToList()
        };
    }

    public string CreateAssistantMessage(
        string city,
        (TimeOnly Start, TimeOnly End, int AvailableMinutes) planningWindow,
        IReadOnlyList<ScoredRecommendation> ranked,
        string responseMode,
        string? locale = null)
    {
        return textProvider.AssistantPlanMessage(city, planningWindow, ranked, responseMode, locale);
    }

    public IReadOnlyList<string> CreateSuggestedReplies(string responseMode, string? locale = null)
    {
        return textProvider.SuggestedReplies(responseMode, locale);
    }

    private static string FormatSubtitle(ScoredRecommendation scored, Recommendation recommendation)
    {
        if (scored.WalkingMinutes.HasValue)
        {
            return $"{scored.WalkingMinutes.Value} min caminando · {recommendation.Neighborhood}";
        }

        return $"{recommendation.SuggestedDurationMinutes} min · {recommendation.Neighborhood}";
    }

    private static string? CalculateEndTime(TimeOnly? start, int durationMinutes)
    {
        return start?.AddMinutes(durationMinutes).ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}

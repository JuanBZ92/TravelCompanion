using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface ITravelAssistantFeedbackService
{
    Task<TravelAssistantFeedbackResponse> RecordAsync(
        AppUser user,
        TravelAssistantFeedbackRequest request,
        CancellationToken cancellationToken);
}

public sealed class TravelAssistantFeedbackService(
    TravelCompanionDbContext dbContext,
    ITravelAssistantConversationStateService conversationStateService,
    TravelAssistantTelemetry telemetry,
    ILogger<TravelAssistantFeedbackService> logger) : ITravelAssistantFeedbackService
{
    public async Task<TravelAssistantFeedbackResponse> RecordAsync(
        AppUser user,
        TravelAssistantFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId))
        {
            telemetry.RecordFeedback(request.Signal, accepted: false, request.Locale, request.Intent, request.ResponseMode);
            return new TravelAssistantFeedbackResponse(false, "ConversationId is required.");
        }

        if (request.RecommendationId == Guid.Empty)
        {
            telemetry.RecordFeedback(request.Signal, accepted: false, request.Locale, request.Intent, request.ResponseMode);
            return new TravelAssistantFeedbackResponse(false, "RecommendationId is required.");
        }

        var recommendation = await dbContext.Recommendations
            .AsNoTracking()
            .Include(existing => existing.Packages)
            .FirstOrDefaultAsync(existing => existing.Id == request.RecommendationId, cancellationToken);
        if (recommendation is null || !CanAccessRecommendation(user, recommendation))
        {
            telemetry.RecordFeedback(request.Signal, accepted: false, request.Locale, request.Intent, request.ResponseMode);
            logger.LogWarning(
                "Travel assistant feedback rejected because recommendation is unavailable. UserId={UserId}; RecommendationId={RecommendationId}; Signal={Signal}.",
                user.Id,
                request.RecommendationId,
                request.Signal);
            return new TravelAssistantFeedbackResponse(false, "Recommendation is not available.");
        }

        var conversation = await conversationStateService.LoadAsync(
            request.ConversationId,
            user.Id,
            cancellationToken);
        if (conversation is null || conversation.UserId != user.Id)
        {
            telemetry.RecordFeedback(request.Signal, accepted: false, request.Locale, request.Intent, request.ResponseMode);
            return new TravelAssistantFeedbackResponse(false, "Conversation is not available.");
        }

        dbContext.TravelAssistantFeedbackItems.Add(new TravelAssistantFeedback
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ConversationId = request.ConversationId.Trim(),
            RecommendationId = recommendation.Id,
            Signal = request.Signal,
            Locale = TrimToNull(request.Locale),
            Intent = TrimToNull(request.Intent),
            ResponseMode = TrimToNull(request.ResponseMode),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        if (request.Signal == TravelAssistantFeedbackSignal.HideSimilar)
        {
            await conversationStateService.AddHiddenTagsAsync(
                request.ConversationId,
                user.Id,
                CreateSimilarityTags(recommendation),
                cancellationToken);
        }

        telemetry.RecordFeedback(request.Signal, accepted: true, request.Locale, request.Intent, request.ResponseMode);
        return new TravelAssistantFeedbackResponse(true, CreateAcceptedMessage(request.Signal, request.Locale));
    }

    private static bool CanAccessRecommendation(AppUser user, Recommendation recommendation)
    {
        var now = DateTimeOffset.UtcNow;
        var entitlements = new UserEntitlementsDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Entitlements
                .Where(entitlement => entitlement.ExpiresAt is null || entitlement.ExpiresAt > now)
                .Select(entitlement => entitlement.AccessLevel)
                .Distinct()
                .ToList(),
            user.Entitlements
                .Where(entitlement => entitlement.DestinationId.HasValue
                    && (entitlement.ExpiresAt is null || entitlement.ExpiresAt > now))
                .Select(entitlement => entitlement.DestinationId!.Value)
                .Distinct()
                .ToList(),
            user.Entitlements
                .Where(entitlement => entitlement.TravelPackageId.HasValue
                    && (entitlement.ExpiresAt is null || entitlement.ExpiresAt > now))
                .Select(entitlement => entitlement.TravelPackageId!.Value)
                .Distinct()
                .ToList(),
            user.Entitlements
                .Where(entitlement => entitlement.ExpiresAt is null || entitlement.ExpiresAt > now)
                .Select(entitlement => new UserEntitlementDto(
                    entitlement.Id,
                    entitlement.AccessLevel,
                    entitlement.DestinationId,
                    entitlement.TravelPackageId,
                    entitlement.GrantedAt,
                    entitlement.ExpiresAt,
                    entitlement.Source))
                .ToList());

        return ContentAccessPolicy.IsRecommendationUnlocked(
            entitlements,
            recommendation.AccessLevel,
            recommendation.DestinationId,
            recommendation.Packages.Select(package => package.Id).ToList());
    }

    private static IReadOnlyList<string> CreateSimilarityTags(Recommendation recommendation)
    {
        return recommendation.Tags
            .Append(recommendation.Category)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string CreateAcceptedMessage(TravelAssistantFeedbackSignal signal, string? locale)
    {
        var spanish = string.IsNullOrWhiteSpace(locale)
            || locale.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        return signal switch
        {
            TravelAssistantFeedbackSignal.HideSimilar => spanish
                ? "Lo tendre en cuenta para esta conversacion."
                : "I will keep that in mind for this conversation.",
            TravelAssistantFeedbackSignal.NotHelpful => spanish
                ? "Gracias, registre que no fue util."
                : "Thanks, I recorded that it was not helpful.",
            _ => spanish
                ? "Gracias, registre tu feedback."
                : "Thanks, I recorded your feedback."
        };
    }
}

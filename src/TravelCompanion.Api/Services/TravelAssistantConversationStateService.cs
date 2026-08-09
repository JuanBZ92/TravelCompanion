using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface ITravelAssistantConversationStateService
{
    Task<TravelChatConversation?> LoadAsync(string conversationId, Guid userId, CancellationToken cancellationToken);
    TravelAssistantConversationState ReadState(TravelChatConversation? conversation);
    Task SavePendingPreferencePatchAsync(
        TravelChatConversation? conversation,
        string conversationId,
        Guid userId,
        string? message,
        TravelPreferenceProfilePatchDto patch,
        CancellationToken cancellationToken);
    Task ClearPendingPreferencePatchAsync(TravelChatConversation? conversation, CancellationToken cancellationToken);
    Task SavePlanningStateAsync(
        TravelChatConversation? conversation,
        string conversationId,
        Guid userId,
        TravelAssistantConversationState state,
        CancellationToken cancellationToken);
    Task AddHiddenTagsAsync(
        string conversationId,
        Guid userId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken);
}

public sealed class TravelAssistantConversationState
{
    public string? LastIntent { get; set; }
    public string? LastLocale { get; set; }
    public string? LastResponseMode { get; set; }
    public string? LastCity { get; set; }
    public DateOnly? LastDate { get; set; }
    public List<string> LastRecommendationIds { get; set; } = [];
    public List<string> HiddenTags { get; set; } = [];
    public string? PromptVersion { get; set; }
}

public sealed class TravelAssistantConversationStateService(
    TravelCompanionDbContext dbContext,
    ILogger<TravelAssistantConversationStateService> logger) : ITravelAssistantConversationStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TravelChatConversation?> LoadAsync(
        string conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.TravelChatConversations
            .FirstOrDefaultAsync(existing => existing.Id == conversationId, cancellationToken);

        if (conversation is null || conversation.UserId == userId)
        {
            return conversation;
        }

        logger.LogWarning(
            "Ignoring travel chat conversation {ConversationId} because it belongs to another user.",
            conversationId);
        return conversation;
    }

    public TravelAssistantConversationState ReadState(TravelChatConversation? conversation)
    {
        if (conversation is null || string.IsNullOrWhiteSpace(conversation.StateJson))
        {
            return CreateStateFromConversation(conversation);
        }

        try
        {
            var state = JsonSerializer.Deserialize<TravelAssistantConversationState>(
                conversation.StateJson,
                JsonOptions);
            return state is null
                ? CreateStateFromConversation(conversation)
                : MergeConversationColumns(conversation, state);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Travel assistant conversation state could not be read. ConversationId={ConversationId}.", conversation.Id);
            return CreateStateFromConversation(conversation);
        }
    }

    public async Task SavePendingPreferencePatchAsync(
        TravelChatConversation? conversation,
        string conversationId,
        Guid userId,
        string? message,
        TravelPreferenceProfilePatchDto patch,
        CancellationToken cancellationToken)
    {
        var isNewConversation = conversation is null;
        conversation = EnsureConversation(conversation, conversationId, userId);
        if (isNewConversation)
        {
            dbContext.TravelChatConversations.Add(conversation);
        }

        conversation.PendingPreferencePatchJson = JsonSerializer.Serialize(patch, JsonOptions);
        conversation.PendingPreferenceOriginalMessage = string.IsNullOrWhiteSpace(message)
            ? null
            : message.Trim();
        conversation.PendingPreferenceRequestedAt = DateTimeOffset.UtcNow;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearPendingPreferencePatchAsync(
        TravelChatConversation? conversation,
        CancellationToken cancellationToken)
    {
        if (conversation is null)
        {
            return;
        }

        conversation.PendingPreferencePatchJson = null;
        conversation.PendingPreferenceOriginalMessage = null;
        conversation.PendingPreferenceRequestedAt = null;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SavePlanningStateAsync(
        TravelChatConversation? conversation,
        string conversationId,
        Guid userId,
        TravelAssistantConversationState state,
        CancellationToken cancellationToken)
    {
        var isNewConversation = conversation is null;
        conversation = EnsureConversation(conversation, conversationId, userId);
        if (isNewConversation)
        {
            dbContext.TravelChatConversations.Add(conversation);
        }

        conversation.LastCity = state.LastCity;
        conversation.LastDate = state.LastDate;
        conversation.LastIntent = state.LastIntent;
        conversation.LastLocale = state.LastLocale;
        conversation.LastPromptVersion = state.PromptVersion;
        conversation.LastResponseMode = string.IsNullOrWhiteSpace(state.LastResponseMode)
            ? "balanced"
            : state.LastResponseMode;
        conversation.LastRecommendationIds = string.Join(",", state.LastRecommendationIds);
        conversation.StateJson = JsonSerializer.Serialize(Sanitize(state), JsonOptions);
        conversation.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddHiddenTagsAsync(
        string conversationId,
        Guid userId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || tags.Count == 0)
        {
            return;
        }

        var conversation = await LoadAsync(conversationId, userId, cancellationToken);
        if (conversation is null || conversation.UserId != userId)
        {
            return;
        }

        var state = ReadState(conversation);
        foreach (var tag in tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim()))
        {
            if (!state.HiddenTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                state.HiddenTags.Add(tag);
            }
        }

        await SavePlanningStateAsync(conversation, conversationId, userId, state, cancellationToken);
    }

    private static TravelChatConversation EnsureConversation(
        TravelChatConversation? conversation,
        string conversationId,
        Guid userId)
    {
        if (conversation is not null)
        {
            return conversation;
        }

        return new TravelChatConversation
        {
            Id = conversationId,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static TravelAssistantConversationState CreateStateFromConversation(
        TravelChatConversation? conversation)
    {
        return new TravelAssistantConversationState
        {
            LastIntent = conversation?.LastIntent,
            LastLocale = conversation?.LastLocale,
            LastResponseMode = conversation?.LastResponseMode,
            LastCity = conversation?.LastCity,
            LastDate = conversation?.LastDate,
            LastRecommendationIds = ParseRecommendationIds(conversation?.LastRecommendationIds),
            PromptVersion = conversation?.LastPromptVersion
        };
    }

    private static TravelAssistantConversationState MergeConversationColumns(
        TravelChatConversation conversation,
        TravelAssistantConversationState state)
    {
        state.LastIntent ??= conversation.LastIntent;
        state.LastLocale ??= conversation.LastLocale;
        state.LastResponseMode ??= conversation.LastResponseMode;
        state.LastCity ??= conversation.LastCity;
        state.LastDate ??= conversation.LastDate;
        state.PromptVersion ??= conversation.LastPromptVersion;
        if (state.LastRecommendationIds.Count == 0)
        {
            state.LastRecommendationIds = ParseRecommendationIds(conversation.LastRecommendationIds);
        }

        return Sanitize(state);
    }

    private static TravelAssistantConversationState Sanitize(TravelAssistantConversationState state)
    {
        state.LastRecommendationIds = state.LastRecommendationIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
        state.HiddenTags = state.HiddenTags
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
        return state;
    }

    private static List<string> ParseRecommendationIds(string? recommendationIds)
    {
        if (string.IsNullOrWhiteSpace(recommendationIds))
        {
            return [];
        }

        return recommendationIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

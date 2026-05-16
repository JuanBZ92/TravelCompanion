using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class OpenAiTravelModelClient : ITravelAiModelClient
{
    private const string GetUserProfileTool = "get_user_profile";
    private const string GetUserReservationsTool = "get_user_reservations";
    private const string RankRecommendationsTool = "rank_recommendations";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OpenAiTravelOptions _options;
    private readonly ILogger<OpenAiTravelModelClient> _logger;
    private readonly ChatClient? _chatClient;

    public OpenAiTravelModelClient(
        IOptions<OpenAiTravelOptions> options,
        ILogger<OpenAiTravelModelClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (_options.Enabled && !string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _chatClient = new ChatClient(_options.Model, _options.ApiKey);
        }
    }

    public async Task<TravelAiModelResult?> CreateStructuredResponseAsync(
        TravelAiModelRequest request,
        CancellationToken cancellationToken)
    {
        if (_chatClient is null)
        {
            return null;
        }

        try
        {
            var messages = CreateMessages(request);
            var options = CreateCompletionOptions(requireToolCall: true);

            var completion = (await _chatClient.CompleteChatAsync(
                messages,
                options,
                cancellationToken)).Value;

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                messages.Add(new AssistantChatMessage(completion));

                foreach (var toolCall in completion.ToolCalls)
                {
                    messages.Add(new ToolChatMessage(
                        toolCall.Id,
                        ResolveToolCall(toolCall, request)));
                }

                options.ToolChoice = ChatToolChoice.CreateNoneChoice();
                completion = (await _chatClient.CompleteChatAsync(
                    messages,
                    options,
                    cancellationToken)).Value;
            }

            if (completion.Content.Count == 0
                || string.IsNullOrWhiteSpace(completion.Content[0].Text))
            {
                return null;
            }

            var draft = JsonSerializer.Deserialize<ModelResponseDraft>(
                completion.Content[0].Text,
                JsonOptions);

            if (draft is null || string.IsNullOrWhiteSpace(draft.Message))
            {
                return null;
            }

            return new TravelAiModelResult(
                draft.Message.Trim(),
                CleanSuggestedReplies(draft.SuggestedReplies));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI travel chat orchestration failed; using deterministic fallback.");
            return null;
        }
    }

    private static List<ChatMessage> CreateMessages(TravelAiModelRequest request)
    {
        return
        [
            new SystemChatMessage("""
                You are a travel assistant inside a mobile app.

                Rules:
                - Use only tool results as source of truth.
                - Do not invent reservations, prices, opening hours, distances, preferences, or booking status.
                - Keep replies concise for mobile.
                - Explain the plan using concrete reasons from ranked recommendations.
                - Return only JSON that matches the requested schema.
                - Do not include sensitive data such as confirmation codes or internal notes.
                """),
            new UserChatMessage($"""
                Traveler message: {request.UserMessage}
                Locale: {request.Locale ?? "es-ES"}
                Intent: {request.Intent}
                Conversation: {request.ConversationId}

                First inspect the available travel tools. Then write a concise assistant message and suggested replies.
                The backend will render deterministic cards separately, so do not create new places or reservations.
                """)
        ];
    }

    private ChatCompletionOptions CreateCompletionOptions(bool requireToolCall)
    {
        return new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.MaxOutputTokenCount,
            ToolChoice = requireToolCall
                ? ChatToolChoice.CreateRequiredChoice()
                : ChatToolChoice.CreateAutoChoice(),
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "travel_chat_response_draft",
                jsonSchema: BinaryData.FromString("""
                    {
                      "type": "object",
                      "properties": {
                        "message": {
                          "type": "string",
                          "description": "Concise mobile-friendly assistant message."
                        },
                        "suggestedReplies": {
                          "type": "array",
                          "items": { "type": "string" },
                          "description": "Short follow-up actions for the user."
                        }
                      },
                      "required": ["message", "suggestedReplies"],
                      "additionalProperties": false
                    }
                    """),
                jsonSchemaIsStrict: true),
            Tools =
            {
                ChatTool.CreateFunctionTool(
                    GetUserProfileTool,
                    "Returns the authenticated traveler's safe preference profile.",
                    EmptyParametersSchema(),
                    functionSchemaIsStrict: true),
                ChatTool.CreateFunctionTool(
                    GetUserReservationsTool,
                    "Returns the authenticated traveler's reservations for the planning date without sensitive fields.",
                    EmptyParametersSchema(),
                    functionSchemaIsStrict: true),
                ChatTool.CreateFunctionTool(
                    RankRecommendationsTool,
                    "Returns backend-ranked recommendation cards with deterministic fit reasons.",
                    EmptyParametersSchema(),
                    functionSchemaIsStrict: true)
            }
        };
    }

    private static BinaryData EmptyParametersSchema()
    {
        return BinaryData.FromString("""
            {
              "type": "object",
              "properties": {},
              "required": [],
              "additionalProperties": false
            }
            """);
    }

    private static string ResolveToolCall(ChatToolCall toolCall, TravelAiModelRequest request)
    {
        var payload = toolCall.FunctionName switch
        {
            GetUserProfileTool => CreateProfileToolResult(request.Profile),
            GetUserReservationsTool => CreateReservationsToolResult(request.Reservations, request.PlanningContext),
            RankRecommendationsTool => CreateRankedRecommendationsToolResult(request.RankedCards),
            _ => new { error = "Unsupported tool." }
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static object CreateProfileToolResult(TravelPreferenceProfile profile)
    {
        return new
        {
            profile.BudgetLevel,
            profile.TravelPace,
            profile.MaxWalkingMinutes,
            profile.Interests,
            profile.FoodPreferences,
            profile.DietaryRestrictions,
            profile.Dislikes
        };
    }

    private static object CreateReservationsToolResult(
        IReadOnlyList<Reservation> reservations,
        TravelPlanningContext context)
    {
        return new
        {
            context.City,
            context.Date,
            windowStart = context.WindowStart?.ToString("HH:mm"),
            windowEnd = context.WindowEnd?.ToString("HH:mm"),
            context.AvailableMinutes,
            reservations = reservations.Select(reservation => new
            {
                reservation.Type,
                reservation.Title,
                reservation.City,
                reservation.LocationName,
                reservation.Address,
                reservation.Date,
                startsAt = reservation.StartsAt.ToString("HH:mm"),
                endsAt = reservation.EndsAt?.ToString("HH:mm")
            })
        };
    }

    private static object CreateRankedRecommendationsToolResult(IReadOnlyList<TravelCardDto> cards)
    {
        return new
        {
            cards = cards.Select(card => new
            {
                card.Type,
                card.Title,
                card.Subtitle,
                card.Description,
                card.StartTime,
                card.EndTime,
                card.EstimatedCost,
                card.DistanceKm,
                card.WalkingMinutes,
                card.WhyItFits,
                card.Warnings,
                card.RecommendationId
            })
        };
    }

    private static IReadOnlyList<string> CleanSuggestedReplies(IReadOnlyList<string> replies)
    {
        return replies
            .Where(reply => !string.IsNullOrWhiteSpace(reply))
            .Select(reply => reply.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }

    private sealed record ModelResponseDraft(
        string Message,
        IReadOnlyList<string> SuggestedReplies);
}

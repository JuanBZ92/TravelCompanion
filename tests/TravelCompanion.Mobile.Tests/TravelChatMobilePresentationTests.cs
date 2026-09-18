using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class TravelChatMobilePresentationTests
{
    [Fact]
    public void Normalize_travel_chat_response_handles_malformed_backend_payload()
    {
        var response = new TravelChatResponse(
            null!,
            null!,
            null!,
            null!,
            null!,
            new MissingContextDto(null!, null!, null!));

        var normalized = MobilePayloadNormalizer.Normalize(response);

        Assert.NotNull(normalized);
        Assert.Equal(string.Empty, normalized.ConversationId);
        Assert.Equal(string.Empty, normalized.Message);
        Assert.Equal(string.Empty, normalized.Intent);
        Assert.Empty(normalized.Cards);
        Assert.Empty(normalized.SuggestedReplies);
        Assert.NotNull(normalized.MissingContext);
        Assert.Equal(string.Empty, normalized.MissingContext.Field);
        Assert.Equal(string.Empty, normalized.MissingContext.Message);
        Assert.Empty(normalized.MissingContext.Suggestions);
    }

    [Fact]
    public void TravelChatCardViewModel_exposes_actionable_card_state()
    {
        var recommendationId = Guid.NewGuid();
        var card = new TravelCardDto(
            "recommendation",
            "Tsukiji Snack Walk",
            "1 min caminando",
            "Local snacks before dinner.",
            "10:30",
            "11:30",
            "medium",
            1.2,
            14,
            ["Encaja con tus intereses.", "Esta cerca.", "Tercera razon."],
            ["Puede haber fila.", "Segundo warning."],
            recommendationId.ToString(),
            null)
        {
            Tags = ["food", "local food", "vegetarian", "market", "extra"]
        };

        var viewModel = new TravelChatCardViewModel(card);

        Assert.Equal("Tsukiji Snack Walk", viewModel.Title);
        Assert.Equal("Time: 10:30 - 11:30", viewModel.TimeLabel);
        Assert.Equal("Cost: Medium", viewModel.CostLabel);
        Assert.StartsWith("Distance:", viewModel.DistanceLabel);
        Assert.Equal("Walk: 14 min", viewModel.WalkingLabel);
        Assert.True(viewModel.CanSave);
        Assert.True(viewModel.HasDetailAction);
        Assert.Equal("Save", viewModel.SaveButtonText);
        Assert.Equal(4, viewModel.Tags.Count);
        Assert.DoesNotContain("extra", viewModel.Tags);
        Assert.Contains(viewModel.TagActions, tag => tag.Label == "Avoid #food");
        Assert.Equal(2, viewModel.WhyItFits.Count);
        Assert.Single(viewModel.Warnings);

        viewModel.IsSaved = true;

        Assert.False(viewModel.CanSave);
        Assert.Equal("Saved", viewModel.SaveButtonText);
    }

    [Fact]
    public void Recommendation_message_shows_the_card_without_repeating_the_introductory_text()
    {
        var recommendation = new TravelChatCardViewModel(new TravelCardDto(
            "recommendation",
            "Local ramen",
            "10 min caminando",
            "Una opción local.",
            "12:00",
            "13:00",
            "low",
            0.8,
            10,
            [],
            [],
            Guid.NewGuid().ToString(),
            null));

        var message = new TravelChatMessageViewModel(
            "Busqué una opción para tu ventana disponible.",
            isFromUser: false,
            [recommendation]);

        Assert.True(message.HasCards);
        Assert.False(message.ShouldShowText);
    }

    [Fact]
    public void Recommendation_message_replaces_only_the_selected_card()
    {
        static TravelChatCardViewModel Card(string title) => new(new TravelCardDto(
            "recommendation",
            title,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            Guid.NewGuid().ToString(),
            null));

        var first = Card("First");
        var second = Card("Second");
        var replacement = Card("Replacement");
        var message = new TravelChatMessageViewModel(string.Empty, false, [first, second]);

        Assert.True(message.ReplaceCard(first, replacement));
        Assert.Equal(["Replacement", "Second"], message.Cards.Select(card => card.Title));
        Assert.True(replacement.CanSave);
    }

    [Fact]
    public void Normalize_travel_chat_response_preserves_safe_guided_question()
    {
        var response = new TravelChatResponse(
            "conversation",
            "Choose",
            "plan_between_reservations",
            [],
            [],
            null,
            new GuidedQuestionDto(
                "priority",
                "What matters most?",
                [new GuidedOptionDto("priority.budget", "Budget")]),
            new GuidedPlanCriteriaDto("food"));

        var normalized = MobilePayloadNormalizer.Normalize(response);

        Assert.Equal("priority", normalized!.GuidedQuestion?.Id);
        Assert.Equal("priority.budget", normalized.GuidedQuestion?.Options.Single().Id);
        Assert.Equal("food", normalized.Criteria?.Category);
    }
}

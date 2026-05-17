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
        Assert.Equal("Horario: 10:30 - 11:30", viewModel.TimeLabel);
        Assert.Equal("Coste: Medio", viewModel.CostLabel);
        Assert.StartsWith("Distancia:", viewModel.DistanceLabel);
        Assert.Equal("Caminata: 14 min", viewModel.WalkingLabel);
        Assert.True(viewModel.CanSave);
        Assert.True(viewModel.HasDetailAction);
        Assert.Equal("Guardar", viewModel.SaveButtonText);
        Assert.Equal(4, viewModel.Tags.Count);
        Assert.DoesNotContain("extra", viewModel.Tags);
        Assert.Contains(viewModel.TagActions, tag => tag.Label == "Evitar #food");
        Assert.Equal(2, viewModel.WhyItFits.Count);
        Assert.Single(viewModel.Warnings);

        viewModel.IsSaved = true;

        Assert.False(viewModel.CanSave);
        Assert.Equal("Guardado", viewModel.SaveButtonText);
    }
}

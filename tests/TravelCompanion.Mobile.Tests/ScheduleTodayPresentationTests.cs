using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class ScheduleTodayPresentationTests
{
    [Fact]
    public void Assigned_recommendation_is_rendered_as_a_curated_location()
    {
        var recommendation = new RecommendationDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Ramen One",
            "Food",
            "Tokyo, Japan",
            "Curated ramen stop.",
            ["food", "ramen"],
            "medium",
            35.67m,
            139.76m,
            60,
            4.2,
            null,
            ContentAccessLevel.Free,
            [],
            1.2m);
        var dto = new TodayRecommendationDto(
            recommendation,
            1.2m,
            "Seleccionada para este bloque",
            false,
            null,
            "Tarde",
            IsAssigned: true);

        var viewModel = new TodayLocationViewModel(dto);

        Assert.True(viewModel.IsAssigned);
        Assert.True(viewModel.HasAssignmentLabel);
        Assert.Equal("RECOMENDACION CURADA", viewModel.AssignmentLabel);
        Assert.False(viewModel.CanDismiss);
        Assert.False(viewModel.CanMarkVisited);
        Assert.False(viewModel.HasVisitStatus);
        Assert.Equal("Ramen", viewModel.RefinedCategory);
        Assert.DoesNotContain("Food", viewModel.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Automatic_recommendation_uses_specific_tag_and_keeps_visit_action()
    {
        var recommendation = new RecommendationDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Tempura Kondo",
            "Food",
            "Ginza, Tokyo",
            "Tempura recomendado.",
            ["food", "tempura", "reservation recommended"],
            "high",
            35.67m,
            139.76m,
            90,
            4.3,
            null,
            ContentAccessLevel.Free,
            [],
            0.8m);
        var dto = new TodayRecommendationDto(
            recommendation,
            0.8m,
            "Encaja con noche",
            false,
            null,
            "Noche");

        var viewModel = new TodayLocationViewModel(dto);

        Assert.Equal("Tempura", viewModel.RefinedCategory);
        Assert.StartsWith("Tempura ·", viewModel.Detail);
        Assert.True(viewModel.CanMarkVisited);
        Assert.False(viewModel.HasRankReason);
    }
}

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
        Assert.False(viewModel.CanRemove);
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

    [Fact]
    public void Traveler_owned_recommendation_can_be_removed_from_paid_itinerary()
    {
        var recommendation = CreateRecommendation("Sushi Test", "sushi");
        var item = new ScheduleItemDto(
            Guid.NewGuid(), recommendation.Id, ReservationType.Event,
            new DateOnly(2026, 10, 1), new TimeOnly(12, 0), null, null,
            recommendation.Title, "Tokyo", recommendation.Title, recommendation.Neighborhood,
            string.Empty, string.Empty, null, null, null, null, null, null,
            ScheduleItemKind.Recommendation, ItineraryItemOwner.Traveler,
            ItineraryItemSource.YukuRecommendation, ItineraryTimePrecision.PeriodOnly);

        var viewModel = new TodayLocationViewModel(
            recommendation,
            0.5m,
            isAssigned: true,
            assignedItem: item);

        Assert.True(viewModel.CanRemove);
        Assert.False(viewModel.CanDismiss);
    }

    [Fact]
    public void Empty_paid_or_premium_block_is_labeled_libre()
    {
        var section = new ScheduleTodaySectionViewModel(
            1,
            new DateOnly(2026, 10, 1),
            "morning",
            "Mañana",
            "Texto temporal del backend",
            [],
            []);

        Assert.False(section.HasContent);
        Assert.Equal("Libre", section.Description);
    }

    private static RecommendationDto CreateRecommendation(string title, string tag) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        title,
        "Food",
        "Tokyo, Japan",
        "Descripcion curada.",
        ["food", tag],
        "medium",
        35.67m,
        139.76m,
        60,
        4.2,
        null,
        ContentAccessLevel.Free,
        [],
        0.5m);
}

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
    }
}

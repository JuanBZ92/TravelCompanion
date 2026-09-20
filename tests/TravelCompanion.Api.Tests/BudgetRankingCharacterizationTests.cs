using System.Reflection;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class BudgetRankingCharacterizationTests
{
    public static TheoryData<string?, double> Prices => new()
    {
        { null, 13 }, { "", 13 }, { "  ", 13 }, { "unknown", 13 },
        { " FREE ", 35 }, { "gratis", 35 }, { "low", 35 }, { "budget", 35 },
        { "cheap", 35 }, { "barato", 35 }, { "medium", 13 }, { "moderate", 13 },
        { "medio", 13 }, { "high", 5 }, { "expensive", 5 }, { "premium", 5 }, { "alto", 5 }
    };

    [Theory]
    [MemberData(nameof(Prices))]
    public void Rank_preserves_budget_aliases_scores_and_title_tiebreaks(string? price, double expectedScore)
    {
        var profile = new TravelPreferenceProfile { BudgetLevel = "low" };
        var ranked = new DeterministicRecommendationRanker().Rank(profile, [],
            [Place("B", price), Place("A", price)],
            new TravelPlanningContext("Tokyo", new(2026, 10, 1), null, null, null, null));

        Assert.Equal(new[] { "A", "B" }, ranked.Select(item => item.Recommendation.Title));
        Assert.All(ranked, item => Assert.Equal(expectedScore, item.Score));
        Assert.All(ranked, item => Assert.Null(item.DistanceKm));
    }

    [Theory]
    [InlineData(TravelChatResponseModes.Cheaper, "Free,Low B,Low A,Medium,High")]
    [InlineData(TravelChatResponseModes.MediumCost, "Medium,Low B,Low A,High,Free")]
    [InlineData(TravelChatResponseModes.HighCost, "High,Medium,Low B,Low A,Free")]
    public void Response_mode_preserves_budget_order_and_score_tiebreaks(string mode, string expected)
    {
        var candidates = Candidates();
        var result = Invoke("ApplyResponseMode", candidates, mode);
        Assert.Equal(expected.Split(','), result.Select(item => item.Recommendation.Title));
        Assert.Equal(new[] { 50d, 40d, 30d, 20d, 10d }, candidates.Select(item => item.Score));
    }

    [Fact]
    public void Multiple_budgets_filter_without_repricing_or_changing_scores()
    {
        var result = Invoke("ApplyGuidedCriteria", Candidates(),
            new GuidedPlanCriteriaDto { Budgets = ["low", "high"] });
        Assert.Equal(new[] { "Low B", "Low A", "Free", "High" }, result.Select(item => item.Recommendation.Title));
        Assert.Equal(new[] { 50d, 40d, 20d, 10d }, result.Select(item => item.Score));
    }

    private static IReadOnlyList<ScoredRecommendation> Candidates() =>
    [
        new(Place("Low B", "cheap"), 50, null, null, [], []),
        new(Place("Low A", "barato"), 40, null, null, [], []),
        new(Place("Medium", "unknown"), 30, null, null, [], []),
        new(Place("Free", "gratis"), 20, null, null, [], []),
        new(Place("High", "premium"), 10, null, null, [], [])
    ];

    private static IEnumerable<ScoredRecommendation> Invoke(string name, params object[] args) =>
        (IEnumerable<ScoredRecommendation>)typeof(TravelRecommendationPlanningService)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;

    private static Recommendation Place(string title, string? price) => new()
    {
        Id = Guid.NewGuid(), Title = title, Category = "Art", Neighborhood = "Tokyo",
        Description = "Gallery", PriceLevel = price!
    };
}

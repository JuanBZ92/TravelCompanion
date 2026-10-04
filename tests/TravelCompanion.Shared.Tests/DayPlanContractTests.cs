using System.Text.Json;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared.Tests;

public sealed class DayPlanContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Minimal_mobile_request_keeps_optional_preferences_and_locale_unset()
    {
        var trip = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var request = JsonSerializer.Deserialize<DayPlanRequest>($$"""
            {"tripId":"{{trip}}","expectedRevision":4,"startDate":"2026-10-20","dayCount":3,"operationId":"{{operation}}"}
            """, Web)!;
        Assert.Equal(trip, request.TripId);
        Assert.Equal(operation, request.OperationId);
        Assert.Equal(new DateOnly(2026, 10, 20), request.StartDate);
        Assert.Null(request.Preferences);
        Assert.Null(request.Locale);
    }

    [Fact]
    public void Stored_proposal_round_trip_preserves_selection_ids_dates_period_and_warning()
    {
        var stop = Guid.NewGuid();
        var recommendation = Guid.NewGuid();
        var card = new TravelCardDto("recommendation", "Kyoto museum", "Kyoto", "Culture", "09:00", "10:00",
            "¥1000", null, null, ["Matches your interests"], ["Check opening hours"], recommendation.ToString(), null)
        { IsPeriodOnly = true, IsDayPlan = true, PeriodKey = "morning" };
        var response = new DayPlanResponse(Guid.NewGuid(), Guid.NewGuid(), 4,
            [new(new(2026, 10, 20), ["Kyoto"], [new(stop, recommendation, "morning", card)], ["evening"])], "Your proposal");
        var restored = JsonSerializer.Deserialize<DayPlanResponse>(JsonSerializer.Serialize(response, Web), Web)!;
        var day = Assert.Single(restored.Days);
        var item = Assert.Single(day.Stops);
        Assert.Equal(stop, item.Id);
        Assert.Equal(recommendation, item.RecommendationId);
        Assert.Equal(new DateOnly(2026, 10, 20), day.Date);
        Assert.Equal("morning", item.PeriodKey);
        Assert.True(item.Card.IsPeriodOnly);
        Assert.True(item.Card.IsDayPlan);
        Assert.Equal("Check opening hours", Assert.Single(item.Card.Warnings));
        Assert.Equal("evening", Assert.Single(day.MissingMoments));
    }

    [Fact]
    public void Apply_contract_contains_selection_and_revision_without_client_supplied_place_data()
    {
        var request = new DayPlanApplyRequest(Guid.NewGuid(), Guid.NewGuid(), 7, Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()]);
        var json = JsonSerializer.Serialize(request, Web);
        var restored = JsonSerializer.Deserialize<DayPlanApplyRequest>(json, Web)!;
        Assert.Equal(request.SelectedStopIds, restored.SelectedStopIds);
        Assert.Equal(request.MutationId, restored.MutationId);
        Assert.Equal(7, restored.ExpectedRevision);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(["operationId", "tripId", "expectedRevision", "mutationId", "selectedStopIds", "locale"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
    }
}

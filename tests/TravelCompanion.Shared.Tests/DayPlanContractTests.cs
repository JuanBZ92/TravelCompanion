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
    public void Legacy_options_without_city_days_keep_an_empty_compatible_default()
    {
        var options = JsonSerializer.Deserialize<DayPlanOptionsDto>("""
            {"enabled":true,"tripId":null,"revision":0,"startsOn":"2026-10-20","endsOn":"2026-10-26",
             "dayCounts":[1,3],"profile":null}
            """, Web)!;

        Assert.Empty(options.CityDays);
        Assert.Equal([1, 3], options.DayCounts);
    }

    [Fact]
    public void City_days_round_trip_preserves_dates_and_transfer_city_order()
    {
        var cityDay = new DayPlanCityDayDto(new(2026, 10, 23), ["Tokyo", "Kyoto"]);
        var options = new DayPlanOptionsDto(true, Guid.NewGuid(), 2, new(2026, 10, 20),
            new(2026, 10, 26), [1, 3, 5, 7],
            new(Guid.NewGuid(), [], [], "medium", "balanced", [], [], true, 25, false, [], null))
        { CityDays = [cityDay] };

        var restored = JsonSerializer.Deserialize<DayPlanOptionsDto>(JsonSerializer.Serialize(options, Web), Web)!;

        var restoredDay = Assert.Single(restored.CityDays);
        Assert.Equal(cityDay.Date, restoredDay.Date);
        Assert.Equal(cityDay.Cities, restoredDay.Cities);
        Assert.Equal(options.TripId, restored.TripId);
        Assert.Equal(options.Revision, restored.Revision);
    }

    [Fact]
    public void Legacy_proposal_without_place_keeps_the_optional_field_unset()
    {
        var stop = new DayPlanStopDto(Guid.NewGuid(), Guid.NewGuid(), "morning",
            new("recommendation", "Museum", "Morning visit", "Culture", null, null, null, null, null,
                [], [], null, null));
        var legacyOptions = new JsonSerializerOptions(Web)
        { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var legacyJson = JsonSerializer.Serialize(stop, legacyOptions);

        Assert.DoesNotContain("\"place\"", legacyJson);
        var restored = JsonSerializer.Deserialize<DayPlanStopDto>(legacyJson, Web)!;
        Assert.Null(restored.Place);
        Assert.Equal(stop.Id, restored.Id);
        Assert.Equal("Morning visit", restored.Card.Subtitle);
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
            [new(new(2026, 10, 20), ["Kyoto"], [new(stop, recommendation, "morning", card) { Place = "Gion, Kyoto" }], ["evening"])], "Your proposal");
        var restored = JsonSerializer.Deserialize<DayPlanResponse>(JsonSerializer.Serialize(response, Web), Web)!;
        var day = Assert.Single(restored.Days);
        var item = Assert.Single(day.Stops);
        Assert.Equal(stop, item.Id);
        Assert.Equal(recommendation, item.RecommendationId);
        Assert.Equal(new DateOnly(2026, 10, 20), day.Date);
        Assert.Equal("morning", item.PeriodKey);
        Assert.Equal("Gion, Kyoto", item.Place);
        Assert.True(item.Card.IsPeriodOnly);
        Assert.True(item.Card.IsDayPlan);
        Assert.Equal("Check opening hours", Assert.Single(item.Card.Warnings));
        Assert.Equal("evening", Assert.Single(day.MissingMoments));
    }

    [Fact]
    public void Legacy_proposal_without_a_card_revision_keeps_revision_zero()
    {
        var json = $$"""
            {"operationId":"{{Guid.NewGuid()}}","tripId":"{{Guid.NewGuid()}}","basedOnRevision":7,
             "days":[],"message":"Legacy proposal"}
            """;

        var restored = JsonSerializer.Deserialize<DayPlanResponse>(json, Web)!;

        Assert.Equal(0, restored.ProposalRevision);
        Assert.Equal(7, restored.BasedOnRevision);
    }

    [Fact]
    public void Replacement_contract_preserves_operation_mutation_and_both_revisions_without_client_place_data()
    {
        var generation = new DayPlanRequest(Guid.NewGuid(), 2, new(2026, 10, 20), 3, Guid.NewGuid(),
            new("efficient", "medium", ["culture"]), "en");
        var request = new DayPlanReplaceRequest(generation.OperationId, generation.TripId, 3,
            Guid.NewGuid(), Guid.NewGuid(), "en") { ExpectedProposalRevision = 4, OriginalRequest = generation };
        var restored = JsonSerializer.Deserialize<DayPlanReplaceRequest>(JsonSerializer.Serialize(request, Web), Web)!;

        Assert.Equal(request.OperationId, restored.OperationId);
        Assert.Equal(request.StopId, restored.StopId);
        Assert.Equal(request.MutationId, restored.MutationId);
        Assert.Equal(3, restored.ExpectedRevision);
        Assert.Equal(4, restored.ExpectedProposalRevision);
        Assert.Equal(JsonSerializer.Serialize(generation, Web), JsonSerializer.Serialize(restored.OriginalRequest, Web));
        Assert.DoesNotContain("recommendationId", JsonSerializer.Serialize(restored, Web));
    }

    [Fact]
    public void Replacement_response_preserves_unchanged_proposal_and_friendly_exhaustion_code()
    {
        var proposal = new DayPlanResponse(Guid.NewGuid(), Guid.NewGuid(), 5, [], "Original")
        { ProposalRevision = 3 };
        var response = new DayPlanReplaceResponse(false, "no_alternative", "Your idea is unchanged.", proposal);

        var restored = JsonSerializer.Deserialize<DayPlanReplaceResponse>(JsonSerializer.Serialize(response, Web), Web)!;

        Assert.False(restored.Replaced);
        Assert.Equal("no_alternative", restored.Code);
        Assert.Equal(proposal.OperationId, restored.Proposal.OperationId);
        Assert.Equal(proposal.TripId, restored.Proposal.TripId);
        Assert.Equal(proposal.BasedOnRevision, restored.Proposal.BasedOnRevision);
        Assert.Equal(proposal.ProposalRevision, restored.Proposal.ProposalRevision);
        Assert.Equal(proposal.Message, restored.Proposal.Message);
        Assert.Empty(restored.Proposal.Days);
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

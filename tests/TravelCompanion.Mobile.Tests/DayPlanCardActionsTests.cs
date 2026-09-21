using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class DayPlanCardActionsTests
{
    [Fact]
    public void Unsaved_suggestion_requests_an_alternative_with_the_latest_draft_context()
    {
        var old = Card(new(9, 0));
        var current = Card(new(9, 0));
        var target = Card(new(10, 30));
        var otherDay = Card(new(13, 0));
        otherDay.PlanningDate = target.PlanningDate!.Value.AddDays(1);

        var action = DayPlanCardActions.CreateAlternative(target, [old, current, target, otherDay], "closer", null);

        Assert.Equal(target.RecommendationId.ToString(), action.RecommendationId);
        Assert.Empty(action.ReplaceReservationIds);
        Assert.Equal(new[] { current.RecommendationId!.Value, target.RecommendationId!.Value },
            action.DraftDayStops.Select(stop => stop.RecommendationId));
        Assert.Equal("closer", action.DistanceAdjustment);
        Assert.All(action.DraftDayStops, stop => Assert.Null(stop.ReservationId));
        Assert.True(target.CanSave);
        Assert.True(target.CanFindAlternative);
    }

    [Fact]
    public void Saved_stop_uses_its_reservation_and_keeps_unsaved_neighbours_as_distance_context()
    {
        var saved = Card(new(10, 30), Guid.NewGuid(), existing: true);
        var neighbour = Card(new(9, 0));

        var action = DayPlanCardActions.CreateAlternative(saved, [saved, neighbour], "closer", "cheaper");

        Assert.True(saved.IsSaved);
        Assert.Null(action.RecommendationId);
        Assert.Equal(saved.ReservationId, Assert.Single(action.ReplaceReservationIds));
        Assert.Equal(neighbour.RecommendationId, Assert.Single(action.DraftDayStops).RecommendationId);
        Assert.Equal("cheaper", action.BudgetAdjustment);
    }

    [Fact]
    public void Pending_replacement_remains_a_draft_and_preserves_the_saved_target()
    {
        var pending = Card(new(10, 30), Guid.NewGuid());
        var action = DayPlanCardActions.CreateAlternative(pending, [pending], null, null);

        Assert.False(pending.IsSaved);
        Assert.Empty(action.ReplaceReservationIds);
        Assert.Equal(pending.ReservationId, Assert.Single(action.DraftDayStops).ReservationId);
        Assert.Equal(LocalizationResourceManager.Instance["AssistantSaveReplacement"], pending.SaveButtonText);
        Assert.Equal(LocalizationResourceManager.Instance["AssistantPendingChange"], pending.DayPlanStateText);
    }

    [Fact]
    public void Searching_disables_actions_and_a_preview_replaces_only_its_card()
    {
        var current = Card(new(10, 30));
        var neighbour = Card(new(9, 0));
        var message = new TravelChatMessageViewModel("", false, [neighbour, current]);
        var changed = new List<string?>();
        current.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        current.IsSearchingAlternative = true;
        Assert.False(current.CanSave);
        Assert.False(current.CanFindAlternative);
        Assert.Contains(nameof(current.CanSave), changed);
        Assert.Contains(nameof(current.CanFindAlternative), changed);
        current.IsSearchingAlternative = false;
        Assert.True(current.CanSave);
        Assert.True(current.CanFindAlternative);

        var alternative = Card(new(10, 30));
        Assert.True(message.ReplaceCard(current, alternative));
        Assert.Equal(2, message.Cards.Count);
        Assert.Same(neighbour, message.Cards[0]);
        Assert.Same(alternative, message.Cards[1]);
        Assert.False(alternative.IsSaved);
    }

    private static TravelChatCardViewModel Card(TimeOnly time, Guid? reservationId = null, bool existing = false) =>
        new(new TravelCardDto(existing ? "existing_day_stop" : "recommendation", "Place", null, null,
            time.ToString("HH:mm"), null, "medium", 3, null, [], [], Guid.NewGuid().ToString(), reservationId?.ToString())
        { IsDayPlan = true, IsPeriodOnly = true, HasLongTransfer = true })
        { PlanningDate = new(2026, 10, 6) };
}

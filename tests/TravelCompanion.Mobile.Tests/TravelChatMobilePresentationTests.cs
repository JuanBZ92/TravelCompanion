using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class TravelChatMobilePresentationTests
{
    [Fact]
    public void Card_icon_actions_follow_save_and_alternative_state()
    {
        var card = ProposalCard("Cafe", Guid.NewGuid(), "09:00", true);
        Assert.Equal("action_save.svg", card.SaveIcon);
        Assert.True(card.CanSave);
        card.IsSaving = true;
        Assert.False(card.CanSave);
        Assert.False(card.CanFindAlternative);
        card.IsSaving = false;
        card.IsSearchingAlternative = true;
        Assert.False(card.CanSave);
        Assert.False(card.CanFindAlternative);
        card.IsSearchingAlternative = false;
        card.IsSaved = true;
        Assert.Equal("action_saved.svg", card.SaveIcon);
        Assert.False(card.CanSave);
    }
    [Fact]
    public void Flexible_day_idea_allows_period_choice_but_existing_reservation_does_not()
    {
        var dto = new TravelCardDto("day_stop", "Cocktails", "Tokyo", null, "09:00", null,
            "medium", null, null, [], [], Guid.NewGuid().ToString(), null)
            { IsPeriodOnly = true, IsDayPlan = true, PeriodKey = "morning" };
        var card = new TravelChatCardViewModel(dto);
        Assert.False(new TravelChatCardViewModel(dto with { PeriodKey = null }).CanChoosePeriod);
        Assert.True(card.CanChoosePeriod);
        card.SelectedPeriodIndex = 2;
        Assert.Equal("afternoon", card.PeriodKey);
        Assert.Equal("morning", dto.PeriodKey);
        card.IsSaved = true;
        Assert.False(card.CanChoosePeriod);
        card.SelectedPeriodIndex = 3;
        Assert.Equal("afternoon", card.PeriodKey);
        var replacement = new TravelChatCardViewModel(dto) { ReservationId = Guid.NewGuid() };
        Assert.False(replacement.CanChoosePeriod);
        replacement.SelectedPeriodIndex = 3;
        Assert.Equal("morning", replacement.PeriodKey);
    }
    [Fact]
    public void Traveler_selected_assistant_day_survives_return_from_the_editor()
    {
        var first = new DateOnly(2026, 9, 27);
        var selected = first.AddDays(2);

        Assert.True(AssistantPlanningDatePolicy.ShouldPreserveSelection(selected, first,
            first.AddDays(4), selectedByTraveler: true, isFullDayFlow: false));
        Assert.False(AssistantPlanningDatePolicy.ShouldPreserveSelection(selected, first,
            first.AddDays(1), selectedByTraveler: true, isFullDayFlow: false));
        Assert.False(AssistantPlanningDatePolicy.ShouldPreserveSelection(selected, first,
            first.AddDays(4), selectedByTraveler: false, isFullDayFlow: false));
    }

    [Fact]
    public void Search_without_selected_interests_can_request_another_option()
    {
        var broadSearch = new GuidedPlanCriteriaDto(Category: null)
        {
            Categories = [GuidedTravelCategories.Food, GuidedTravelCategories.Relax,
                GuidedTravelCategories.Culture, GuidedTravelCategories.Walk,
                GuidedTravelCategories.Dance, GuidedTravelCategories.Nature,
                GuidedTravelCategories.Shopping, GuidedTravelCategories.Viewpoint,
                GuidedTravelCategories.Nightlife]
        };

        Assert.True(AssistantGuidedCriteriaPolicy.HasValidCategory(broadSearch));
        Assert.True(AssistantGuidedCriteriaPolicy.HasValidCategory(
            new GuidedPlanCriteriaDto(GuidedTravelCategories.Food)));
        Assert.False(AssistantGuidedCriteriaPolicy.HasValidCategory(new GuidedPlanCriteriaDto()));
        Assert.False(AssistantGuidedCriteriaPolicy.HasValidCategory(
            new GuidedPlanCriteriaDto { Categories = null! }));
    }

    [Fact]
    public void Day_proposal_keeps_bookings_and_avoids_duplicate_saved_recommendations()
    {
        var day = new DateOnly(2026, 9, 27);
        var savedRecommendation = Guid.NewGuid();
        var booking = ProposalItem(day, "Hotel check-in", new TimeOnly(15, 0),
            ReservationType.Lodging);
        var saved = ProposalItem(day, "Museum", new TimeOnly(11, 0),
            recommendationId: savedRecommendation);
        var duplicate = ProposalCard("Museum", savedRecommendation, "11:00", false);
        var idea = ProposalCard("Garden", Guid.NewGuid(), "09:00", true);

        var rows = AssistantDayProposalBuilder.Build([booking, saved], [duplicate, idea], day);

        Assert.Equal(3, rows.Count);
        Assert.Equal("Garden", rows[0].Title);
        Assert.Equal("Museum", rows[1].Title);
        Assert.Equal("Hotel check-in", rows[2].Title);
        Assert.Single(rows, row => row.IsSuggestion);
        Assert.DoesNotContain("09:00", rows[0].When);
    }

    [Fact]
    public void Day_proposal_shows_ongoing_hotel_without_inventing_a_new_check_in()
    {
        var hotel = ProposalItem(new DateOnly(2026, 9, 27), "Hotel",
            new TimeOnly(15, 0), ReservationType.Lodging) with
        { EndsOn = new DateOnly(2026, 9, 30) };

        var rows = AssistantDayProposalBuilder.Build([hotel], [], new DateOnly(2026, 9, 28));

        var row = Assert.Single(rows);
        Assert.True(row.IsOngoingStay);
        Assert.DoesNotContain("15:00", row.When);
    }

    [Fact]
    public void Day_proposal_does_not_label_an_unscheduled_idea_as_evening()
    {
        var day = new DateOnly(2026, 9, 27);
        var idea = ProposalCard("Open plan", Guid.NewGuid(), null, true);

        var row = Assert.Single(AssistantDayProposalBuilder.Build([], [idea], day));

        Assert.Equal(LocalizationResourceManager.Instance["AssistantProposalFlexible"], row.When);
    }

    [Fact]
    public void Search_proposal_does_not_present_a_window_boundary_as_a_fixed_activity_time()
    {
        var day = new DateOnly(2026, 9, 28);
        var saved = ProposalItem(day, "Museum", new TimeOnly(10, 0));
        var idea = ProposalCard("Cafe", Guid.NewGuid(), "00:00", periodOnly: false);

        var row = Assert.Single(AssistantDayProposalBuilder.BuildQuickSearch([saved], [idea], day));

        Assert.Equal("Cafe", row.Title);
        Assert.Equal(LocalizationResourceManager.Instance["AssistantProposalFlexible"], row.When);
    }

    [Fact]
    public void Quick_search_without_timing_shows_only_new_ideas_in_their_original_order()
    {
        var day = new DateOnly(2026, 9, 28);
        var saved = ProposalItem(day, "Museum", new TimeOnly(11, 0));
        var first = ProposalCard("Cafe", Guid.NewGuid(), null, false);
        var second = ProposalCard("Garden", Guid.NewGuid(), "00:00", false);

        var rows = AssistantDayProposalBuilder.BuildQuickSearch([saved], [first, second], day);

        Assert.Equal(["Cafe", "Garden"], rows.Select(row => row.Title));
        Assert.All(rows, row => Assert.True(row.IsSuggestion));
    }

    [Fact]
    public void Quick_search_does_not_add_context_with_only_one_timed_idea()
    {
        var day = new DateOnly(2026, 9, 28);
        var saved = ProposalItem(day, "Museum", new TimeOnly(12, 0));
        var idea = ProposalCard("Cafe", Guid.NewGuid(), "09:00", false);

        var row = Assert.Single(AssistantDayProposalBuilder.BuildQuickSearch([saved], [idea], day));

        Assert.Equal("Cafe", row.Title);
    }

    [Fact]
    public void Quick_search_keeps_only_saved_plans_between_timed_ideas()
    {
        var day = new DateOnly(2026, 9, 28);
        var before = ProposalItem(day, "Breakfast", new TimeOnly(8, 0));
        var between = ProposalItem(day, "Museum", new TimeOnly(12, 0)) with
        { EndsAt = new TimeOnly(13, 0) };
        var overlapsLastIdea = ProposalItem(day, "Long visit", new TimeOnly(15, 0)) with
        { EndsAt = new TimeOnly(18, 0) };
        var after = ProposalItem(day, "Dinner", new TimeOnly(20, 0));
        var first = ProposalCard("Cafe", Guid.NewGuid(), "09:00", false);
        var second = ProposalCard("Garden", Guid.NewGuid(), "17:00", false);

        var rows = AssistantDayProposalBuilder.BuildQuickSearch(
            [before, between, overlapsLastIdea, after], [first, second], day);

        Assert.Equal(["Cafe", "Museum", "Garden"], rows.Select(row => row.Title));
        Assert.Single(rows, row => row.IsSaved);
    }

    [Fact]
    public void Quick_search_does_not_use_flexible_periods_as_chronological_context()
    {
        var day = new DateOnly(2026, 9, 28);
        var morning = ProposalItem(day, "Morning booking", new TimeOnly(10, 0));
        var midday = ProposalItem(day, "Lunch", new TimeOnly(13, 0));
        var night = ProposalItem(day, "Night booking", new TimeOnly(21, 0));
        var first = ProposalCard("Morning idea", Guid.NewGuid(), "09:00", true);
        var second = ProposalCard("Night idea", Guid.NewGuid(), "21:00", true);

        var rows = AssistantDayProposalBuilder.BuildQuickSearch(
            [morning, midday, night], [first, second], day);

        Assert.Equal(["Morning idea", "Night idea"], rows.Select(row => row.Title));
        Assert.All(rows, row => Assert.Equal(
            LocalizationResourceManager.Instance["AssistantProposalFlexible"], row.When));
    }

    [Fact]
    public void Quick_search_keeps_a_saved_result_without_listing_other_saved_plans()
    {
        var day = new DateOnly(2026, 9, 28);
        var recommendationId = Guid.NewGuid();
        var savedIdea = ProposalItem(day, "Cafe", new TimeOnly(10, 0),
            recommendationId: recommendationId);
        var otherSaved = ProposalItem(day, "Museum", new TimeOnly(12, 0));
        var card = ProposalCard("Cafe", recommendationId, "10:00", false);
        var newIdea = ProposalCard("Garden", Guid.NewGuid(), null, false);

        var rows = AssistantDayProposalBuilder.BuildQuickSearch(
            [savedIdea, otherSaved], [card, newIdea], day);

        Assert.Equal(["Cafe", "Garden"], rows.Select(row => row.Title));
        Assert.True(rows[0].IsSaved);
        Assert.False(rows[1].IsSaved);
    }

    private static ScheduleItemDto ProposalItem(DateOnly date, string title, TimeOnly time,
        ReservationType type = ReservationType.Event, Guid? recommendationId = null) =>
        new(Guid.NewGuid(), recommendationId, type, date, time, null, null, title,
            "Tokyo", title, string.Empty, string.Empty, string.Empty,
            null, null, null, null, null, null);

    private static TravelChatCardViewModel ProposalCard(string title, Guid recommendationId,
        string? start, bool periodOnly) => new(new TravelCardDto(
            "recommendation", title, "Tokyo", null, start, null, "medium", null, null,
            [], [], recommendationId.ToString(), null) { IsPeriodOnly = periodOnly });

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
    public void Normalize_schedule_replaces_a_stale_conflict_for_flexible_recommendations()
    {
        var date = new DateOnly(2026, 10, 1);
        var item = new ScheduleItemDto(
            Guid.NewGuid(), Guid.NewGuid(), ReservationType.Event, date,
            new TimeOnly(9, 0), null, new TimeOnly(10, 0), "Flexible cafe",
            "Tokyo", "Flexible cafe", string.Empty, "AI-PLAN", string.Empty,
            null, null, null, null, null, null,
            ScheduleItemKind.Recommendation, ItineraryItemOwner.Traveler,
            ItineraryItemSource.YukuRecommendation, ItineraryTimePrecision.Exact,
            Flexibility: ItineraryFlexibility.Flexible);
        var staleReview = new DayReviewDto(
            date, DayReviewStatuses.Tight, "Tu día tiene poco margen", "Stale",
            [new DayReviewIssueDto(
                DayReviewIssueKinds.TightTransfer, DayReviewSeverities.Warning,
                "Traslado estimado con poco margen", "Stale", [item.Id])]);
        var schedule = new TripScheduleDto(
            Guid.NewGuid(), "Traveler", "Japan", date, date, [item],
            DayReviews: [staleReview]);

        var normalized = MobilePayloadNormalizer.Normalize(schedule);

        var review = Assert.Single(normalized!.DayReviews!);
        Assert.Equal(DayReviewStatuses.Balanced, review.Status);
        Assert.Empty(review.Issues);
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

    [Theory]
    [InlineData("Su valoracion es mas baja que otras opciones.")]
    [InlineData("Su valoración es más baja que otras opciones.")]
    public void Existing_low_rating_warning_is_hidden_but_other_warnings_remain(string legacyWarning)
    {
        var card = new TravelCardDto("recommendation", "Café", null, null,
            null, null, null, null, null, [], [legacyWarning, "Puede haber fila."], null, null);

        var viewModel = new TravelChatCardViewModel(card);

        Assert.Equal(["Puede haber fila."], viewModel.Warnings);
        Assert.True(viewModel.HasWarnings);
    }

    [Fact]
    public void Period_only_day_plan_does_not_show_a_fixed_time()
    {
        var card = new TravelCardDto(
            "recommendation", "Morning cafe", "Café de mañana", null,
            "09:00", "10:00", "low", null, null, [], [], Guid.NewGuid().ToString(), null)
        {
            IsPeriodOnly = true
        };

        var viewModel = new TravelChatCardViewModel(card);

        Assert.False(viewModel.HasTimeLabel);
        Assert.Equal(string.Empty, viewModel.TimeLabel);
        Assert.Equal(new TimeOnly(9, 0), viewModel.StartsAt);
    }

    [Fact]
    public void Day_plan_shows_its_reason_without_expanding_details()
    {
        var dayPlan = new TravelChatCardViewModel(new TravelCardDto(
            "recommendation", "Museum", "Morning", null, "09:00", "10:00",
            "medium", null, null, ["Coincide con tu interés por arte."], [],
            Guid.NewGuid().ToString(), null) { IsDayPlan = true });
        var ordinary = new TravelChatCardViewModel(new TravelCardDto(
            "recommendation", "Museum", "Morning", null, "09:00", "10:00",
            "medium", null, null, ["Coincide con tu interés por arte."], [],
            Guid.NewGuid().ToString(), null));

        Assert.True(dayPlan.ShowReasons);
        Assert.False(ordinary.ShowReasons);
        ordinary.ToggleDetailsCommand.Execute(null);
        Assert.True(ordinary.ShowReasons);
    }

    [Fact]
    public void Day_plan_card_keeps_replacement_identity_and_exposes_distance_action()
    {
        var reservationId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var card = new TravelCardDto("recommendation", "Museum", "Morning", null, "10:30", "11:30",
            "low", 4.2, null, [], ["4.2 km in a straight line"], Guid.NewGuid().ToString(), reservationId.ToString())
        { IsDayPlan = true, HasLongTransfer = true, ReplacesRecommendationId = originalId, IsPeriodOnly = true };
        var normalized = MobilePayloadNormalizer.Normalize(new TravelChatResponse("day", "", "day_plan", [card], [], null));
        var viewModel = new TravelChatCardViewModel(Assert.Single(normalized!.Cards));
        Assert.Equal(reservationId, viewModel.ReservationId);
        Assert.Equal(originalId, viewModel.ReplacesRecommendationId);
        Assert.True(viewModel.HasLongTransfer);
        Assert.True(viewModel.HasWarnings);
        Assert.False(viewModel.ShowPreferenceAdjustment);
        Assert.False(viewModel.HasTimeLabel);
        Assert.NotEqual(Guid.Empty, viewModel.SaveMutationId);
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
    public void Full_day_progress_message_updates_without_becoming_a_duplicate_chat_message()
    {
        var message = new TravelChatMessageViewModel(
            "Armando tu día...",
            isFromUser: false,
            isProgressMessage: true,
            isLoading: true);

        Assert.True(message.IsProgressMessage);
        Assert.True(message.IsLoading);
        Assert.False(message.ShouldShowText);

        message.UpdateProgress("2 de 5 planes listos.", isLoading: true);

        Assert.Equal("2 de 5 planes listos.", message.Text);
        Assert.True(message.IsLoading);

        message.UpdateProgress("Se agregaron 5 planes directamente a este día.", isLoading: false);

        Assert.False(message.IsLoading);
        Assert.Equal("Se agregaron 5 planes directamente a este día.", message.Text);
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

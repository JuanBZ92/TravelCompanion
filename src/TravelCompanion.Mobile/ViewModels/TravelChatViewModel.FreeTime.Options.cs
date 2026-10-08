using System.Globalization;
using System.Text;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class TravelChatViewModel
{
    private Task ReplaceFreeTimeOptionAsync(TravelChatCardViewModel card) => RequestFreeTimeOptionsAsync(card);

    private async Task RequestFreeTimeOptionsAsync(TravelChatCardViewModel? replaced)
    {
        if (IsBusy || !IsFreeTimeSearch || _freeTimeArea is null) return;
        if (!sessionService.CanUseAssistant) { ErrorMessage = Resource("ExpressAccessUnavailable"); return; }
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        { ErrorMessage = Resource("ExpressNeedsConnection"); return; }
        var scope = BeginAssistantOperation();
        if (replaced is not null) replaced.IsSearchingAlternative = true;
        try
        {
            ErrorMessage = null;
            _freeTimeNoOptions = false;
            StatusMessage = Resource("ExpressSearching");
            var token = await scope.AwaitAsync(_ => sessionService.GetTokenAsync());
            VerifyFreeTimeAccess(scope);
            if (string.IsNullOrWhiteSpace(token)) { ErrorMessage = Resource("ExpressAccessUnavailable"); return; }
            var cached = await scope.AwaitAsync(_ => bootstrapStore.GetCachedAsync());
            VerifyFreeTimeAccess(scope);
            if (cached?.Value.Schedule is not { } schedule || schedule.TripId != scope.TripId)
            { ErrorMessage = Resource("AssistantProposalNeedsRefresh"); return; }
            _assistantSchedule = schedule;
            RefreshFreeTimeWindow();
            if (_freeTimeWindow is not { } window || DateOnly.FromDateTime(window.StartsAtLocal) != scope.Date)
            { ErrorMessage = Resource("AssistantFreeTimeNoWindow"); return; }
            if (_freeTimeNearPlanId.HasValue && _freeTimeNextPlan?.Id != _freeTimeNearPlanId)
            { _freeTimeArea = null; ErrorMessage = Resource("ExpressAreaChanged"); return; }
            var retry = _pendingRetryRequest is not null && _freeTimeRetryReplacementId == replaced?.RecommendationId
                && _pendingRetryRequest.Date == scope.Date && _pendingRetryRequest.Criteria?.WindowEndsAtLocal >
                    UpcomingActivitySelector.GetTripNow(schedule.TimeZoneId, FreeTimeClock());
            // Keep the draw made by Surprise stable for retries and individual replacements.
            // With saved interests, leave the choice open for the existing personalized ranking.
            var categories = _freeTimeSurpriseSelected
                ? _freeTimeSurpriseCategory is { } randomCategory ? new[] { randomCategory } : AvailableFreeTimeCategories()
                : _quickCategories.ToArray();
            var criteria = new GuidedPlanCriteriaDto(
                _freeTimeSurpriseSelected ? _freeTimeSurpriseCategory : _quickCategories.FirstOrDefault(), GuidedTravelPriorities.Direct,
                MaxWalkingMinutes: _freeTimeArea == "city" ? null : 30, MaxDurationMinutes: window.AvailableMinutes)
            {
                Categories = categories, WindowStartsAtLocal = window.StartsAtLocal,
                WindowEndsAtLocal = window.EndsAtLocal, WindowTimeZoneId = schedule.TimeZoneId,
                NearReservationId = _freeTimeNearPlanId, ExcludedRecommendationIds = _freeTimeProposedIds.ToArray()
            };
            var request = retry ? _pendingRetryRequest! : new TravelChatRequest(Resource("AssistantFreeTimeRequest"),
                _conversationId, _freeTimeArea == "next" ? _freeTimeNextPlan?.City ?? City : City, scope.Date,
                _freeTimeLocation, CultureInfo.CurrentUICulture.Name,
                new GuidedTravelActionDto(replaced is null ? GuidedTravelActions.Recommend : GuidedTravelActions.Alternative,
                    RecommendationId: replaced?.RecommendationId?.ToString()), criteria, Guid.NewGuid());
            _pendingRetryRequest = request;
            _freeTimeRetryReplacementId = replaced?.RecommendationId;
            scope.SetNetworkTimeout(TravelChatNetworkTimeout);
            var response = await scope.AwaitAsync(ct => apiClient.SendTravelChatAsync(token, request, ct));
            VerifyFreeTimeAccess(scope);
            if (response is null) { ErrorMessage = Resource("ExpressTryAgain"); return; }
            _pendingRetryRequest = null;
            if (response.TrialAccess?.State is TrialAccessState.Revoked or TrialAccessState.Expired or TrialAccessState.NoAccess)
            {
                sessionService.ApplyTrialAccess(response.TrialAccess);
                ErrorMessage = Resource("ExpressAccessUnavailable");
                return;
            }
            _conversationId = response.ConversationId;
            _guidedCriteria = response.Criteria ?? request.Criteria;
            if (response.MissingContext?.Field is "upgrade" or "daily_limit" or "upgrade_required")
            {
                ErrorMessage = response.Message;
                await PaywallNavigation.OpenAsync(PaywallEntryPoint.Assistant, limitReached: true);
                scope.Verify();
                if (response.TrialAccess is not null) sessionService.ApplyTrialAccess(response.TrialAccess);
                return;
            }
            var seen = new HashSet<Guid>(_freeTimeProposedIds);
            var places = new HashSet<string>(_freeTimeProposedPlaces, StringComparer.Ordinal);
            var accepted = response.Cards.Where(card => Guid.TryParse(card.RecommendationId, out var id)
                    && seen.Add(id) && RememberFreeTimePlace(card, places))
                .Take(replaced is null ? 3 : 1).ToList();
            var cards = accepted.Select(card => new TravelChatCardViewModel(card) { PlanningDate = scope.Date }).ToList();
            if (cards.Count == 0)
            {
                _freeTimeNoOptions = response.MissingContext?.Field is null or "city";
                ErrorMessage = response.MissingContext?.Field == "time_window" ? Resource("AssistantFreeTimeNoWindow")
                    : response.MissingContext?.Field == "area" ? Resource("ExpressAreaChanged")
                    : response.Cards.Count == 0 && !string.IsNullOrWhiteSpace(response.Message) ? response.Message
                    : replaced is null ? Resource("ExpressNoOptions") : Resource("ExpressNoReplacement");
                if (response.MissingContext?.Field == "area") _freeTimeArea = null;
                if (response.TrialAccess is not null) sessionService.ApplyTrialAccess(response.TrialAccess);
                return;
            }
            // Record accepted results before publishing or awaiting analytics. A lost UI continuation
            // must not make a later request recycle options already shown to the traveler.
            foreach (var card in cards) if (card.RecommendationId is { } id) _freeTimeProposedIds.Add(id);
            foreach (var card in accepted) RememberFreeTimePlace(card, _freeTimeProposedPlaces);
            if (replaced is null)
            {
                Messages.Clear();
                Messages.Add(new TravelChatMessageViewModel(response.Message, false, cards));
                await ShowAssistantProposalAsync(cards, Resource("ExpressResultsHelp"), isQuickSearch: true, operation: scope);
                VerifyFreeTimeAccess(scope);
            }
            else
            {
                var index = _proposalCards.IndexOf(replaced);
                if (index < 0) return;
                _proposalCards[index] = cards[0];
                Messages.FirstOrDefault(message => message.Cards.Contains(replaced))?.ReplaceCard(replaced, cards[0]);
                if (SelectedDetailCard == replaced) SelectedDetailCard = null;
                await RefreshAssistantProposalAsync(scope);
                VerifyFreeTimeAccess(scope);
            }
            ErrorMessage = null;
            if (!scope.ExplicitlyCancelled) StatusMessage = null;
            // Applying this response's quota changes the session version. Do it only after the
            // guarded publication, with no asynchronous continuation that can reuse the old scope.
            if (response.TrialAccess is not null) sessionService.ApplyTrialAccess(response.TrialAccess);
        }
        catch (OperationCanceledException) when (!IsCurrentFreeTimeScope(scope)) { }
        catch (UnauthorizedAccessException) { if (scope.CanPublish) ErrorMessage = Resource("ExpressAccessUnavailable"); }
        catch (Exception) { if (scope.CanPublish) ErrorMessage = Resource("ExpressTryAgain"); }
        finally { if (replaced is not null) replaced.IsSearchingAlternative = false; FinishFreeTimeOperation(scope); }
    }

    private void VerifyFreeTimeAccess(AssistantRequestScope scope)
    {
        scope.Verify();
        if (_assistantSchedule is { } schedule && scope.Date != DateOnly.FromDateTime(
                UpcomingActivitySelector.GetTripNow(schedule.TimeZoneId, FreeTimeClock())))
        {
            _freeTimeNeedsRestart = true;
            ErrorMessage = Resource("ExpressDayChanged");
            OnPropertyChanged(nameof(FreeTimeNeedsRestart));
            scope.Cancel();
            throw new OperationCanceledException(scope.Token);
        }
        if (!IsCurrentFreeTimeScope(scope)) throw new OperationCanceledException(scope.Token);
        if (!sessionService.CanUseAssistant) throw new UnauthorizedAccessException();
    }

    private bool IsCurrentFreeTimeScope(AssistantRequestScope scope) => scope.CanPublish && IsFreeTimeSearch
        && (ShowAssistantSearch || ShowAssistantProposal);

    private void FinishFreeTimeOperation(AssistantRequestScope scope)
    {
        if (ReferenceEquals(_assistantOperation, scope))
        {
            _assistantOperation = null;
            IsBusy = false;
            if (!scope.ExplicitlyCancelled) StatusMessage = null;
            NotifyFreeTimeLabels();
        }
        scope.Dispose();
    }

    private async Task<bool> ValidateFreeTimeSaveAsync(TravelChatCardViewModel card)
    {
        if (!IsFreeTimeSearch) return true;
        using var scope = new AssistantRequestScope(sessionService, () => DateOnly.FromDateTime(PlanningDate),
            () => _assistantPageOperationVersion);
        var cached = await scope.AwaitAsync(_ => bootstrapStore.GetCachedAsync());
        VerifyFreeTimeAccess(scope);
        if (cached?.Value.Schedule is not { } schedule || schedule.TripId != scope.TripId
            || card.StartsAt is not { } start || card.EndsAt is not { } end
            || _guidedCriteria?.WindowStartsAtLocal is not { } proposedStart
            || _guidedCriteria.WindowEndsAtLocal is not { } proposedEnd)
        { ErrorMessage = Resource("AssistantProposalNeedsRefresh"); return false; }
        var startLocal = scope.Date.ToDateTime(start);
        var endLocal = scope.Date.ToDateTime(end);
        var now = UpcomingActivitySelector.GetTripNow(schedule.TimeZoneId, FreeTimeClock());
        var valid = startLocal >= proposedStart && endLocal <= proposedEnd && endLocal > startLocal
            && startLocal >= now && AssistantFreeTimeWindow.Resolve(schedule, scope.Date,
                start.ToTimeSpan(), (int)(endLocal - startLocal).TotalMinutes, FreeTimeClock()) is { } current
            && current.EndsAtLocal >= endLocal;
        if (!valid) ErrorMessage = Resource("ExpressOptionExpired");
        return valid;
    }

    private static bool RememberFreeTimePlace(TravelCardDto card, HashSet<string> places)
    {
        var title = string.Concat(card.Title.Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark
                && char.IsLetterOrDigit(character))).ToUpperInvariant();
        var titleKey = "title:" + title;
        var providerKey = string.IsNullOrWhiteSpace(card.ProviderPlaceId) ? null : "provider:" + card.ProviderPlaceId.Trim();
        if (places.Contains(titleKey) || providerKey is not null && places.Contains(providerKey)) return false;
        places.Add(titleKey);
        if (providerKey is not null) places.Add(providerKey);
        return true;
    }
}

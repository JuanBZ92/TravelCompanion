using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class TravelChatViewModel
{
    private string _assistantSurface = "home";
    private readonly Stack<string> _assistantHistory = new();
    private bool _quickSearchSubmission;
    private bool _proposalIsQuickSearch;
    private string _proposalMessage = string.Empty;
    private TravelChatCardViewModel? _selectedDetailCard;
    private readonly List<TravelChatCardViewModel> _proposalCards = [];
    private Guid? _proposalTripId;
    private int? _proposalRevision;
    private Guid? _pendingProposalSaveRecommendationId;
    private bool _assistantDateSelectedByTraveler;
    private DateTime _assistantMinimumDate = new(2020, 1, 1);
    private DateTime _assistantMaximumDate = new(2100, 12, 31);
    private readonly HashSet<string> _quickCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _quickBudgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _quickWalking = [];

    public ObservableCollection<AssistantProposalRow> ProposalRows { get; } = [];
    public ObservableCollection<TravelChatGuidedOptionViewModel> QuickCategories { get; } =
        new(CreateCategoryOptions(includeContinue: false));
    public ObservableCollection<TravelChatGuidedOptionViewModel> QuickBudgets { get; } =
        new(CreateBudgetOptions().Where(option => option.Id != "budget.continue"));
    public ObservableCollection<TravelChatGuidedOptionViewModel> QuickWalking { get; } =
        new(CreateDistanceOptions().Where(option => option.Id != "distance.continue"));
    public bool ShowAssistantHome => _assistantSurface == "home";
    public bool ShowAssistantSearch => _assistantSurface == "search";
    public bool ShowAssistantProposal => _assistantSurface == "proposal";
    public bool ShowAssistantConversation => _assistantSurface == "conversation";
    public bool HasProposalRows => ProposalRows.Count > 0;
    public DateTime AssistantMinimumDate
    {
        get => _assistantMinimumDate;
        private set => SetProperty(ref _assistantMinimumDate, value);
    }
    public DateTime AssistantMaximumDate
    {
        get => _assistantMaximumDate;
        private set => SetProperty(ref _assistantMaximumDate, value);
    }
    public string ProposalMessage => _proposalMessage;
    public string AssistantContext => $"{DateOnly.FromDateTime(PlanningDate).ToString("d MMMM", CultureInfo.CurrentCulture)} · {City ?? Resource("AssistantDestinationFallback")}";
    public string AssistantQuotaNote => sessionService.IsTrial
        ? string.Format(CultureInfo.CurrentCulture, Resource("AssistantHomeQuota"),
            sessionService.DayImprovementsRemaining, sessionService.TrialAssistantRequestsRemaining)
        : string.Empty;
    public string AssistantSearchQuotaNote => sessionService.IsTrial
        ? string.Format(CultureInfo.CurrentCulture, Resource("AssistantSearchQuota"),
            sessionService.TrialAssistantRequestsRemaining)
        : string.Empty;
    public string AssistantHomeIntro => Resource("AssistantHomeIntro");
    public string AssistantForDay => Resource("AssistantForDay");
    public string AssistantChooseDate => Resource("AssistantChooseDate");
    public string AssistantCompleteDay => Resource("AssistantCompleteDay");
    public string AssistantCompleteDayHelp => Resource("AssistantCompleteDayHelp");
    public string AssistantFindPlace => Resource("AssistantFindPlace");
    public string AssistantAsk => Resource("AssistantAsk");
    public string AssistantBackHome => Resource("AssistantBackHome");
    public string AssistantSearchIntro => Resource("AssistantSearchIntro");
    public string AssistantInterests => Resource("AssistantInterests");
    public string AssistantOptionalBudget => Resource("AssistantOptionalBudget");
    public string AssistantOptionalWalk => Resource("AssistantOptionalWalk");
    public string AssistantShowIdeas => Resource("AssistantShowIdeas");
    public string AssistantYourDay => Resource("AssistantYourDay");
    public string AssistantNoIdeas => Resource("AssistantNoIdeas");
    public string AssistantOpenActivity => Resource("AssistantOpenActivity");
    public string AssistantAskAboutDay => Resource("AssistantAskAboutDay");
    public string AssistantConversationPrompt => Resource("AssistantConversationPrompt");
    public string AssistantConversationHelp => Resource("AssistantConversationHelp");
    public string AssistantAnotherOption => Resource("AssistantAnotherOption");
    public string AssistantAdjust => Resource("AssistantAdjust");
    public string AssistantClose => Resource("AssistantClose");
    public bool ShowAssistantQuotaNote => sessionService.IsTrial;
    public TravelChatCardViewModel? SelectedDetailCard
    {
        get => _selectedDetailCard;
        private set
        {
            if (SetProperty(ref _selectedDetailCard, value)) OnPropertyChanged(nameof(HasSelectedDetailCard));
        }
    }
    public bool HasSelectedDetailCard => SelectedDetailCard is not null;

    private void SetAssistantSurface(string surface, bool remember = true)
    {
        if (_assistantSurface == surface) return;
        if (remember) _assistantHistory.Push(_assistantSurface);
        _assistantSurface = surface;
        OnPropertyChanged(nameof(ShowAssistantHome));
        OnPropertyChanged(nameof(ShowAssistantSearch));
        OnPropertyChanged(nameof(ShowAssistantProposal));
        OnPropertyChanged(nameof(ShowAssistantConversation));
    }

    private void RefreshAssistantContext()
    {
        OnPropertyChanged(nameof(AssistantContext));
        OnPropertyChanged(nameof(AssistantQuotaNote));
        OnPropertyChanged(nameof(AssistantSearchQuotaNote));
        OnPropertyChanged(nameof(ShowAssistantQuotaNote));
    }

    private void RestoreAssistantSearch()
    {
        if (_assistantHistory.TryPeek(out var previous) && previous == "search") _assistantHistory.Pop();
        SetAssistantSurface("search", remember: false);
    }

    private void SetAssistantDateRange(TripScheduleDto schedule)
    {
        AssistantMinimumDate = schedule.StartsOn.ToDateTime(TimeOnly.MinValue);
        var max = sessionService.IsTrial
            ? schedule.StartsOn.AddDays(FreePlanningPolicy.MaximumDays - 1)
            : schedule.EndsOn;
        AssistantMaximumDate = (max < schedule.EndsOn ? max : schedule.EndsOn)
            .ToDateTime(TimeOnly.MinValue);
    }

    public async Task UpdateCityForDateAsync(DateTime selectedDate)
    {
        PlanningDate = selectedDate;
        _assistantDateSelectedByTraveler = true;
        var schedule = (await bootstrapStore.GetCachedAsync())?.Value.Schedule;
        if (schedule is null || schedule.TripId != sessionService.CurrentTripId) return;
        var date = DateOnly.FromDateTime(PlanningDate);
        City = schedule.Items.Where(item => item.Date <= date && !string.IsNullOrWhiteSpace(item.City))
            .OrderByDescending(item => item.Date).Select(item => item.City).FirstOrDefault()
            ?? schedule.Items.Where(item => item.Date > date && !string.IsNullOrWhiteSpace(item.City))
                .OrderBy(item => item.Date).Select(item => item.City).FirstOrDefault()
            ?? City;
    }

    private void RefreshAssistantLabels()
    {
        RefreshAssistantContext();
        OnPropertyChanged(nameof(AssistantHomeIntro));
        OnPropertyChanged(nameof(AssistantForDay));
        OnPropertyChanged(nameof(AssistantChooseDate));
        OnPropertyChanged(nameof(AssistantCompleteDay));
        OnPropertyChanged(nameof(AssistantCompleteDayHelp));
        OnPropertyChanged(nameof(AssistantFindPlace));
        OnPropertyChanged(nameof(AssistantAsk));
        OnPropertyChanged(nameof(AssistantBackHome));
        OnPropertyChanged(nameof(AssistantSearchIntro));
        OnPropertyChanged(nameof(AssistantInterests));
        OnPropertyChanged(nameof(AssistantOptionalBudget));
        OnPropertyChanged(nameof(AssistantOptionalWalk));
        OnPropertyChanged(nameof(AssistantShowIdeas));
        OnPropertyChanged(nameof(AssistantYourDay));
        OnPropertyChanged(nameof(AssistantNoIdeas));
        OnPropertyChanged(nameof(AssistantOpenActivity));
        OnPropertyChanged(nameof(AssistantAskAboutDay));
        OnPropertyChanged(nameof(AssistantConversationPrompt));
        OnPropertyChanged(nameof(AssistantConversationHelp));
        OnPropertyChanged(nameof(AssistantAnotherOption));
        OnPropertyChanged(nameof(AssistantAdjust));
        OnPropertyChanged(nameof(AssistantClose));
        QuickCategories.Clear();
        foreach (var option in CreateCategoryOptions(includeContinue: false)) QuickCategories.Add(option);
        QuickBudgets.Clear();
        foreach (var option in CreateBudgetOptions().Where(option => option.Id != "budget.continue"))
            QuickBudgets.Add(option);
        QuickWalking.Clear();
        foreach (var option in CreateDistanceOptions().Where(option => option.Id != "distance.continue"))
            QuickWalking.Add(option);
        RefreshQuickSelections();
        if (ProposalRows.Count > 0)
        {
            var rows = ProposalRows.ToList();
            ProposalRows.Clear();
            foreach (var row in rows) ProposalRows.Add(row);
        }
    }

    [RelayCommand]
    private async Task OpenCompleteDayAsync()
    {
        SetAssistantSurface("conversation");
        await RequestDayAlternativeAsync(DateOnly.FromDateTime(PlanningDate), City, null);
    }

    [RelayCommand]
    private void OpenQuickSearch()
    {
        ErrorMessage = null;
        StatusMessage = null;
        _quickCategories.Clear();
        _quickBudgets.Clear();
        _quickWalking.Clear();
        if (_cachedPreferenceProfile is { } profile)
        {
            foreach (var interest in profile.Interests.Where(GuidedTravelCategories.IsValid).Take(3))
                _quickCategories.Add(interest);
            if (profile.BudgetLevel is "low" or "medium" or "high") _quickBudgets.Add(profile.BudgetLevel);
            if (profile.MaxWalkingMinutes is 15 or 30) _quickWalking.Add(profile.MaxWalkingMinutes);
        }
        RefreshQuickSelections();
        SetAssistantSurface("search");
    }

    [RelayCommand]
    private void OpenAssistantConversation()
    {
        SelectedDetailCard = null;
        ErrorMessage = null;
        StatusMessage = null;
        Messages.Clear();
        OnMessagesChanged();
        HasGuidedQuestion = false;
        IsFreeTextVisible = true;
        SetAssistantSurface("conversation");
    }

    [RelayCommand]
    private void ReturnAssistantHome()
    {
        TryAssistantBack();
    }

    public bool TryAssistantBack()
    {
        if (HasSelectedDetailCard) { CloseAssistantCard(); return true; }
        if (IsSecondaryMenuVisible) { IsSecondaryMenuVisible = false; return true; }
        if (ShowAssistantConversation && HasGuidedQuestion && CanGoBack)
        {
            GoBackGuided();
            return true;
        }
        if (ShowAssistantHome) return false;
        _quickSearchSubmission = false;
        if (IsBusy) CancelActiveOperations();
        SetAssistantSurface(_assistantHistory.TryPop(out var previous) ? previous : "home", remember: false);
        return true;
    }

    [RelayCommand]
    private void SelectQuickCriterion(TravelChatGuidedOptionViewModel? option)
    {
        if (option is null) return;
        if (option.Id.StartsWith("category.", StringComparison.Ordinal))
        {
            var category = option.Id["category.".Length..];
            if (!_quickCategories.Remove(category) && _quickCategories.Count < 3) _quickCategories.Add(category);
        }
        else if (option.Id.StartsWith("budget.", StringComparison.Ordinal))
        {
            var budget = option.Id["budget.".Length..];
            if (!_quickBudgets.Remove(budget)) { _quickBudgets.Clear(); _quickBudgets.Add(budget); }
        }
        else if (option.Id.StartsWith("distance.", StringComparison.Ordinal))
        {
            var value = option.Id["distance.".Length..];
            var minutes = int.TryParse(value, out var parsed) ? parsed : 0;
            if (!_quickWalking.Remove(minutes)) { _quickWalking.Clear(); _quickWalking.Add(minutes); }
        }
        RefreshQuickSelections();
    }

    private void RefreshQuickSelections()
    {
        foreach (var option in QuickCategories)
            option.IsSelected = _quickCategories.Contains(option.Id["category.".Length..]);
        foreach (var option in QuickBudgets)
            option.IsSelected = _quickBudgets.Contains(option.Id["budget.".Length..]);
        foreach (var option in QuickWalking)
            option.IsSelected = int.TryParse(option.Id["distance.".Length..], out var minutes)
                && _quickWalking.Contains(minutes);
    }

    [RelayCommand]
    private async Task SubmitQuickSearchAsync()
    {
        if (IsBusy) return;
        var categories = _quickCategories.Count == 0
            ? new[] { GuidedTravelCategories.Food, GuidedTravelCategories.Relax, GuidedTravelCategories.Culture,
                GuidedTravelCategories.Walk, GuidedTravelCategories.Dance, GuidedTravelCategories.Nature,
                GuidedTravelCategories.Shopping, GuidedTravelCategories.Viewpoint, GuidedTravelCategories.Nightlife }
            : _quickCategories.ToArray();
        _guidedCriteria = new GuidedPlanCriteriaDto(
            _quickCategories.FirstOrDefault(), GuidedTravelPriorities.Direct,
            _quickBudgets.FirstOrDefault(), _quickWalking.FirstOrDefault() is > 0 ? _quickWalking.First() : null)
        {
            Categories = categories,
            Budgets = _quickBudgets.ToArray(),
            WalkingMinuteOptions = _quickWalking.Where(value => value > 0).ToArray()
        };
        _pendingGuidedAction = new GuidedTravelActionDto(GuidedTravelActions.Recommend);
        _pendingReplacementCard = null;
        _quickSearchSubmission = true;
        MessageText = Resource("AssistantSearchRequest");
        HasGuidedQuestion = false;
        IsFreeTextVisible = false;
        SetAssistantSurface("conversation");
        try { await SendMessageAsync(); }
        finally { _quickSearchSubmission = false; }
    }

    private async Task ShowAssistantProposalAsync(IReadOnlyList<TravelChatCardViewModel> cards,
        string message, bool isQuickSearch = false)
    {
        StatusMessage = null;
        ProposalRows.Clear();
        _proposalCards.Clear();
        _proposalCards.AddRange(cards);
        _proposalTripId = null;
        _proposalRevision = null;
        _pendingProposalSaveRecommendationId = null;
        _proposalIsQuickSearch = isQuickSearch;
        _proposalMessage = message;
        OnPropertyChanged(nameof(ProposalMessage));
        await RefreshAssistantProposalAsync();
        SetAssistantSurface("proposal", remember: false);
        if (cards.Count > 0)
            await analytics.TrackAsync("proposal_previewed", "assistant", tripId: sessionService.CurrentTripId);
    }

    public async Task RefreshAssistantProposalAsync()
    {
        if (string.IsNullOrWhiteSpace(_proposalMessage)
            || !ShowAssistantProposal && _assistantSurface != "conversation") return;
        var cached = await bootstrapStore.GetCachedAsync();
        var schedule = cached?.Value.Schedule;
        if (schedule is null || schedule.TripId != sessionService.CurrentTripId)
        {
            ErrorMessage = Resource("AssistantProposalNeedsRefresh");
            return;
        }
        var savedThroughEditor = _pendingProposalSaveRecommendationId is { } pendingId
            && schedule.Items.Any(item => item.RecommendationId == pendingId
                && item.Date == DateOnly.FromDateTime(PlanningDate));
        if (!savedThroughEditor && _proposalTripId == schedule.TripId && _proposalRevision.HasValue
            && schedule.Revision != _proposalRevision.Value && _proposalCards.Any(card => !card.IsSaved))
        {
            ErrorMessage = Resource("AssistantProposalNeedsRefresh");
            return;
        }
        _proposalTripId = schedule.TripId;
        _proposalRevision = schedule.Revision;
        _pendingProposalSaveRecommendationId = null;
        ProposalRows.Clear();
        var date = DateOnly.FromDateTime(PlanningDate);
        var rows = _proposalIsQuickSearch
            ? AssistantDayProposalBuilder.BuildQuickSearch(schedule.Items, _proposalCards, date)
            : AssistantDayProposalBuilder.Build(schedule.Items, _proposalCards, date);
        foreach (var row in rows) ProposalRows.Add(row);
        OnPropertyChanged(nameof(HasProposalRows));
    }

    public async Task OpenProposalRowAsync(AssistantProposalRow? row)
    {
        if (row?.Suggestion is { } card) { SelectedDetailCard = card; return; }
        if (row?.SavedItem is { } item)
            await Shell.Current.GoToAsync(nameof(ScheduleItemDetailPage),
                new Dictionary<string, object> { ["ScheduleItem"] = item });
    }

    public void OpenAssistantCard(TravelChatCardViewModel? card) => SelectedDetailCard = card;

    [RelayCommand]
    private void CloseAssistantCard()
    {
        SelectedDetailCard = null;
        // A period choice is local until Save; refresh its label without regenerating ideas.
        var rows = ProposalRows.ToArray();
        ProposalRows.Clear();
        foreach (var row in rows) ProposalRows.Add(row);
    }

    [RelayCommand]
    private Task SaveAssistantCardAsync() => SaveProposalCardAsync(SelectedDetailCard);

    public async Task SaveProposalCardAsync(TravelChatCardViewModel? card)
    {
        if (card is null || !card.CanSave) return;
        if (ShowAssistantProposal && _proposalRevision.HasValue)
        {
            var latest = (await bootstrapStore.GetCachedAsync())?.Value.Schedule;
            if (latest is null || latest.TripId != _proposalTripId || latest.Revision != _proposalRevision.Value)
            {
                ErrorMessage = Resource("AssistantProposalNeedsRefresh");
                SelectedDetailCard = null;
                return;
            }
        }
        _pendingProposalSaveRecommendationId = card.RecommendationId;
        await SaveItineraryItemCommand.ExecuteAsync(card);
        if (card.IsSaved)
        {
            if (SelectedDetailCard == card) SelectedDetailCard = null;
            _proposalRevision = null;
            await RefreshAssistantProposalAsync();
        }
    }

    [RelayCommand]
    private Task ReplaceAssistantCardAsync() => ReplaceProposalCardAsync(SelectedDetailCard);

    public async Task ReplaceProposalCardAsync(TravelChatCardViewModel? card)
    {
        if (card is null || !card.CanFindAlternative || IsBusy) return;
        var message = Messages.FirstOrDefault(item => item.Cards.Contains(card));
        var previousCards = message?.Cards.ToList();
        var wasQuickSearch = _proposalIsQuickSearch;
        if (SelectedDetailCard == card) SelectedDetailCard = null;
        await ReplaceRecommendationCommand.ExecuteAsync(card);
        if (!card.IsDayPlanCard && message is not null && previousCards is not null
            && !message.Cards.SequenceEqual(previousCards))
            await ShowAssistantProposalAsync(message.Cards.ToList(), _proposalMessage, wasQuickSearch);
    }

    [RelayCommand]
    private void AdjustAssistantCard()
    {
        SelectedDetailCard = null;
        AdjustGuidedPlanCommand.Execute(null);
        SetAssistantSurface("conversation");
    }
}

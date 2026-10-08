using System.Collections.ObjectModel;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

// Presentation and external I/O boundaries only. The two production FreeTime partials,
// session, request scope, time window and proposal builder run unchanged in these tests.
public sealed partial class TravelChatViewModel(AuthSessionService sessionService, MobileBootstrapStore bootstrapStore)
    : ViewModelBase
{
    private int _assistantPageOperationVersion;
    private int _assistantSurfaceVersion;
    private TravelChatRequest? _pendingRetryRequest;
    private AssistantRequestScope? _assistantOperation;
    private GuidedPlanCriteriaDto? _guidedCriteria;
    private string? _conversationId;
    private bool _assistantDateSelectedByTraveler;
    private readonly HashSet<string> _quickCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TravelChatCardViewModel> _proposalCards = [];
    private readonly TravelCompanionApiClient apiClient = new();
    private readonly ExpressTestLocationService locationService = new();
    private static readonly TimeSpan TravelChatNetworkTimeout = TimeSpan.FromSeconds(20);
    public DateTime PlanningDate { get; set; }
    public string? City { get; set; } = "Tokyo";
    public DateTime AssistantMaximumDate { get; set; } = DateTime.MaxValue;
    public int SearchOpened { get; private set; }
    public bool DateSelectedByTraveler => _assistantDateSelectedByTraveler;
    public TravelChatRequest? PendingRetryRequest => _pendingRetryRequest;
    public Func<DateTime, Task>? ResolveCity { get; set; }
    public string AssistantFindPlace => "Find a place";
    public string AssistantSearchIntro => "Choose interests";
    public string AssistantSearchAction => "Show ideas";
    public string AssistantYourDay => "Your day";
    public bool ShowAssistantSearch { get; private set; }
    public bool ShowAssistantProposal { get; private set; }
    public TravelChatCardViewModel? SelectedDetailCard { get; private set; }
    public ObservableCollection<TravelChatMessageViewModel> Messages { get; } = [];
    public IReadOnlyList<TravelChatCardViewModel> Options => _proposalCards;
    public TravelCompanionApiClient Api => apiClient;
    public ExpressTestLocationService Location => locationService;
    private static string Resource(string key) => LocalizationResourceManager.Instance[key];
    private void OpenQuickSearch() { SearchOpened++; ShowAssistantSearch = true; }
    private Task UpdateCityForDateAsync(DateTime date) => ResolveCity?.Invoke(date) ?? Task.CompletedTask;
    private void RestoreAssistantSearch() { ShowAssistantSearch = true; ShowAssistantProposal = false; }
    private void RefreshQuickSelections() { }
    public void SelectInterest(string value) => _quickCategories.Add(value);
    public Task SubmitExpressAsync() => SubmitFreeTimeAsync();
    public Task ReplaceExpressAsync(TravelChatCardViewModel card) => ReplaceFreeTimeOptionAsync(card);
    public Task<bool> ValidateExpressSaveAsync(TravelChatCardViewModel card) => ValidateFreeTimeSaveAsync(card);
    public bool BackToInterests() => TryFreeTimeBack();
    private AssistantRequestScope BeginAssistantOperation()
    {
        _assistantOperation?.Cancel();
        var scope = new AssistantRequestScope(sessionService, () => DateOnly.FromDateTime(PlanningDate),
            () => _assistantPageOperationVersion);
        _assistantOperation = scope;
        IsBusy = true;
        return scope;
    }
    private Task ShowAssistantProposalAsync(IReadOnlyList<TravelChatCardViewModel> cards, string message,
        bool isQuickSearch = false, AssistantRequestScope? operation = null)
    {
        operation?.Verify();
        _proposalCards.Clear();
        _proposalCards.AddRange(cards);
        ShowAssistantProposal = true;
        ShowAssistantSearch = false;
        return Task.CompletedTask;
    }
    public Func<Task>? RefreshProposal { get; set; }
    private async Task RefreshAssistantProposalAsync(AssistantRequestScope? operation = null)
    {
        if (RefreshProposal is not null) await RefreshProposal();
        operation?.Verify();
    }
    public void LeavePage() { CancelActiveOperations(); ShowAssistantSearch = false; ShowAssistantProposal = false; }
    public void ChangeSurface() { _assistantSurfaceVersion++; _isFreeTimeSearch = false; }
    public void CancelActiveOperations() { _assistantPageOperationVersion++; _assistantOperation?.Cancel(); IsBusy = false; }
    public void ResetExpressForSession() { CancelActiveOperations(); _isFreeTimeSearch = false; ResetFreeTimeState(); }
}

public sealed class ExpressTestLocationService : ILocationService
{
    public int Requests { get; private set; }
    public Func<CancellationToken, Task<GeoPointDto?>> Resolve { get; set; } = _ =>
        Task.FromResult<GeoPointDto?>(new(35.68m, 139.76m));
    public Task<GeoPointDto?> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    { Requests++; return Resolve(cancellationToken); }
    public Task<GeoPointDto?> GetLastKnownLocationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<GeoPointDto?>(null);
}

using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

// Only the other Assistant surfaces are controlled here. FreeTime setup executes
// its production partial, with the real session and ViewModelBase.
public sealed partial class TravelChatViewModel(AuthSessionService sessionService, MobileBootstrapStore bootstrapStore)
    : ViewModelBase
{
    private int _assistantPageOperationVersion;
    private int _assistantSurfaceVersion;
    private TravelChatRequest? _pendingRetryRequest;
    private bool _assistantDateSelectedByTraveler;
    public DateTime PlanningDate { get; set; }
    public DateTime AssistantMaximumDate { get; set; } = DateTime.MaxValue;
    public int SearchOpened { get; private set; }
    public bool DateSelectedByTraveler => _assistantDateSelectedByTraveler;
    public TravelChatRequest? PendingRetryRequest => _pendingRetryRequest;
    public Func<DateTime, Task>? ResolveCity { get; set; }
    public string AssistantFindPlace => "Find a place";
    public string AssistantSearchIntro => "Choose interests";
    public string AssistantSearchAction => "Show ideas";
    private static string Resource(string key) => LocalizationResourceManager.Instance[key];
    private void OpenQuickSearch() => SearchOpened++;
    private Task UpdateCityForDateAsync(DateTime date) => ResolveCity?.Invoke(date) ?? Task.CompletedTask;
    public void LeavePage() => _assistantPageOperationVersion++;
    public void ChangeSurface() => _assistantSurfaceVersion++;
}

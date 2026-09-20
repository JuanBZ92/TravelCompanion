using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class TravelChatCardViewModel : ObservableObject
{
    private readonly TravelCardDto _card;
    private bool _isSaved;
    private string? _feedbackStatusMessage;
    private bool _isDetailsVisible;

    public TravelChatCardViewModel(TravelCardDto card)
    {
        _card = card;
        RecommendationId = Guid.TryParse(card.RecommendationId, out var recommendationId)
            ? recommendationId
            : null;
        StartsAt = TimeOnly.TryParse(card.StartTime, out var startsAt)
            ? startsAt
            : null;
        EndsAt = TimeOnly.TryParse(card.EndTime, out var endsAt)
            ? endsAt
            : null;
        TimePrecision = card.IsPeriodOnly
            ? ItineraryTimePrecision.PeriodOnly
            : ItineraryTimePrecision.Exact;
        LocalizationResourceManager.Instance.CultureChanged += OnCultureChanged;
    }

    public string Title => _card.Title;
    public string? Subtitle => _card.Subtitle;
    public string? Description => _card.Description;
    public string CostLabel => string.IsNullOrWhiteSpace(FormatCost(_card.EstimatedCost))
        ? string.Empty
        : $"{Resource("AssistantCostPrefix")}: {FormatCost(_card.EstimatedCost)}";
    public bool HasCostLabel => !string.IsNullOrWhiteSpace(CostLabel);
    public string DistanceLabel => _card.DistanceKm.HasValue
        ? $"{Resource("AssistantDistancePrefix")}: {_card.DistanceKm.Value.ToString("0.0", CultureInfo.CurrentCulture)} km"
        : string.Empty;
    public bool HasDistanceLabel => !string.IsNullOrWhiteSpace(DistanceLabel);
    public string WalkingLabel => _card.WalkingMinutes.HasValue
        ? $"{Resource("AssistantWalkingPrefix")}: {_card.WalkingMinutes.Value} min"
        : string.Empty;
    public bool HasWalkingLabel => !string.IsNullOrWhiteSpace(WalkingLabel);
    public Guid? RecommendationId { get; }
    public bool HasRecommendationId => RecommendationId.HasValue;
    public bool HasDetailAction => HasRecommendationId;
    public string RecommendationReference => RecommendationId?.ToString() ?? Title;
    public TimeOnly? StartsAt { get; }
    public TimeOnly? EndsAt { get; }
    public ItineraryTimePrecision TimePrecision { get; }
    public bool CanSave => RecommendationId.HasValue && !IsSaved;
    public string SaveButtonText => IsSaved ? Resource("AssistantSavedButton") : Resource("AssistantSaveButton");
    public string DetailButtonText => Resource("AssistantDetailButton");
    public string NearbyButtonText => Resource("AssistantNearbyButton");
    public string ReplaceButtonText => Resource("AssistantReplaceButton");
    public string UsefulButtonText => Resource("AssistantUsefulButton");
    public string NotUsefulButtonText => Resource("AssistantNotUsefulButton");
    public string HideSimilarButtonText => Resource("AssistantHideSimilarButton");
    public string AdjustButtonText => Resource("AssistantGuidedAdjust" );
    public string SummaryLine => CreateSummaryLine();
    public bool IsDetailsVisible
    {
        get => _isDetailsVisible;
        private set => SetProperty(ref _isDetailsVisible, value);
    }
    public bool HasFeedbackActions => HasRecommendationId;
    public string? FeedbackStatusMessage
    {
        get => _feedbackStatusMessage;
        set
        {
            if (SetProperty(ref _feedbackStatusMessage, value))
            {
                OnPropertyChanged(nameof(HasFeedbackStatus));
            }
        }
    }
    public bool HasFeedbackStatus => !string.IsNullOrWhiteSpace(FeedbackStatusMessage);
    public bool IsSaved
    {
        get => _isSaved;
        set
        {
            if (SetProperty(ref _isSaved, value))
            {
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }
    public string TimeLabel => TimePrecision != ItineraryTimePrecision.Exact || string.IsNullOrWhiteSpace(_card.StartTime)
        ? string.Empty
        : string.IsNullOrWhiteSpace(_card.EndTime)
            ? $"{Resource("AssistantTimePrefix")}: {_card.StartTime!}"
            : $"{Resource("AssistantTimePrefix")}: {_card.StartTime} - {_card.EndTime}";
    public bool HasTimeLabel => !string.IsNullOrWhiteSpace(TimeLabel);
    public IReadOnlyList<string> Tags => (_card.Tags ?? []).Take(4).ToList();
    public IReadOnlyList<TravelChatTagActionViewModel> TagActions => Tags
        .Select(tag => new TravelChatTagActionViewModel(tag))
        .ToList();
    public bool HasTags => Tags.Count > 0;
    public IReadOnlyList<string> WhyItFits => _card.WhyItFits.Take(2).ToList();
    public bool HasReasons => WhyItFits.Count > 0;
    public IReadOnlyList<string> Warnings => _card.Warnings.Take(1).ToList();
    public IReadOnlyList<string> WarningLabels => Warnings
        .Select(warning => $"{Resource("AssistantAttentionPrefix")}: {warning}")
        .ToList();
    public bool HasWarnings => Warnings.Count > 0;

    [RelayCommand]
    private void ToggleDetails() => IsDetailsVisible = !IsDetailsVisible;

    private string CreateSummaryLine()
    {
        var values = new List<string>();
        if (StartsAt.HasValue && EndsAt.HasValue)
        {
            var minutes = (int)(EndsAt.Value.ToTimeSpan() - StartsAt.Value.ToTimeSpan()).TotalMinutes;
            if (minutes > 0)
            {
                values.Add($"≈ {minutes} min");
            }
        }
        if (HasCostLabel)
        {
            values.Add(CostLabel);
        }
        if (HasDistanceLabel)
        {
            values.Add(DistanceLabel);
        }
        if (HasWalkingLabel)
        {
            values.Add($"{WalkingLabel} ({Resource("AssistantGuidedEstimated")})");
        }
        return string.Join(" · ", values);
    }

    private static string FormatCost(string? cost)
    {
        return cost?.Trim().ToLowerInvariant() switch
        {
            null or "" => string.Empty,
            "free" or "gratis" => Resource("AssistantFreeCost"),
            "low" or "budget" or "cheap" or "barato" => Resource("AssistantLowCost"),
            "medium" or "moderate" or "medio" => Resource("AssistantMediumCost"),
            "high" or "expensive" or "premium" or "alto" => Resource("AssistantHighCost"),
            _ => cost.Trim()
        };
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CostLabel));
        OnPropertyChanged(nameof(DistanceLabel));
        OnPropertyChanged(nameof(WalkingLabel));
        OnPropertyChanged(nameof(TimeLabel));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(DetailButtonText));
        OnPropertyChanged(nameof(NearbyButtonText));
        OnPropertyChanged(nameof(ReplaceButtonText));
        OnPropertyChanged(nameof(UsefulButtonText));
        OnPropertyChanged(nameof(NotUsefulButtonText));
        OnPropertyChanged(nameof(HideSimilarButtonText));
        OnPropertyChanged(nameof(AdjustButtonText));
        OnPropertyChanged(nameof(SummaryLine));
        OnPropertyChanged(nameof(TagActions));
        OnPropertyChanged(nameof(WarningLabels));
    }

    private static string Resource(string key)
    {
        return LocalizationResourceManager.Instance[key];
    }
}

public sealed class TravelChatTagActionViewModel(string tag)
{
    public string Tag { get; } = tag;
    public string Label => $"{LocalizationResourceManager.Instance["AssistantAvoidTagPrefix"]} #{Tag}";
}

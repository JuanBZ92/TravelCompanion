using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class DayReviewViewModel
{
    public DayReviewViewModel(DayReviewDto review)
    {
        Date = review.Date;
        Status = review.Status;
        Title = review.Title;
        Summary = review.Summary;
        Issues = review.Issues.Select(issue => new DayReviewIssueViewModel(issue)).ToList();
    }

    public DateOnly Date { get; }
    public string Status { get; }
    public string Title { get; }
    public string Summary { get; }
    public IReadOnlyList<DayReviewIssueViewModel> Issues { get; }
    public bool HasIssues => Issues.Count > 0;
    public string AssistantActionLabel => HasIssues ? "Proponer un plan mejor" : "Mejorar este día";
    public string Eyebrow => Status == DayReviewStatuses.Balanced ? "DÍA REVISADO" : "REVISAR MI DÍA";
    public string StatusGlyph => Status == DayReviewStatuses.Balanced ? "✓" : "!";
    public string AccentColor => Status switch
    {
        DayReviewStatuses.NeedsAttention => "#B5483F",
        DayReviewStatuses.Tight => "#A86D19",
        _ => "#39705A"
    };
    public string BackgroundColor => Status switch
    {
        DayReviewStatuses.NeedsAttention => "#FFF1EF",
        DayReviewStatuses.Tight => "#FFF7E8",
        _ => "#EDF6F1"
    };
}

public sealed class DayReviewIssueViewModel
{
    private readonly DayReviewIssueDto _issue;

    public DayReviewIssueViewModel(DayReviewIssueDto issue)
    {
        _issue = issue;
    }

    public DayReviewIssueDto Issue => _issue;
    public string Title => _issue.Title;
    public string Message => _issue.Message;
    public IReadOnlyList<Guid> ItemIds => _issue.ItemIds;
    public bool CanOpenPlan => _issue.Kind != DayReviewIssueKinds.PackedDay && _issue.ItemIds.Count > 0;
    public string ActionLabel => _issue.Kind == DayReviewIssueKinds.Overlap ? "Corregir horario" : "Ajustar plan";
    public string SeverityColor => _issue.Severity switch
    {
        DayReviewSeverities.Critical => "#B5483F",
        DayReviewSeverities.Warning => "#A86D19",
        _ => "#39705A"
    };
}

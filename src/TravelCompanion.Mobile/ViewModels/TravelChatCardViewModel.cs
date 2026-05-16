using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class TravelChatCardViewModel(TravelCardDto card)
{
    public string Title => card.Title;
    public string? Subtitle => card.Subtitle;
    public string? Description => card.Description;
    public string TimeLabel => string.IsNullOrWhiteSpace(card.StartTime)
        ? string.Empty
        : string.IsNullOrWhiteSpace(card.EndTime)
            ? card.StartTime!
            : $"{card.StartTime} - {card.EndTime}";
    public bool HasTimeLabel => !string.IsNullOrWhiteSpace(TimeLabel);
    public IReadOnlyList<string> WhyItFits => card.WhyItFits;
    public bool HasReasons => WhyItFits.Count > 0;
    public IReadOnlyList<string> Warnings => card.Warnings;
    public bool HasWarnings => Warnings.Count > 0;
}

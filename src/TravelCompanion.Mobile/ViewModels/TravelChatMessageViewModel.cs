namespace TravelCompanion.Mobile.ViewModels;

public sealed class TravelChatMessageViewModel(
    string text,
    bool isFromUser,
    IReadOnlyList<TravelChatCardViewModel>? cards = null)
{
    public string Text { get; } = text;
    public bool IsFromUser { get; } = isFromUser;
    public bool IsFromAssistant => !IsFromUser;
    public IReadOnlyList<TravelChatCardViewModel> Cards { get; } = cards ?? [];
    public bool HasCards => Cards.Count > 0;
}

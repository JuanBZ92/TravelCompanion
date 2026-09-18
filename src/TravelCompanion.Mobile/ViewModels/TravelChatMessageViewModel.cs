using System.Collections.ObjectModel;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class TravelChatMessageViewModel(
    string text,
    bool isFromUser,
    IReadOnlyList<TravelChatCardViewModel>? cards = null)
{
    public string Text { get; } = text;
    public bool IsFromUser { get; } = isFromUser;
    public bool IsFromAssistant => !IsFromUser;
    public ObservableCollection<TravelChatCardViewModel> Cards { get; } = new(cards ?? []);
    public bool HasCards => Cards.Count > 0;
    public bool ShouldShowText => !HasCards && !string.IsNullOrWhiteSpace(Text);

    public bool ReplaceCard(TravelChatCardViewModel current, TravelChatCardViewModel replacement)
    {
        var index = Cards.IndexOf(current);
        if (index < 0)
        {
            return false;
        }

        Cards[index] = replacement;
        return true;
    }
}

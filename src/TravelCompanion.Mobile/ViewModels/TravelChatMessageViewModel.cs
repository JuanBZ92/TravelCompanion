using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class TravelChatMessageViewModel : ObservableObject
{
    private string _text;
    private bool _isLoading;

    public TravelChatMessageViewModel(
        string text,
        bool isFromUser,
        IReadOnlyList<TravelChatCardViewModel>? cards = null,
        bool isProgressMessage = false,
        bool isLoading = false)
    {
        _text = text;
        _isLoading = isLoading;
        IsFromUser = isFromUser;
        IsProgressMessage = isProgressMessage;
        Cards = new ObservableCollection<TravelChatCardViewModel>(cards ?? []);
    }

    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsFromUser { get; }
    public bool IsFromAssistant => !IsFromUser;
    public bool IsProgressMessage { get; }
    public ObservableCollection<TravelChatCardViewModel> Cards { get; }
    public bool HasCards => Cards.Count > 0;
    public bool ShouldShowText => !IsProgressMessage && !HasCards && !string.IsNullOrWhiteSpace(Text);

    public void UpdateProgress(string text, bool isLoading)
    {
        Text = text;
        IsLoading = isLoading;
    }

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

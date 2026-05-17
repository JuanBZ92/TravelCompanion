using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class TravelChatPage : ContentPage
{
    private readonly TravelChatViewModel _viewModel;

    public TravelChatPage()
        : this(MauiProgram.Services.GetRequiredService<TravelChatViewModel>())
    {
    }

    public TravelChatPage(TravelChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadContextAsync();
    }

    private async void OnSuggestedReplyTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is string reply)
        {
            await _viewModel.SendSuggestedReplyCommand.ExecuteAsync(reply);
        }
    }

    private async void OnSaveItineraryItemClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.SaveItineraryItemCommand.ExecuteAsync(card);
        }
    }

    private async void OnOpenRecommendationDetailClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.OpenRecommendationDetailCommand.ExecuteAsync(card);
        }
    }

    private async void OnRequestLessWalkingClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.RequestLessWalkingCommand.ExecuteAsync(card);
        }
    }

    private async void OnReplaceRecommendationClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.ReplaceRecommendationCommand.ExecuteAsync(card);
        }
    }
}

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
}

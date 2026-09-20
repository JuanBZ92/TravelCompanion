using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class AccountPage : ContentPage
{
    private readonly AccountViewModel _viewModel;

    public AccountPage() : this(MauiProgram.Services.GetRequiredService<AccountViewModel>()) { }

    public AccountPage(AccountViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAccountCommand.ExecuteAsync(null);
    }
}

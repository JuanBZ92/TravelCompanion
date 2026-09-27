using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class AccountPage : ContentPage
{
    private async void OnDocumentsClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(DocsPage));
    private async void OnPassClicked(object? sender, EventArgs e) => await Services.PaywallNavigation.OpenAsync(Shared.Dtos.PaywallEntryPoint.Today);
    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is AppShell shell) await shell.SignOutAsync();
    }
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

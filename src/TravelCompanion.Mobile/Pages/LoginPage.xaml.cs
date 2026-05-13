using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class LoginPage : ContentPage
{
    public LoginPage()
        : this(MauiProgram.Services.GetRequiredService<LoginViewModel>())
    {
    }

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

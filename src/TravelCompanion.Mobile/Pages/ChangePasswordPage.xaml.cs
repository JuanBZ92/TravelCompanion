using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class ChangePasswordPage : ContentPage
{
    public ChangePasswordPage()
        : this(MauiProgram.Services.GetRequiredService<ChangePasswordViewModel>())
    {
    }

    public ChangePasswordPage(ChangePasswordViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

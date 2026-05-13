namespace TravelCompanion.Mobile.Pages;

public partial class SupportPage : ContentPage
{
    public SupportPage()
        : this(MauiProgram.Services.GetRequiredService<ViewModels.SupportViewModel>())
    {
    }

    public SupportPage(ViewModels.SupportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

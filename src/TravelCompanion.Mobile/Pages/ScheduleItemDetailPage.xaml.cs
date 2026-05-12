using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class ScheduleItemDetailPage : ContentPage
{
    public ScheduleItemDetailPage()
        : this(MauiProgram.Services.GetRequiredService<ScheduleItemDetailViewModel>())
    {
    }

    public ScheduleItemDetailPage(ScheduleItemDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

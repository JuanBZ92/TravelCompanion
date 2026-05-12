using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class SchedulePage : ContentPage
{
    private readonly ScheduleViewModel _viewModel;
    private bool _loaded;

    public SchedulePage()
        : this(MauiProgram.Services.GetRequiredService<ScheduleViewModel>())
    {
    }

    public SchedulePage(ScheduleViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadScheduleCommand.ExecuteAsync(null);
    }
}

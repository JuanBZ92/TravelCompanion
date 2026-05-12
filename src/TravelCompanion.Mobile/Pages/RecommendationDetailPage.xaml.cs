using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class RecommendationDetailPage : ContentPage
{
    public RecommendationDetailPage()
        : this(MauiProgram.Services.GetRequiredService<RecommendationDetailViewModel>())
    {
    }

    public RecommendationDetailPage(RecommendationDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

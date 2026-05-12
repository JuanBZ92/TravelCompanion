using TravelCompanion.Mobile.Pages;

namespace TravelCompanion.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(RecommendationDetailPage), typeof(RecommendationDetailPage));
        Routing.RegisterRoute(nameof(ScheduleItemDetailPage), typeof(ScheduleItemDetailPage));
    }
}

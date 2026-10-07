using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class PaywallPresentation
{
    public static string HeadingKey(PaywallEntryPoint entryPoint) => entryPoint switch
    {
        PaywallEntryPoint.Map => "PaywallHeadingMap",
        PaywallEntryPoint.Today or PaywallEntryPoint.Routes => "PaywallHeadingToday",
        PaywallEntryPoint.Assistant => "PaywallHeadingAssistant",
        PaywallEntryPoint.Offline => "PaywallHeadingOffline",
        PaywallEntryPoint.Expenses => "PaywallHeadingExpenses",
        _ => "PaywallHeroTitle"
    };
}

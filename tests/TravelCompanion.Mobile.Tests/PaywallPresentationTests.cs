using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class PaywallPresentationTests
{
    [Theory]
    [InlineData(PaywallEntryPoint.Map, "PaywallHeadingMap")]
    [InlineData(PaywallEntryPoint.Today, "PaywallHeadingToday")]
    [InlineData(PaywallEntryPoint.Routes, "PaywallHeadingToday")]
    [InlineData(PaywallEntryPoint.Assistant, "PaywallHeadingAssistant")]
    [InlineData(PaywallEntryPoint.Offline, "PaywallHeadingOffline")]
    [InlineData(PaywallEntryPoint.Expenses, "PaywallHeadingExpenses")]
    [InlineData(PaywallEntryPoint.ExplicitUpgrade, "PaywallHeroTitle")]
    [InlineData((PaywallEntryPoint)999, "PaywallHeroTitle")]
    public void Contextual_heading_uses_the_current_entry_point_and_safe_default(PaywallEntryPoint entryPoint, string key) =>
        Assert.Equal(key, PaywallPresentation.HeadingKey(entryPoint));
}

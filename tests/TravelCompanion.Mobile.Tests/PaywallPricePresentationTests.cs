using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public class PaywallPricePresentationTests
{
    [Theory]
    [InlineData(true, true, "24,99 €", "24,99 €", true)]
    [InlineData(true, true, "$29.99", "$29.99", true)]
    [InlineData(true, true, null, "", false)]
    [InlineData(true, true, "  ", "", false)]
    [InlineData(true, false, "24,99 €", "", false)]
    [InlineData(false, true, "24,99 €", "24,99 €", false)]
    public void Purchase_requires_available_product_localized_price_and_server_permission(
        bool allowed, bool available, string? localized, string expectedPrice, bool expectedCanBuy)
    {
        var result = PaywallPricePresentation.Create(allowed, available, localized);
        Assert.Equal(expectedPrice, result.Price);
        Assert.Equal(expectedCanBuy, result.CanBuy);
    }
}

namespace TravelCompanion.Mobile.Services;

internal sealed record PaywallPricePresentation(string Price, bool CanBuy)
{
    public static PaywallPricePresentation Create(bool offerAllowsPurchase, bool productAvailable, string? localizedPrice)
    {
        var price = productAvailable ? localizedPrice?.Trim() ?? string.Empty : string.Empty;
        return new(price, offerAllowsPurchase && productAvailable && price.Length > 0);
    }
}

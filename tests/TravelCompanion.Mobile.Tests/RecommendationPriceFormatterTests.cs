using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class RecommendationPriceFormatterTests
{
    [Theory]
    [InlineData(null, "Medio")]
    [InlineData("", "Medio")]
    [InlineData("   ", "Medio")]
    [InlineData(" FREE ", "Gratis")]
    [InlineData("gratis", "Gratis")]
    [InlineData("low", "Bajo")]
    [InlineData("budget", "Bajo")]
    [InlineData("cheap", "Bajo")]
    [InlineData("barato", "Bajo")]
    [InlineData("medium", "Medio")]
    [InlineData("moderate", "Medio")]
    [InlineData("medio", "Medio")]
    [InlineData("high", "Alto")]
    [InlineData("expensive", "Alto")]
    [InlineData("premium", "Alto")]
    [InlineData("alto", "Alto")]
    [InlineData("  ¥2,000–¥3,000  ", "¥2,000–¥3,000")]
    public void Existing_labels_and_unknown_price_text_are_preserved(string? value, string expected) =>
        Assert.Equal(expected, RecommendationPriceFormatter.Format(value));
}

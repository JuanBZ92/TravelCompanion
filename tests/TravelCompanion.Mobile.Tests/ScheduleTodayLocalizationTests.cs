using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[CollectionDefinition("Schedule localization", DisableParallelization = true)]
public sealed class ScheduleLocalizationCollection;

[Collection("Schedule localization")]
public sealed class ScheduleTodayLocalizationTests : IDisposable
{
    private readonly string _originalCulture = LocalizationResourceManager.Instance.CurrentCulture.Name;

    [Theory]
    [InlineData("es", "morning", "Mañana")]
    [InlineData("es", "midday", "Mediodía")]
    [InlineData("es", "afternoon", "Tarde")]
    [InlineData("es", "night", "Noche")]
    [InlineData("en-US", "morning", "Morning")]
    [InlineData("en-US", "midday", "Midday")]
    [InlineData("en-US", "afternoon", "Afternoon")]
    [InlineData("en-US", "night", "Evening")]
    public void Known_period_uses_the_selected_language_instead_of_the_api_label(
        string culture, string key, string expected)
    {
        LocalizationResourceManager.Instance.SetCulture(culture);
        var section = Section(key, "Etiqueta del backend", string.Empty);

        Assert.Equal(expected, section.PeriodLabel);
        Assert.Equal(key, section.PeriodKey);
        Assert.Equal(expected, ScheduleTodaySectionViewModel.ResolvePeriodLabel(key, "Etiqueta del backend"));
    }

    [Theory]
    [InlineData("es", "Día 4", "Libre")]
    [InlineData("en-US", "Day 4", "Free")]
    public void Generated_day_title_and_free_block_are_localized(string culture, string day, string free)
    {
        LocalizationResourceManager.Instance.SetCulture(culture);
        var section = Section("morning", "Mañana", "Texto del backend");

        Assert.Equal(day, section.Title);
        Assert.Equal(free, section.Description);
        Assert.False(section.ShowDescription);
        Assert.Equal(day, ScheduleTodaySectionViewModel.FormatDayTitle(4));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en-US")]
    public void Unknown_period_and_traveler_description_remain_unchanged(string culture)
    {
        LocalizationResourceManager.Instance.SetCulture(culture);
        const string travelerDescription = "Mis planes en 日本, sin traducción automática.";
        var section = Section("custom-period", "Mi momento especial", travelerDescription, populated: true);

        Assert.Equal("Mi momento especial", section.PeriodLabel);
        Assert.Equal(travelerDescription, section.Description);
        Assert.True(section.ShowDescription);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en-US")]
    public void Localizing_the_heading_still_hides_existing_generated_spanish_captions(string culture)
    {
        LocalizationResourceManager.Instance.SetCulture(culture);
        var section = Section("morning", "Mañana", "Mañana del día 4 en Tokyo.", populated: true);

        Assert.Empty(section.Description);
        Assert.False(section.ShowDescription);
    }

    [Fact]
    public void Existing_section_resolves_generated_text_from_the_current_language()
    {
        LocalizationResourceManager.Instance.SetCulture("es");
        var section = Section("morning", "Mañana", string.Empty);
        Assert.Equal("Mañana", section.PeriodLabel);

        LocalizationResourceManager.Instance.SetCulture("en-US");

        Assert.Equal("Morning", section.PeriodLabel);
        Assert.Equal("Day 4", section.Title);
        Assert.Equal("Free", section.Description);
    }

    private static ScheduleTodaySectionViewModel Section(
        string key, string label, string description, bool populated = false)
    {
        TodayLocationViewModel[] locations = populated
            ? [new(new RecommendationDto(Guid.NewGuid(), Guid.NewGuid(), "Lugar del usuario", "Culture",
                "Tokyo", string.Empty, [], "medium", 35m, 139m, 60, null, null,
                ContentAccessLevel.Free, [], null), null)]
            : [];
        return new(4, new DateOnly(2026, 10, 20), key, label, description, locations, []);
    }

    public void Dispose() => LocalizationResourceManager.Instance.SetCulture(_originalCulture);
}

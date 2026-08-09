using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace TravelCompanion.Mobile.Services;

public sealed class LocalizationResourceManager : INotifyPropertyChanged
{
    private const string PreferenceKey = "app_language";
    private static readonly Lazy<LocalizationResourceManager> LazyInstance = new(() => new LocalizationResourceManager());
    private readonly ResourceManager _resourceManager = new(
        "TravelCompanion.Mobile.Localization.AppResources",
        typeof(LocalizationResourceManager).Assembly);

    private CultureInfo _currentCulture = new("en-US");

    private LocalizationResourceManager()
    {
    }

    public static LocalizationResourceManager Instance => LazyInstance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CultureChanged;

    public CultureInfo CurrentCulture => _currentCulture;

    public string this[string resourceKey] => GetString(resourceKey);

    public void Initialize()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        var savedCulture = Preferences.Default.Get(PreferenceKey, string.Empty);
#else
        var savedCulture = string.Empty;
#endif
        var cultureName = string.IsNullOrWhiteSpace(savedCulture)
            ? ResolveDeviceCulture(CultureInfo.CurrentUICulture)
            : savedCulture;
        SetCulture(cultureName, persist: false);
    }

    public void SetCulture(string cultureName)
    {
        SetCulture(cultureName, persist: true);
    }

    public string GetString(string resourceKey)
    {
        try
        {
            return _resourceManager.GetString(resourceKey, _currentCulture)
                ?? _resourceManager.GetString(resourceKey, CultureInfo.GetCultureInfo("en-US"))
                ?? FallbackString(resourceKey);
        }
        catch (MissingManifestResourceException)
        {
            return FallbackString(resourceKey);
        }
    }

    private void SetCulture(string cultureName, bool persist)
    {
        var normalized = ResolveDeviceCulture(CultureInfo.GetCultureInfo(cultureName));
        var culture = CultureInfo.GetCultureInfo(normalized);
        _currentCulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (persist)
        {
#if ANDROID || IOS || MACCATALYST || WINDOWS
            Preferences.Default.Set(PreferenceKey, normalized);
#endif
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string ResolveDeviceCulture(CultureInfo culture)
    {
        return culture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase)
            ? "es"
            : "en-US";
    }

    private static string FallbackString(string resourceKey)
    {
        return resourceKey switch
        {
            "AssistantCostPrefix" => "Cost",
            "AssistantDistancePrefix" => "Distance",
            "AssistantWalkingPrefix" => "Walk",
            "AssistantTimePrefix" => "Time",
            "AssistantSaveButton" => "Save",
            "AssistantSavedButton" => "Saved",
            "AssistantAvoidTagPrefix" => "Avoid",
            "AssistantLowCost" => "Low",
            "AssistantMediumCost" => "Medium",
            "AssistantHighCost" => "High",
            "AssistantFreeCost" => "Free",
            "AssistantAttentionPrefix" => "Attention",
            _ => resourceKey
        };
    }
}

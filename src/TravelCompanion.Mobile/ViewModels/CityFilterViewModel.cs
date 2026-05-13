using CommunityToolkit.Mvvm.ComponentModel;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class CityFilterViewModel : ObservableObject
{
    // Light theme colors
    private static readonly Color AccentColor = Color.FromArgb("#3D3329");
    private static readonly Color MistColor = Color.FromArgb("#F5F3F0");
    private static readonly Color PaperColor = Color.FromArgb("#FCFBF9");
    private static readonly Color InkColor = Color.FromArgb("#1A1714");
    private static readonly Color LineColor = Color.FromArgb("#1A171414");

    private readonly string _cityName;
    private bool _isSelected;

    public CityFilterViewModel(string cityName, bool isSelected = false)
    {
        _cityName = cityName;
        _isSelected = isSelected;
    }

    public string CityName
    {
        get => _cityName;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(BackgroundColor));
                OnPropertyChanged(nameof(TextColor));
                OnPropertyChanged(nameof(BorderColor));
            }
        }
    }

    public bool IsAllCities => _cityName == "All Cities";

    public Color BackgroundColor => IsSelected ? AccentColor : MistColor;
    public Color TextColor => IsSelected ? PaperColor : InkColor;
    public Color BorderColor => IsSelected ? AccentColor : LineColor;
}

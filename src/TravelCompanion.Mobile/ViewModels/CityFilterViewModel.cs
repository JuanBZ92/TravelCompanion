using CommunityToolkit.Mvvm.ComponentModel;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class CityFilterViewModel : ObservableObject
{
    private static readonly Color AccentColor = Color.FromArgb("#3D3329");
    private static readonly Color PaperColor = Color.FromArgb("#FFFFFF");
    private static readonly Color MutedColor = Color.FromArgb("#8A8078");
    private static readonly Color TransparentColor = Color.FromArgb("#00FFFFFF");
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

    public string DisplayName => IsAllCities ? "Todas" : _cityName;

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

    public Color BackgroundColor => IsSelected ? PaperColor : TransparentColor;
    public Color TextColor => IsSelected ? AccentColor : MutedColor;
    public Color BorderColor => IsSelected ? LineColor : TransparentColor;
}

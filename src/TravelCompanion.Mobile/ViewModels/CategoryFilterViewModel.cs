using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class CategoryFilterViewModel : ObservableObject
{
    private static readonly Color AccentColor = Color.FromArgb("#3D3329");
    private static readonly Color PaperColor = Color.FromArgb("#FFFFFF");
    private static readonly Color MutedColor = Color.FromArgb("#8A8078");
    private static readonly Color TransparentColor = Color.FromArgb("#00FFFFFF");
    private static readonly Color GoldColor = Color.FromArgb("#B8956A");
    private static readonly Color LineColor = Color.FromArgb("#1A171414");

    private bool _isSelected;

    public CategoryFilterViewModel(string label, bool isSelected = false)
    {
        Label = label;
        _isSelected = isSelected;
    }

    public string Label { get; }

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

    public Color BackgroundColor => IsSelected ? PaperColor : TransparentColor;
    public Color TextColor => IsSelected ? AccentColor : MutedColor;
    public Color BorderColor => IsSelected ? GoldColor : LineColor;
}

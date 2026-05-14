using CommunityToolkit.Mvvm.ComponentModel;
using TravelCompanion.Shared;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleTypeFilterViewModel : ObservableObject
{
    private static readonly Color AccentColor = Color.FromArgb("#3D3329");
    private static readonly Color MistColor = Color.FromArgb("#F5F3F0");
    private static readonly Color PaperColor = Color.FromArgb("#FCFBF9");
    private static readonly Color InkColor = Color.FromArgb("#1A1714");
    private static readonly Color LineColor = Color.FromArgb("#1A171414");

    private bool _isSelected;

    public ScheduleTypeFilterViewModel(ReservationType type, bool isSelected = false)
    {
        Type = type;
        _isSelected = isSelected;
    }

    public ReservationType Type { get; }

    public string Label => Type switch
    {
        ReservationType.Flight => "Vuelos",
        ReservationType.Lodging => "Hospedajes",
        _ => "Eventos"
    };

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

    public Color BackgroundColor => IsSelected ? AccentColor : MistColor;
    public Color TextColor => IsSelected ? PaperColor : InkColor;
    public Color BorderColor => IsSelected ? AccentColor : LineColor;
}

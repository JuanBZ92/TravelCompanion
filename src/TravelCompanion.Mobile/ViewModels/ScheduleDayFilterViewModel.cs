using CommunityToolkit.Mvvm.ComponentModel;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleDayFilterViewModel : ObservableObject
{
    private static readonly Color InkColor = Color.FromArgb("#1A1714");
    private static readonly Color PaperColor = Color.FromArgb("#FFFFFF");
    private static readonly Color MutedColor = Color.FromArgb("#8A8078");
    private static readonly Color LineColor = Color.FromArgb("#1A171414");
    private static readonly Color TransparentColor = Color.FromArgb("#00FFFFFF");

    private bool _isSelected;

    public ScheduleDayFilterViewModel(
        DateOnly date,
        int tripDayNumber,
        string city,
        bool isSelected = false)
    {
        Date = date;
        TripDayNumber = tripDayNumber;
        City = city;
        _isSelected = isSelected;
    }

    public DateOnly Date { get; }
    public int TripDayNumber { get; }
    public string City { get; }
    public string DayLabel => $"DIA {TripDayNumber}";
    public string DateLabel => Date.Day.ToString();

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(BackgroundColor));
                OnPropertyChanged(nameof(BorderColor));
                OnPropertyChanged(nameof(PrimaryTextColor));
                OnPropertyChanged(nameof(SecondaryTextColor));
            }
        }
    }

    public Color BackgroundColor => IsSelected ? InkColor : PaperColor;
    public Color BorderColor => IsSelected ? InkColor : LineColor;
    public Color PrimaryTextColor => IsSelected ? PaperColor : InkColor;
    public Color SecondaryTextColor => IsSelected ? PaperColor : MutedColor;
}

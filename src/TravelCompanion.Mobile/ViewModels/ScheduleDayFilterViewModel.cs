using CommunityToolkit.Mvvm.ComponentModel;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleDayFilterViewModel : ObservableObject
{
    private static readonly Color InkColor = Color.FromArgb("#1A1714");
    private static readonly Color PaperColor = Color.FromArgb("#FAF6F0");
    private static readonly Color MutedColor = Color.FromArgb("#8A8078");
    private static readonly Color LineColor = Color.FromArgb("#1A171414");
    private static readonly Color TransparentColor = Color.FromArgb("#00FFFFFF");

    private bool _isSelected;
    private string _city;
    private string? _stayTitle;
    private bool _canOpenMap;
    private double _textScale = 1;

    public ScheduleDayFilterViewModel(
        DateOnly date,
        int tripDayNumber,
        string city,
        bool isSelected = false, bool isLocked = false)
    {
        IsLocked = isLocked;
        Date = date;
        TripDayNumber = tripDayNumber;
        _city = city;
        _isSelected = isSelected;
    }

    public DateOnly Date { get; }
    public int TripDayNumber { get; }
    public string City
    {
        get => _city;
        private set
        {
            if (SetProperty(ref _city, value) && IsSelected && _stayTitle is null)
                NotifyDisplayChanged();
        }
    }
    public bool IsLocked { get; }
    public string DayLabel => $"{(IsLocked ? "🔒 " : "")}D{TripDayNumber}";
    public string DateLabel => $"{(IsLocked ? "🔒 " : "")}{Date.Day}/{Date.Month}";
    public string FullDateLabel => (IsLocked ? "🔒 " : string.Empty)
        + Date.ToString("D", LocalizationResourceManager.Instance.CurrentCulture);
    public string SelectedLabel(string? hotel) => WithPlace(DateLabel, hotel);
    public string SelectedDescription(string? hotel) => WithPlace(FullDateLabel, hotel);
    public double ItemWidth => IsSelected ? Math.Max(184, 68 * _textScale) : 68 * _textScale;
    public string DisplayLabel => IsSelected ? SelectedLabel(_stayTitle) : DateLabel;
    public string DisplayDescription => IsSelected ? SelectedDescription(_stayTitle) : FullDateLabel;
    public bool CanOpenMap => IsSelected && _canOpenMap;

    private string WithPlace(string date, string? hotel)
    {
        var place = string.IsNullOrWhiteSpace(hotel) ? City : hotel.Trim();
        return string.IsNullOrWhiteSpace(place) ? date : $"{date} · {place}";
    }

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
                OnPropertyChanged(nameof(ItemWidth));
                OnPropertyChanged(nameof(CanOpenMap));
                NotifyDisplayChanged();
            }
        }
    }

    public Color BackgroundColor => IsSelected ? InkColor : PaperColor;
    public Color BorderColor => IsSelected ? InkColor : LineColor;
    public Color PrimaryTextColor => IsSelected ? PaperColor : InkColor;
    public Color SecondaryTextColor => IsSelected ? PaperColor : MutedColor;

    public void UpdateCity(string city)
    {
        if (!string.IsNullOrWhiteSpace(city))
        {
            City = city;
        }
    }

    public void UpdateStay(string? title, bool canOpenMap = false)
    {
        var normalized = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (string.Equals(_stayTitle, normalized, StringComparison.Ordinal) && _canOpenMap == canOpenMap) return;
        _stayTitle = normalized;
        _canOpenMap = canOpenMap;
        if (IsSelected)
        {
            NotifyDisplayChanged();
            OnPropertyChanged(nameof(CanOpenMap));
        }
    }

    public void UpdateTextScale(double scale)
    {
        var normalized = double.IsFinite(scale) ? Math.Max(1, scale) : 1;
        if (_textScale == normalized) return;
        _textScale = normalized;
        OnPropertyChanged(nameof(ItemWidth));
    }

    private void NotifyDisplayChanged()
    {
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(DisplayDescription));
    }
}

internal static class ScheduleDayNavigation
{
    public static ScheduleDayFilterViewModel? ResolveChoice(
        IEnumerable<ScheduleDayFilterViewModel> days, ScheduleDayFilterViewModel? candidate) =>
        candidate is not null && days.Any(day => ReferenceEquals(day, candidate)) ? candidate : null;

    public static ScheduleDayFilterViewModel? Find(
        IReadOnlyList<ScheduleDayFilterViewModel> days, DateOnly? selectedDate, int offset = 0)
    {
        if (selectedDate is null) return null;
        for (var index = 0; index < days.Count; index++)
        {
            if (days[index].Date != selectedDate) continue;
            var target = index + offset;
            return target >= 0 && target < days.Count ? days[target] : null;
        }
        return null;
    }
}

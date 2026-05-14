using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TravelCompanion.Shared;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleTypeSectionViewModel(
    ReservationType type,
    string cacheKey,
    IEnumerable<ScheduleDayViewModel> days,
    bool isVisible = false) : ObservableObject
{
    private bool _isVisible = isVisible;

    public ReservationType Type { get; } = type;
    public string CacheKey { get; } = cacheKey;
    public ObservableCollection<ScheduleDayViewModel> Days { get; } = new(days);

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}

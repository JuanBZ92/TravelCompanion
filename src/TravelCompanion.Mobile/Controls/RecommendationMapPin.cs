using Microsoft.Maui.Controls.Maps;

namespace TravelCompanion.Mobile.Controls;

public sealed class RecommendationMapPin : Pin
{
    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(
        nameof(IsSelected),
        typeof(bool),
        typeof(RecommendationMapPin),
        false);

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }
}

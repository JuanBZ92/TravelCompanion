#if ANDROID
using Android.Gms.Maps.Model;
using Microsoft.Maui.Maps.Handlers;
using TravelCompanion.Mobile.Controls;
#endif

namespace TravelCompanion.Mobile.Services;

public static class MapPinSelectionStyling
{
    private static bool _isConfigured;

    public static void Configure()
    {
        if (_isConfigured)
        {
            return;
        }

        _isConfigured = true;

#if ANDROID
        MapPinHandler.Mapper.AppendToMapping(
            "TravelCompanionRecommendationSelection",
            (handler, pin) =>
            {
                if (pin is not RecommendationMapPin recommendationPin)
                {
                    return;
                }

                const float selectedPinGoldHue = 43f;
                var hue = recommendationPin.IsSelected
                    ? selectedPinGoldHue
                    : BitmapDescriptorFactory.HueRed;
                handler.PlatformView.SetIcon(BitmapDescriptorFactory.DefaultMarker(hue));
            });
#endif
    }
}

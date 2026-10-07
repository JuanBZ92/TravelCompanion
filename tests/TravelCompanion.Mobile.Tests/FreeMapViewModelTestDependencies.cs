global using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels
{
    // The paid-map navigation is outside these tests. All FreeMap loading,
    // cancellation and session checks execute the production ViewModel/store.
    public sealed class MapViewModel
    {
        public IAsyncRelayCommand<RecommendationDto> AddToItineraryCommand { get; } =
            new AsyncRelayCommand<RecommendationDto>(_ => Task.CompletedTask);
    }
}

namespace TravelCompanion.Mobile.Services
{
    public static class GoogleMapsLauncher
    {
        public static Task<bool> OpenAsync(RecommendationDto recommendation) => Task.FromResult(true);
    }
}

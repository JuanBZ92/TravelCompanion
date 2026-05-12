using CommunityToolkit.Mvvm.ComponentModel;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class RecommendationListItemViewModel(RecommendationDto recommendation) : ObservableObject
{
    private bool _isFavorite;
    private bool _isUnlocked = true;

    public RecommendationDto Recommendation { get; } = recommendation;
    public Guid Id => Recommendation.Id;
    public string Title => Recommendation.Title;
    public string Category => Recommendation.Category;
    public string Neighborhood => Recommendation.Neighborhood;
    public string Description => Recommendation.Description;
    public int SuggestedDurationMinutes => Recommendation.SuggestedDurationMinutes;
    public string AccessLevel => GetAccessLevelText(Recommendation.AccessLevel);
    public decimal? DistanceKm => Recommendation.DistanceKm;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetProperty(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteGlyph));
                OnPropertyChanged(nameof(FavoriteLabel));
            }
        }
    }

    public bool IsUnlocked
    {
        get => _isUnlocked;
        set
        {
            if (SetProperty(ref _isUnlocked, value))
            {
                OnPropertyChanged(nameof(AccessStatus));
            }
        }
    }

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string FavoriteLabel => IsFavorite ? "Quitar favorito" : "Guardar favorito";
    public string AccessStatus => IsUnlocked ? "Incluido" : "Bloqueado";

    private static string GetAccessLevelText(ContentAccessLevel accessLevel)
    {
        return accessLevel switch
        {
            ContentAccessLevel.Free => "Gratis",
            ContentAccessLevel.Paid => "Pago fijo",
            ContentAccessLevel.Subscription => "Suscripcion",
            ContentAccessLevel.Bundle => "Paquete",
            ContentAccessLevel.AdminOnly => "Admin",
            _ => accessLevel.ToString()
        };
    }
}

using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class RecommendationDetailViewModel(FavoritesService favoritesService) : ViewModelBase, IQueryAttributable
{
    private RecommendationDto? _recommendation;
    private bool _isFavorite;
    private bool _isUnlocked = true;

    public RecommendationDto? Recommendation
    {
        get => _recommendation;
        set
        {
            if (SetProperty(ref _recommendation, value))
            {
                IsFavorite = value is not null && favoritesService.IsFavorite(value.Id);
                OnPropertyChanged(nameof(RecommendationTags));
                OnPropertyChanged(nameof(HasRecommendationTags));
                OnPropertyChanged(nameof(CostLevelText));
            }
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetProperty(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteButtonText));
            }
        }
    }

    public string FavoriteButtonText => IsFavorite ? "Quitar de favoritos" : "Guardar favorito";
    public string AccessStatus => IsUnlocked ? "Incluido en tu acceso" : "Contenido bloqueado";
    public string AccessLevelText => Recommendation is null
        ? string.Empty
        : GetAccessLevelText(Recommendation.AccessLevel);
    public string CostLevelText => Recommendation is null
        ? string.Empty
        : FormatPriceLevel(Recommendation.PriceLevel);
    public IReadOnlyList<string> RecommendationTags => Recommendation is null
        ? []
        : Recommendation.Tags.Count > 0
            ? Recommendation.Tags
            : [Recommendation.Category.ToLowerInvariant()];
    public bool HasRecommendationTags => RecommendationTags.Count > 0;

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

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Recommendation", out var value) && value is RecommendationDto selectedRecommendation)
        {
            Recommendation = selectedRecommendation;
            OnPropertyChanged(nameof(AccessLevelText));
            OnPropertyChanged(nameof(CostLevelText));
        }

        if (query.TryGetValue("IsUnlocked", out var unlockedValue) && unlockedValue is bool unlocked)
        {
            IsUnlocked = unlocked;
        }
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (Recommendation is null)
        {
            return;
        }

        IsFavorite = favoritesService.ToggleFavorite(Recommendation.Id);
    }

    [RelayCommand]
    private async Task OpenMapsAsync()
    {
        if (Recommendation is null || !IsUnlocked)
        {
            return;
        }

        var location = new Location((double)Recommendation.Latitude, (double)Recommendation.Longitude);
        var options = new MapLaunchOptions
        {
            Name = Recommendation.Title,
            NavigationMode = NavigationMode.Walking
        };

        await Map.Default.OpenAsync(location, options);
    }

    private static string GetAccessLevelText(ContentAccessLevel accessLevel) =>
        ProductAccessModel.GetLabel(accessLevel);

    private static string FormatPriceLevel(string? priceLevel)
    {
        return priceLevel?.Trim().ToLowerInvariant() switch
        {
            "free" or "gratis" => "Gratis",
            "low" or "budget" or "cheap" or "barato" => "Bajo",
            "medium" or "moderate" or "medio" => "Medio",
            "high" or "expensive" or "premium" or "alto" => "Alto",
            _ => string.IsNullOrWhiteSpace(priceLevel) ? "Medio" : priceLevel.Trim()
        };
    }
}

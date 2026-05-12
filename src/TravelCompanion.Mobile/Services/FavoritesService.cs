namespace TravelCompanion.Mobile.Services;

public sealed class FavoritesService
{
    private const string StorageKey = "favorite_recommendation_ids";
    private readonly HashSet<Guid> _favoriteIds;

    public FavoritesService()
    {
        _favoriteIds = LoadFavorites();
    }

    public bool IsFavorite(Guid recommendationId)
    {
        return _favoriteIds.Contains(recommendationId);
    }

    public bool ToggleFavorite(Guid recommendationId)
    {
        var isFavorite = !_favoriteIds.Remove(recommendationId);
        if (isFavorite)
        {
            _favoriteIds.Add(recommendationId);
        }

        SaveFavorites();
        return isFavorite;
    }

    private static HashSet<Guid> LoadFavorites()
    {
        var rawValue = Preferences.Default.Get(StorageKey, string.Empty);
        return rawValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
    }

    private void SaveFavorites()
    {
        var rawValue = string.Join(';', _favoriteIds.OrderBy(id => id));
        Preferences.Default.Set(StorageKey, rawValue);
    }
}

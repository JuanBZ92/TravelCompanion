using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface ITravelRecommendationPlanningService
{
    Task<TravelRecommendationPlanningResult> RankAsync(
        AppUser user,
        IReadOnlyCollection<Guid> destinationIds,
        string city,
        TravelPreferenceProfile profile,
        IReadOnlyList<Reservation> reservations,
        TravelPlanningContext context,
        string responseMode,
        ISet<string> excludedRecommendationIds,
        CancellationToken cancellationToken);
}

public sealed record TravelRecommendationPlanningResult(
    int UnlockedRecommendationCount,
    int RankedCandidateCount,
    int DislikedFilteredCandidateCount,
    int ExcludedRecommendationCount,
    IReadOnlyList<ScoredRecommendation> RankedRecommendations);

public sealed class TravelRecommendationPlanningService(
    TravelCompanionDbContext dbContext,
    IRecommendationRanker ranker) : ITravelRecommendationPlanningService
{
    public async Task<TravelRecommendationPlanningResult> RankAsync(
        AppUser user,
        IReadOnlyCollection<Guid> destinationIds,
        string city,
        TravelPreferenceProfile profile,
        IReadOnlyList<Reservation> reservations,
        TravelPlanningContext context,
        string responseMode,
        ISet<string> excludedRecommendationIds,
        CancellationToken cancellationToken)
    {
        var unlockedRecommendations = await LoadUnlockedRecommendationsAsync(
            user,
            destinationIds,
            city,
            cancellationToken).ConfigureAwait(false);
        if (unlockedRecommendations.Count == 0)
        {
            return new TravelRecommendationPlanningResult(0, 0, 0, 0, []);
        }

        var rankedCandidates = ApplyResponseMode(
                ranker.Rank(profile, reservations, unlockedRecommendations, context),
                responseMode)
            .ToList();
        var rankedCandidateCount = rankedCandidates.Count;

        var dislikedFilteredCandidates = RemoveDislikedCandidates(rankedCandidates, profile.Dislikes);
        var dislikedFilteredCandidateCount = dislikedFilteredCandidates.Count;
        if (profile.Dislikes.Count > 0 || dislikedFilteredCandidates.Count > 0)
        {
            rankedCandidates = dislikedFilteredCandidates;
        }

        var excludedRecommendationCount = 0;
        if (excludedRecommendationIds.Count > 0)
        {
            var freshCandidates = rankedCandidates
                .Where(scored => !excludedRecommendationIds.Contains(scored.Recommendation.Id.ToString()))
                .ToList();
            excludedRecommendationCount = rankedCandidates.Count - freshCandidates.Count;
            if (freshCandidates.Count > 0)
            {
                rankedCandidates = freshCandidates;
            }
        }

        return new TravelRecommendationPlanningResult(
            unlockedRecommendations.Count,
            rankedCandidateCount,
            dislikedFilteredCandidateCount,
            excludedRecommendationCount,
            rankedCandidates);
    }

    private async Task<IReadOnlyList<Recommendation>> LoadUnlockedRecommendationsAsync(
        AppUser user,
        IReadOnlyCollection<Guid> destinationIds,
        string city,
        CancellationToken cancellationToken)
    {
        var recommendations = await dbContext.Recommendations
            .AsNoTracking()
            .Include(recommendation => recommendation.Packages)
            .Where(recommendation => destinationIds.Contains(recommendation.DestinationId))
            .OrderBy(recommendation => recommendation.Title)
            .ToListAsync(cancellationToken);

        var entitlements = ToEntitlementsDto(user);
        var unlocked = recommendations
            .Where(recommendation => ContentAccessPolicy.IsRecommendationUnlocked(
                entitlements,
                recommendation.AccessLevel,
                recommendation.DestinationId,
                recommendation.Packages.Select(package => package.Id).ToList()))
            .ToList();
        var cityMatches = unlocked
            .Where(recommendation =>
                recommendation.Neighborhood.Contains(city, StringComparison.OrdinalIgnoreCase)
                || recommendation.Description.Contains(city, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return cityMatches.Count > 0 ? cityMatches : unlocked;
    }

    private static UserEntitlementsDto ToEntitlementsDto(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var activeEntitlements = user.Entitlements
            .Where(entitlement => entitlement.ExpiresAt is null || entitlement.ExpiresAt > now)
            .ToList();

        return new UserEntitlementsDto(
            user.Id,
            user.Email,
            user.DisplayName,
            activeEntitlements.Select(entitlement => entitlement.AccessLevel).Distinct().ToList(),
            activeEntitlements
                .Where(entitlement => entitlement.DestinationId.HasValue)
                .Select(entitlement => entitlement.DestinationId!.Value)
                .Distinct()
                .ToList(),
            activeEntitlements
                .Where(entitlement => entitlement.TravelPackageId.HasValue)
                .Select(entitlement => entitlement.TravelPackageId!.Value)
                .Distinct()
                .ToList(),
            activeEntitlements
                .Select(entitlement => new UserEntitlementDto(
                    entitlement.Id,
                    entitlement.AccessLevel,
                    entitlement.DestinationId,
                    entitlement.TravelPackageId,
                    entitlement.GrantedAt,
                    entitlement.ExpiresAt,
                    entitlement.Source))
                .ToList());
    }

    private static IEnumerable<ScoredRecommendation> ApplyResponseMode(
        IEnumerable<ScoredRecommendation> ranked,
        string responseMode)
    {
        return responseMode switch
        {
            TravelChatResponseModes.LessWalking => ranked
                .OrderBy(scored => scored.WalkingMinutes ?? scored.Recommendation.SuggestedDurationMinutes)
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            TravelChatResponseModes.Shorter => ranked
                .OrderBy(scored => scored.Recommendation.SuggestedDurationMinutes)
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            TravelChatResponseModes.Food => SelectTopicMatches(
                ranked,
                IsFoodRecommendation),
            TravelChatResponseModes.FoodBreakfast => SelectTopicMatches(
                ranked,
                recommendation => IsFoodMealRecommendation(recommendation, FoodMeal.Breakfast)),
            TravelChatResponseModes.FoodLunch => SelectTopicMatches(
                ranked,
                recommendation => IsFoodMealRecommendation(recommendation, FoodMeal.Lunch)),
            TravelChatResponseModes.FoodDinner => SelectTopicMatches(
                ranked,
                recommendation => IsFoodMealRecommendation(recommendation, FoodMeal.Dinner)),
            TravelChatResponseModes.FoodBrunch => SelectTopicMatches(
                ranked,
                recommendation => IsFoodMealRecommendation(recommendation, FoodMeal.Brunch)),
            TravelChatResponseModes.FoodSushi => SelectTopicMatches(
                ranked,
                recommendation => IsFoodFacetRecommendation(recommendation, FoodFacet.Sushi),
                IsFoodRecommendation),
            TravelChatResponseModes.FoodRamen => SelectTopicMatches(
                ranked,
                recommendation => IsFoodFacetRecommendation(recommendation, FoodFacet.Ramen),
                IsFoodRecommendation),
            TravelChatResponseModes.FoodCafe => SelectTopicMatches(
                ranked,
                recommendation => IsFoodFacetRecommendation(recommendation, FoodFacet.Cafe),
                IsFoodRecommendation),
            TravelChatResponseModes.Culture => SelectTopicMatches(
                ranked,
                IsCultureRecommendation),
            TravelChatResponseModes.CultureMuseum => SelectTopicMatches(
                ranked,
                recommendation => IsCultureFacetRecommendation(recommendation, CultureFacet.Museum)),
            TravelChatResponseModes.CultureTemple => SelectTopicMatches(
                ranked,
                recommendation => IsCultureFacetRecommendation(recommendation, CultureFacet.Temple)),
            TravelChatResponseModes.CultureArt => SelectTopicMatches(
                ranked,
                recommendation => IsCultureFacetRecommendation(recommendation, CultureFacet.Art)),
            TravelChatResponseModes.CultureHistory => SelectTopicMatches(
                ranked,
                recommendation => IsCultureFacetRecommendation(recommendation, CultureFacet.History)),
            TravelChatResponseModes.Nature => SelectTopicMatches(
                ranked,
                IsNatureRecommendation),
            TravelChatResponseModes.NatureGarden => SelectTopicMatches(
                ranked,
                recommendation => IsNatureFacetRecommendation(recommendation, NatureFacet.Garden)),
            TravelChatResponseModes.NaturePark => SelectTopicMatches(
                ranked,
                recommendation => IsNatureFacetRecommendation(recommendation, NatureFacet.Park)),
            TravelChatResponseModes.NatureCoast => SelectTopicMatches(
                ranked,
                recommendation => IsNatureFacetRecommendation(recommendation, NatureFacet.Coast)),
            TravelChatResponseModes.NatureOnsen => SelectTopicMatches(
                ranked,
                recommendation => IsNatureFacetRecommendation(recommendation, NatureFacet.Onsen)),
            TravelChatResponseModes.Shopping => SelectTopicMatches(
                ranked,
                IsShoppingRecommendation),
            TravelChatResponseModes.ShoppingMarket => SelectTopicMatches(
                ranked,
                recommendation => IsShoppingFacetRecommendation(recommendation, ShoppingFacet.Market)),
            TravelChatResponseModes.ShoppingVintage => SelectTopicMatches(
                ranked,
                recommendation => IsShoppingFacetRecommendation(recommendation, ShoppingFacet.Vintage)),
            TravelChatResponseModes.ShoppingSouvenir => SelectTopicMatches(
                ranked,
                recommendation => IsShoppingFacetRecommendation(recommendation, ShoppingFacet.Souvenir)),
            TravelChatResponseModes.Viewpoint => SelectTopicMatches(
                ranked,
                IsViewpointRecommendation),
            TravelChatResponseModes.ViewpointSunset => SelectTopicMatches(
                ranked,
                recommendation => IsViewpointFacetRecommendation(recommendation, ViewpointFacet.Sunset)),
            TravelChatResponseModes.ViewpointPhoto => SelectTopicMatches(
                ranked,
                recommendation => IsViewpointFacetRecommendation(recommendation, ViewpointFacet.Photo)),
            TravelChatResponseModes.Nightlife => SelectTopicMatches(
                ranked,
                IsNightlifeRecommendation),
            TravelChatResponseModes.NightlifeBar => SelectTopicMatches(
                ranked,
                recommendation => IsNightlifeFacetRecommendation(recommendation, NightlifeFacet.Bar)),
            TravelChatResponseModes.NightlifeKaraoke => SelectTopicMatches(
                ranked,
                recommendation => IsNightlifeFacetRecommendation(recommendation, NightlifeFacet.Karaoke)),
            TravelChatResponseModes.NightlifeLiveMusic => SelectTopicMatches(
                ranked,
                recommendation => IsNightlifeFacetRecommendation(recommendation, NightlifeFacet.LiveMusic)),
            TravelChatResponseModes.Dance => SelectTopicMatches(
                ranked,
                IsDanceRecommendation,
                IsNightlifeRecommendation),
            TravelChatResponseModes.Neighborhood => SelectTopicMatches(
                ranked,
                IsNeighborhoodRecommendation),
            TravelChatResponseModes.Cheaper => ranked
                .OrderBy(scored => PriceRank(scored.Recommendation.PriceLevel))
                .ThenBy(scored => scored.Recommendation.AccessLevel == ContentAccessLevel.Free ? 0 : 1)
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            TravelChatResponseModes.MediumCost => ranked
                .OrderBy(scored => Math.Abs(PriceRank(scored.Recommendation.PriceLevel) - 2))
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            TravelChatResponseModes.HighCost => ranked
                .OrderByDescending(scored => PriceRank(scored.Recommendation.PriceLevel))
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            TravelChatResponseModes.WalkIn => ranked
                .OrderBy(scored => IsWalkInRecommendation(scored.Recommendation) ? 0 : 1)
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            _ => ranked
        };
    }

    private static IEnumerable<ScoredRecommendation> SelectTopicMatches(
        IEnumerable<ScoredRecommendation> ranked,
        Func<Recommendation, bool> primaryMatch,
        Func<Recommendation, bool>? fallbackMatch = null)
    {
        var rankedList = ranked.ToList();
        var primaryMatches = rankedList
            .Where(scored => primaryMatch(scored.Recommendation))
            .ToList();
        if (primaryMatches.Count > 0)
        {
            return primaryMatches;
        }

        if (fallbackMatch is not null)
        {
            var fallbackMatches = rankedList
                .Where(scored => fallbackMatch(scored.Recommendation))
                .ToList();
            if (fallbackMatches.Count > 0)
            {
                return fallbackMatches;
            }
        }

        return [];
    }

    private static int PriceRank(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "free" or "gratis" => 0,
            "low" or "budget" or "cheap" or "barato" => 1,
            "medium" or "moderate" or "medio" => 2,
            "high" or "expensive" or "premium" or "alto" => 3,
            _ => 2
        };
    }

    private static bool IsFoodRecommendation(Recommendation recommendation)
    {
        return ContainsAny(
            CreateRecommendationSearchableText(recommendation),
            "food",
            "comida",
            "snack",
            "restaurant",
            "restaurante",
            "cafe",
            "café",
            "sake");
    }

    private static bool IsFoodMealRecommendation(Recommendation recommendation, FoodMeal meal)
    {
        if (!IsFoodRecommendation(recommendation))
        {
            return false;
        }

        var text = CreateRecommendationSearchableText(recommendation);
        return meal switch
        {
            FoodMeal.Breakfast => ContainsAny(text, "breakfast", "desayuno", "desayunar", "morning coffee", "cafe", "café")
                || IsOpenDuring(recommendation.OpeningHours, new TimeOnly(8, 0), new TimeOnly(10, 0)),
            FoodMeal.Lunch => ContainsAny(text, "lunch", "almuerzo", "almorzar", "mediodia", "medio dia")
                || IsOpenDuring(recommendation.OpeningHours, new TimeOnly(12, 0), new TimeOnly(14, 30)),
            FoodMeal.Dinner => ContainsAny(text, "dinner", "cena", "cenar", "izakaya", "night food", "first night", "okonomiyaki", "omakase")
                || IsOpenDuring(recommendation.OpeningHours, new TimeOnly(19, 0), new TimeOnly(21, 0)),
            FoodMeal.Brunch => ContainsAny(text, "brunch"),
            _ => false
        };
    }

    private static bool IsFoodFacetRecommendation(Recommendation recommendation, FoodFacet facet)
    {
        if (!IsFoodRecommendation(recommendation))
        {
            return false;
        }

        var text = CreateRecommendationSearchableText(recommendation);
        return facet switch
        {
            FoodFacet.Sushi => ContainsAny(text, "sushi", "omakase", "edomae", "kaiten"),
            FoodFacet.Ramen => ContainsAny(text, "ramen"),
            FoodFacet.Cafe => ContainsAny(text, "cafe", "café", "coffee", "cafeteria", "specialty coffee"),
            _ => false
        };
    }

    private static bool IsCultureRecommendation(Recommendation recommendation)
    {
        return ContainsAny(
            CreateRecommendationSearchableText(recommendation),
            "culture",
            "cultura",
            "museum",
            "museo",
            "history",
            "historia",
            "arte");
    }

    private static bool IsCultureFacetRecommendation(Recommendation recommendation, CultureFacet facet)
    {
        if (!IsCultureRecommendation(recommendation))
        {
            return false;
        }

        var text = CreateRecommendationSearchableText(recommendation);
        return facet switch
        {
            CultureFacet.Museum => ContainsAny(text, "museum", "museo", "gallery", "galeria", "exhibition", "exposicion", "teamlab"),
            CultureFacet.Temple => ContainsAny(text, "temple", "templo", "shrine", "santuario", "jingu", "taisha", "dera", "torii"),
            CultureFacet.Art => ContainsAny(text, "art", "arte", "gallery", "galeria", "exhibition", "exposicion", "contemporary", "contemporaneo", "teamlab"),
            CultureFacet.History => ContainsAny(text, "history", "historia", "historic", "historico", "historica", "castle", "castillo", "old town", "casco antiguo"),
            _ => false
        };
    }

    private static bool IsNatureRecommendation(Recommendation recommendation)
    {
        return ContainsAny(
            CreateRecommendationSearchableText(recommendation),
            "nature",
            "naturaleza",
            "park",
            "parque",
            "garden",
            "jardin",
            "verde",
            "green",
            "coast",
            "costa",
            "river",
            "rio",
            "lake",
            "lago",
            "island",
            "isla",
            "onsen",
            "termales");
    }

    private static bool IsNatureFacetRecommendation(Recommendation recommendation, NatureFacet facet)
    {
        if (!IsNatureRecommendation(recommendation))
        {
            return false;
        }

        var text = CreateRecommendationSearchableText(recommendation);
        return facet switch
        {
            NatureFacet.Garden => ContainsAny(text, "garden", "jardin", "jardines"),
            NatureFacet.Park => ContainsAny(text, "park", "parque"),
            NatureFacet.Coast => ContainsAny(text, "coast", "costa", "coastal", "beach", "playa", "river", "rio", "riverside", "lake", "lago", "island", "isla"),
            NatureFacet.Onsen => ContainsAny(text, "onsen", "termales", "banos termales", "hot spring", "hot springs", "spa", "ryokan"),
            _ => false
        };
    }

    private static bool IsShoppingRecommendation(Recommendation recommendation)
    {
        return ContainsAny(
            CreateRecommendationSearchableText(recommendation),
            "shopping",
            "compras",
            "shop",
            "tienda",
            "tiendas",
            "market",
            "mercado",
            "vintage",
            "souvenir",
            "regalo",
            "ceramica",
            "cuchillos",
            "mall");
    }

    private static bool IsShoppingFacetRecommendation(Recommendation recommendation, ShoppingFacet facet)
    {
        if (!IsShoppingRecommendation(recommendation))
        {
            return false;
        }

        var text = CreateRecommendationSearchableText(recommendation);
        return facet switch
        {
            ShoppingFacet.Market => ContainsAny(text, "market", "mercado", "depachika"),
            ShoppingFacet.Vintage => ContainsAny(text, "vintage", "second hand", "segunda mano", "thrift", "antique", "antiguedades"),
            ShoppingFacet.Souvenir => ContainsAny(text, "souvenir", "souvenirs", "regalo", "regalos", "recuerdos", "ceramica", "cuchillos"),
            _ => false
        };
    }

    private static bool IsViewpointRecommendation(Recommendation recommendation)
    {
        return ContainsAny(
            CreateRecommendationSearchableText(recommendation),
            "viewpoint",
            "view point",
            "mirador",
            "view",
            "vista",
            "vistas",
            "observation deck",
            "observatorio",
            "sky",
            "sunset",
            "atardecer",
            "photo",
            "foto",
            "fotogenico");
    }

    private static bool IsViewpointFacetRecommendation(Recommendation recommendation, ViewpointFacet facet)
    {
        if (!IsViewpointRecommendation(recommendation))
        {
            return false;
        }

        var text = CreateRecommendationSearchableText(recommendation);
        return facet switch
        {
            ViewpointFacet.Sunset => ContainsAny(text, "sunset", "atardecer", "golden hour", "horario dorado"),
            ViewpointFacet.Photo => ContainsAny(text, "photo", "foto", "fotos", "photography", "fotografia", "fotogenico", "fotogenica"),
            _ => false
        };
    }

    private static bool IsNightlifeRecommendation(Recommendation recommendation)
    {
        var text = CreateRecommendationSearchableText(recommendation);
        if (ContainsAny(
            text,
            "nightlife",
            "bares",
            "cocktail",
            "coctel",
            "karaoke",
            "izakaya",
            "jazz",
            "live music",
            "musica en vivo",
            "music",
            "musica",
            "club",
            "boliche",
            "disco",
            "dance",
            "baile",
            "bailar",
            "neones"))
        {
            return true;
        }

        if (ContainsToken(text, "bar") || ContainsToken(text, "pub"))
        {
            return true;
        }

        return !string.Equals(recommendation.Category, "Food", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(text, "night", "noche", "nocturno");
    }

    private static bool IsDanceRecommendation(Recommendation recommendation)
    {
        return ContainsAny(
            CreateRecommendationSearchableText(recommendation),
            "dance",
            "dancing",
            "bailar",
            "baile",
            "club",
            "boliche",
            "disco",
            "fiesta")
            || ContainsToken(CreateRecommendationSearchableText(recommendation), "dj");
    }

    private static bool IsNightlifeFacetRecommendation(Recommendation recommendation, NightlifeFacet facet)
    {
        if (!IsNightlifeRecommendation(recommendation))
        {
            return false;
        }

        var text = CreateRecommendationSearchableText(recommendation);
        return facet switch
        {
            NightlifeFacet.Bar => ContainsAny(text, "bares", "cocktail", "coctel", "izakaya", "pub")
                || ContainsToken(text, "bar"),
            NightlifeFacet.Karaoke => ContainsAny(text, "karaoke"),
            NightlifeFacet.LiveMusic => ContainsAny(text, "live music", "musica en vivo", "jazz", "concert", "concierto"),
            _ => false
        };
    }

    private static bool IsNeighborhoodRecommendation(Recommendation recommendation)
    {
        return ContainsAny(
            CreateRecommendationSearchableText(recommendation),
            "barrio",
            "barrios",
            "local",
            "hidden",
            "calle",
            "calles",
            "alley",
            "alleys",
            "old town",
            "casco antiguo",
            "paseo");
    }

    private static bool IsWalkInRecommendation(Recommendation recommendation)
    {
        return ContainsAny(CreateRecommendationSearchableText(recommendation), "walk in", "walk-in");
    }

    private static List<ScoredRecommendation> RemoveDislikedCandidates(
        IReadOnlyList<ScoredRecommendation> candidates,
        IReadOnlyList<string> dislikes)
    {
        if (dislikes.Count == 0)
        {
            return candidates.ToList();
        }

        return candidates
            .Where(candidate => !dislikes.Any(dislike =>
                !string.IsNullOrWhiteSpace(dislike)
                && CreateRecommendationSearchableText(candidate.Recommendation)
                    .Contains(dislike, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static string CreateRecommendationSearchableText(Recommendation recommendation)
    {
        return NormalizeSearchableText(string.Join(
            ' ',
            [
                recommendation.Title,
                recommendation.Category,
                recommendation.Neighborhood,
                recommendation.Description,
                .. recommendation.Tags
            ]));
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsToken(string value, string token)
    {
        return value
            .Split([' ', ',', '.', ';', ':', '/', '\\', '|', '-', '_', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => string.Equals(part, token, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeSearchableText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool IsOpenDuring(string? openingHours, TimeOnly visitStart, TimeOnly visitEnd)
    {
        if (string.IsNullOrWhiteSpace(openingHours))
        {
            return false;
        }

        foreach (var segment in openingHours.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(part => part.Split('/', StringSplitOptions.RemoveEmptyEntries))
                .Where(part => part.Contains('-', StringComparison.Ordinal))
                .ToList();

            foreach (var part in parts)
            {
                var range = part.Split('-', 2, StringSplitOptions.TrimEntries);
                if (range.Length != 2
                    || !TimeOnly.TryParse(range[0], out var opensAt)
                    || !TimeOnly.TryParse(range[1], out var closesAt))
                {
                    continue;
                }

                if (closesAt < opensAt)
                {
                    if (visitStart >= opensAt || visitEnd <= closesAt)
                    {
                        return true;
                    }

                    continue;
                }

                if (opensAt <= visitStart && closesAt >= visitEnd)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private enum FoodMeal
    {
        Breakfast,
        Lunch,
        Dinner,
        Brunch
    }

    private enum FoodFacet
    {
        Sushi,
        Ramen,
        Cafe
    }

    private enum CultureFacet
    {
        Museum,
        Temple,
        Art,
        History
    }

    private enum NatureFacet
    {
        Garden,
        Park,
        Coast,
        Onsen
    }

    private enum ShoppingFacet
    {
        Market,
        Vintage,
        Souvenir
    }

    private enum ViewpointFacet
    {
        Sunset,
        Photo
    }

    private enum NightlifeFacet
    {
        Bar,
        Karaoke,
        LiveMusic
    }
}

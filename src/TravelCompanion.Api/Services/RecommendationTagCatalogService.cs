using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed partial class RecommendationTagCatalogService(TravelCompanionDbContext dbContext) : IRecommendationTagCatalogService
{
    private static readonly IReadOnlyDictionary<string, string[]> AliasMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["culture"] = ["cultura", "cultural", "museo", "museos", "templo", "templos", "santuario", "santuarios", "historia", "arte"],
            ["museum"] = ["museo", "museos"],
            ["history"] = ["historia", "historico", "historica", "castillo"],
            ["art"] = ["arte", "galeria", "exposicion"],
            ["food"] = ["comida", "gastronomia", "gastronomia", "restaurante", "restaurant", "desayuno", "almuerzo", "aluerzo", "cena", "brunch"],
            ["local food"] = ["comida local", "gastronomia local"],
            ["cafe"] = ["café", "cafeteria", "cafetería"],
            ["breakfast"] = ["desayuno", "desayunar", "brunch"],
            ["sushi"] = ["omakase", "edomae", "kaiten"],
            ["ramen"] = ["noodles", "fideos"],
            ["snacks"] = ["snack", "picoteo"],
            ["bar"] = ["bares", "tragos", "cocktail", "coctel", "cóctel"],
            ["nature"] = ["naturaleza", "verde", "jardin", "jardines", "parque", "costa", "playa", "rio", "lago", "isla"],
            ["viewpoint"] = ["mirador", "vistas", "vista", "atardecer", "fotos", "foto", "fotografia"],
            ["shopping"] = ["compras", "tiendas", "mercado", "vintage", "souvenir", "regalos", "ceramica", "cuchillos"],
            ["neighborhood"] = ["barrio", "barrios", "calle", "calles", "local"],
            ["walking"] = ["caminar", "caminata", "paseo", "walk"],
            ["romantic"] = ["pareja", "cita", "romantico", "romántico"],
            ["nightlife"] = ["nocturno", "noche", "night", "nightlife", "bar", "bares", "cocktail", "coctel", "karaoke", "musica", "jazz", "musica en vivo"],
            ["dance"] = ["bailar", "baile", "boliche", "club", "disco", "fiesta", "dj"],
            ["vegetarian"] = ["vegetariano", "vegetariana"],
            ["vegan"] = ["vegano", "vegana"],
            ["onsen"] = ["baños termales", "banos termales", "termales"],
            ["walk-in"] = ["walk in", "sin reserva", "sin reservar"],
            ["reservation recommended"] = ["reserva recomendada", "reservar recomendado", "conviene reservar"],
            ["reservation required"] = ["solo con reserva", "reserva obligatoria", "reservar obligatorio"]
        };
    private static readonly (string Tag, string DisplayName, bool IsCategory)[] BuiltInTags =
    [
        ("culture", "Culture", true),
        ("museum", "Museum", false),
        ("history", "History", false),
        ("art", "Art", false),
        ("food", "Food", true),
        ("local food", "local food", false),
        ("cafe", "cafe", false),
        ("breakfast", "breakfast", false),
        ("sushi", "sushi", false),
        ("ramen", "ramen", false),
        ("snacks", "snacks", false),
        ("bar", "bar", false),
        ("walk-in", "walk-in", false),
        ("reservation recommended", "reservation recommended", false),
        ("reservation required", "reservation required", false),
        ("premium", "premium", false),
        ("tempura", "tempura", false),
        ("soba", "soba", false),
        ("udon", "udon", false),
        ("gyoza", "gyoza", false),
        ("yakiniku", "yakiniku", false),
        ("yakitori", "yakitori", false),
        ("tonkatsu", "tonkatsu", false),
        ("unagi", "unagi", false),
        ("kaiseki", "kaiseki", false),
        ("pizza", "pizza", false),
        ("burger", "burger", false),
        ("market", "market", false),
        ("tea", "tea", false),
        ("nature", "Nature", true),
        ("viewpoint", "Viewpoint", true),
        ("shopping", "Shopping", true),
        ("neighborhood", "Neighborhood", true),
        ("walking", "walking", false),
        ("romantic", "romantic", false),
        ("nightlife", "nightlife", true),
        ("dance", "dance", false),
        ("vegetarian", "vegetarian", false),
        ("vegan", "vegan", false),
        ("onsen", "onsen", false)
    ];

    public async Task<IReadOnlyList<RecommendationTagDto>> GetCatalogAsync(
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Recommendations
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(destinationSlug))
        {
            query = query.Where(recommendation => recommendation.Destination != null
                && recommendation.Destination.Slug == destinationSlug);
        }

        var recommendations = await query
            .Select(recommendation => new
            {
                recommendation.Category,
                recommendation.Tags
            })
            .ToListAsync(cancellationToken);

        var entries = new Dictionary<string, TagAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tag, displayName, isCategory) in BuiltInTags)
        {
            AddKnown(entries, tag, displayName, isCategory);
        }

        foreach (var recommendation in recommendations)
        {
            Add(entries, recommendation.Category, isCategory: true);
            foreach (var tag in recommendation.Tags)
            {
                Add(entries, tag, isCategory: false);
            }
        }

        return entries
            .Values
            .Select(entry => new RecommendationTagDto(
                entry.Tag,
                entry.DisplayName,
                GetAliases(entry.Tag),
                entry.RecommendationCount,
                entry.IsCategory))
            .OrderBy(tag => tag.Tag)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ResolveAvoidedTagsAsync(
        string message,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(message);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var catalog = await GetCatalogAsync(destinationSlug, cancellationToken).ConfigureAwait(false);
        var matches = new List<string>();
        foreach (var tag in catalog)
        {
            var terms = new[] { tag.Tag }.Concat(tag.Aliases);
            if (terms.Any(term => ContainsAvoidedTerm(normalized, Normalize(term))))
            {
                AddUnique(matches, tag.Tag);
            }
        }

        return matches;
    }

    public async Task<RecommendationTagNormalizationResult> NormalizeTagsAsync(
        IReadOnlyList<string> tags,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        if (tags.Count == 0)
        {
            return new RecommendationTagNormalizationResult([], [], new Dictionary<string, string>());
        }

        var catalog = await GetCatalogAsync(destinationSlug, cancellationToken).ConfigureAwait(false);
        var knownTags = catalog.Select(tag => tag.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedTags = new List<string>();
        var unknownTags = new List<string>();
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawTag in tags)
        {
            var normalized = Normalize(rawTag);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var canonical = ResolveCanonicalTag(normalized);
            if (!knownTags.Contains(canonical))
            {
                AddUnique(unknownTags, canonical);
            }

            if (!string.Equals(rawTag.Trim(), canonical, StringComparison.OrdinalIgnoreCase))
            {
                replacements[rawTag.Trim()] = canonical;
            }

            AddUnique(normalizedTags, canonical);
        }

        return new RecommendationTagNormalizationResult(
            normalizedTags.OrderBy(tag => tag).ToList(),
            unknownTags.OrderBy(tag => tag).ToList(),
            replacements);
    }

    private static void Add(
        Dictionary<string, TagAccumulator> entries,
        string? value,
        bool isCategory)
    {
        var tag = ResolveCanonicalTag(Normalize(value));
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        if (entries.TryGetValue(tag, out var existing))
        {
            existing.RecommendationCount++;
            existing.IsCategory = existing.IsCategory || isCategory;
            return;
        }

        entries[tag] = new TagAccumulator(tag, FormatDisplayName(value!), isCategory);
    }

    private static void AddKnown(
        Dictionary<string, TagAccumulator> entries,
        string tag,
        string displayName,
        bool isCategory)
    {
        tag = Normalize(tag);
        if (entries.TryGetValue(tag, out var existing))
        {
            existing.IsCategory = existing.IsCategory || isCategory;
            return;
        }

        entries[tag] = new TagAccumulator(tag, displayName, isCategory)
        {
            RecommendationCount = 0
        };
    }

    private static IReadOnlyList<string> GetAliases(string tag)
    {
        var aliases = new List<string>();
        if (AliasMap.TryGetValue(tag, out var mappedAliases))
        {
            aliases.AddRange(mappedAliases.Select(Normalize));
        }

        aliases.RemoveAll(string.IsNullOrWhiteSpace);
        return aliases
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(alias => alias)
            .ToList();
    }

    private static string ResolveCanonicalTag(string normalizedTag)
    {
        if (string.IsNullOrWhiteSpace(normalizedTag))
        {
            return string.Empty;
        }

        foreach (var (tag, aliases) in AliasMap)
        {
            var canonical = Normalize(tag);
            if (string.Equals(normalizedTag, canonical, StringComparison.OrdinalIgnoreCase)
                || aliases.Any(alias => string.Equals(normalizedTag, Normalize(alias), StringComparison.OrdinalIgnoreCase)))
            {
                return canonical;
            }
        }

        return normalizedTag;
    }

    private static bool ContainsAvoidedTerm(string normalizedMessage, string normalizedTerm)
    {
        if (string.IsNullOrWhiteSpace(normalizedTerm))
        {
            return false;
        }

        return ContainsAny(
            normalizedMessage,
            $"evitar {normalizedTerm}",
            $"evita {normalizedTerm}",
            $"evite {normalizedTerm}",
            $"evitando {normalizedTerm}",
            $"avoid {normalizedTerm}",
            $"no me gusta {normalizedTerm}",
            $"no quiero {normalizedTerm}",
            $"sin {normalizedTerm}");
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (character == '#')
            {
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return RegexWhitespace().Replace(builder.ToString(), " ").Trim().ToLowerInvariant();
    }

    private static string FormatDisplayName(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed;
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+")]
    private static partial System.Text.RegularExpressions.Regex RegexWhitespace();

    private sealed class TagAccumulator(string tag, string displayName, bool isCategory)
    {
        public string Tag { get; } = tag;
        public string DisplayName { get; } = displayName;
        public int RecommendationCount { get; set; } = 1;
        public bool IsCategory { get; set; } = isCategory;
    }
}

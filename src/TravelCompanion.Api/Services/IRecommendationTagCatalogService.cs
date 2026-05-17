using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface IRecommendationTagCatalogService
{
    Task<IReadOnlyList<RecommendationTagDto>> GetCatalogAsync(
        string? destinationSlug = null,
        CancellationToken cancellationToken = default);

    Task<RecommendationTagNormalizationResult> NormalizeTagsAsync(
        IReadOnlyList<string> tags,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ResolveAvoidedTagsAsync(
        string message,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default);
}

public sealed record RecommendationTagNormalizationResult(
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> UnknownTags,
    IReadOnlyDictionary<string, string> Replacements);

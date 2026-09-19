using System.Globalization;
using System.Text;

namespace TravelCompanion.Shared;

public static class CatalogSearch
{
    public static Func<string, string, string, int> CreateFieldScorer(string query)
    {
        var normalizedScorer = CreateNormalizedFieldScorer(query);
        return (titleValue, metadataValue, descriptionValue) =>
            normalizedScorer(
                Normalize(titleValue),
                Normalize(metadataValue),
                Normalize(descriptionValue));
    }

    public static Func<string, string, string, int> CreateNormalizedFieldScorer(string query)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (normalizedTitle, normalizedMetadata, normalizedDescription) =>
        {
            if (words.Length == 0 || words.Any(word =>
                    !normalizedTitle.Contains(word, StringComparison.Ordinal)
                    && !normalizedMetadata.Contains(word, StringComparison.Ordinal)
                    && !normalizedDescription.Contains(word, StringComparison.Ordinal)))
            {
                return 0;
            }

            return words.Sum(word => normalizedTitle.Contains(word, StringComparison.Ordinal)
                ? 100
                : normalizedMetadata.Contains(word, StringComparison.Ordinal) ? 20 : 1);
        };
    }

    public static string Normalize(string value) => new string((value ?? string.Empty)
            .Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray())
        .Normalize(NormalizationForm.FormC)
        .ToLowerInvariant();
}

using System.Globalization;
using System.Text;
using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Services;

public static class CatalogSearch
{
    public static int Score(Recommendation item, string query)
    {
        return CreateScorer(query)(item);
    }

    public static Func<Recommendation, int> CreateScorer(string query)
    {
        var scoreFields = CreateFieldScorer(query);
        return item => scoreFields(
            item.Title,
            $"{item.Neighborhood} {item.Category} {item.RefinedType} {item.RefinedTypeEn} {string.Join(' ', item.Tags)}",
            $"{item.Description} {item.DescriptionEn} {item.ExtraDescription} {item.ExtraDescriptionEn}");
    }

    public static Func<string, string, string, int> CreateFieldScorer(string query)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (titleValue, metadataValue, descriptionValue) =>
        {
            var title = Normalize(titleValue);
            var metadata = Normalize(metadataValue);
            var description = Normalize(descriptionValue);
            if (words.Length == 0 || words.Any(w => !title.Contains(w) && !metadata.Contains(w) && !description.Contains(w))) return 0;
            return words.Sum(w => title.Contains(w) ? 100 : metadata.Contains(w) ? 20 : 1);
        };
    }

    private static string Normalize(string value) => new string(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
        .Normalize(NormalizationForm.FormC).ToLowerInvariant();
}

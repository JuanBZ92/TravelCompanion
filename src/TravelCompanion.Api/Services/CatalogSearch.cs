using System.Globalization;
using System.Text;
using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Services;

public static class CatalogSearch
{
    public static int Score(Recommendation item, string query)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var title = Normalize(item.Title);
        var metadata = Normalize($"{item.Neighborhood} {item.Category} {item.RefinedType} {item.RefinedTypeEn} {string.Join(' ', item.Tags)}");
        var description = Normalize($"{item.Description} {item.DescriptionEn} {item.ExtraDescription} {item.ExtraDescriptionEn}");
        if (words.Length == 0 || words.Any(w => !title.Contains(w) && !metadata.Contains(w) && !description.Contains(w))) return 0;
        return words.Sum(w => title.Contains(w) ? 100 : metadata.Contains(w) ? 20 : 1);
    }

    private static string Normalize(string value) => new string(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
        .Normalize(NormalizationForm.FormC).ToLowerInvariant();
}

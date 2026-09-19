using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Services;

public static class CatalogSearch
{
    public static int Score(Recommendation item, string query)
    {
        var score = TravelCompanion.Shared.CatalogSearch.CreateFieldScorer(query);
        return score(
            item.Title,
            $"{item.Neighborhood} {item.Category} {item.RefinedType} {item.RefinedTypeEn} {string.Join(' ', item.Tags)}",
            $"{item.Description} {item.DescriptionEn} {item.ExtraDescription} {item.ExtraDescriptionEn}");
    }

    public static Func<string, string, string, int> CreateFieldScorer(string query) =>
        TravelCompanion.Shared.CatalogSearch.CreateFieldScorer(query);
}

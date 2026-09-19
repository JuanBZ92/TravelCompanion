using TravelCompanion.Shared;

namespace TravelCompanion.Shared.Tests;

public sealed class CatalogSearchTests
{
    [Fact]
    public void Shared_ranker_normalizes_accents_and_requires_every_search_word()
    {
        var score = CatalogSearch.CreateFieldScorer("cafe kyoto");

        Assert.True(score("Café Central", "Kyoto food", "Specialty coffee") > 0);
        Assert.Equal(0, score("Café Central", "Tokyo food", "Specialty coffee"));
    }

    [Fact]
    public void Shared_ranker_prioritizes_title_over_metadata_and_description()
    {
        var score = CatalogSearch.CreateFieldScorer("temple");

        Assert.True(
            score("Temple garden", "Kyoto", "Historic site")
            > score("Garden", "Kyoto temple", "Historic site"));
        Assert.True(
            score("Garden", "Kyoto temple", "Historic site")
            > score("Garden", "Kyoto", "Historic temple site"));
    }
}

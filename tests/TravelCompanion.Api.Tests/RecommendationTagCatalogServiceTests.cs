using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class RecommendationTagCatalogServiceTests
{
    [Fact]
    public async Task GetCatalogAsync_returns_canonical_tags_with_aliases()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japan",
            Slug = "japan",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Gion walk",
            Category = "Culture",
            Neighborhood = "Gion",
            Description = "Evening history walk.",
            Tags = ["history", "onsen"],
            PriceLevel = "medium",
            Latitude = 35.0m,
            Longitude = 135.0m,
            SuggestedDurationMinutes = 60,
            AccessLevel = TravelCompanion.Shared.ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = new RecommendationTagCatalogService(dbContext);
        var catalog = await service.GetCatalogAsync("japan", CancellationToken.None);

        var culture = Assert.Single(catalog, tag => tag.Tag == "culture");
        Assert.Equal("Culture", culture.DisplayName);
        Assert.True(culture.IsCategory);
        Assert.Contains("cultura", culture.Aliases);

        var onsen = Assert.Single(catalog, tag => tag.Tag == "onsen");
        Assert.Contains("termales", onsen.Aliases);
    }

    [Theory]
    [InlineData("evitar cultura", "culture")]
    [InlineData("sin baños termales", "onsen")]
    [InlineData("evitar miradores", "viewpoint")]
    [InlineData("sin jardines", "nature")]
    [InlineData("evitar compras vintage", "shopping")]
    [InlineData("avoid history", "history")]
    public async Task ResolveAvoidedTagsAsync_maps_aliases_to_canonical_tags(
        string message,
        string expectedTag)
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japan",
            Slug = "japan",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Onsen history route",
            Category = "Culture",
            Neighborhood = "Hakone",
            Description = "Thermal bath and history route.",
            Tags = ["history", "onsen"],
            PriceLevel = "medium",
            Latitude = 35.0m,
            Longitude = 135.0m,
            SuggestedDurationMinutes = 60,
            AccessLevel = TravelCompanion.Shared.ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = new RecommendationTagCatalogService(dbContext);
        var avoidedTags = await service.ResolveAvoidedTagsAsync(message, "japan", CancellationToken.None);

        Assert.Contains(expectedTag, avoidedTags);
    }

    [Fact]
    public async Task NormalizeTagsAsync_maps_aliases_and_reports_unknown_tags()
    {
        await using var dbContext = CreateDbContext();
        var service = new RecommendationTagCatalogService(dbContext);

        var result = await service.NormalizeTagsAsync(
            ["Cultura", "baños termales", "nightlife"],
            cancellationToken: CancellationToken.None);

        Assert.Equal(["culture", "nightlife", "onsen"], result.Tags);
        Assert.Empty(result.UnknownTags);
        Assert.Equal("culture", result.Replacements["Cultura"]);
        Assert.Equal("onsen", result.Replacements["baños termales"]);
    }

    [Fact]
    public async Task GetCatalogAsync_counts_aliases_under_canonical_tags()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japan",
            Slug = "japan",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Cultura termal",
            Category = "Cultura",
            Neighborhood = "Hakone",
            Description = "Thermal route.",
            Tags = ["baños termales"],
            PriceLevel = "medium",
            Latitude = 35.0m,
            Longitude = 135.0m,
            SuggestedDurationMinutes = 60,
            AccessLevel = TravelCompanion.Shared.ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = new RecommendationTagCatalogService(dbContext);
        var catalog = await service.GetCatalogAsync("japan", CancellationToken.None);

        Assert.DoesNotContain(catalog, tag => tag.Tag == "cultura");
        Assert.DoesNotContain(catalog, tag => tag.Tag == "banos termales");
        Assert.Equal(1, Assert.Single(catalog, tag => tag.Tag == "culture").RecommendationCount);
        Assert.Equal(1, Assert.Single(catalog, tag => tag.Tag == "onsen").RecommendationCount);
    }

    private static TravelCompanionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TravelCompanionDbContext(options);
    }
}

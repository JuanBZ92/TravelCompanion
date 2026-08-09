using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Controllers;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class RecommendationsControllerTests
{
    [Fact]
    public async Task GetRecommendations_public_catalog_excludes_paid_subscription_and_packaged_content()
    {
        await using var dbContext = CreateDbContext();
        var destination = new Destination
        {
            Id = Guid.NewGuid(),
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        };
        var package = new TravelPackage
        {
            Id = Guid.NewGuid(),
            DestinationId = destination.Id,
            Name = "Premium",
            Slug = "premium",
            Description = "Premium content",
            Price = 19,
            Currency = "USD"
        };

        dbContext.Destinations.Add(destination);
        dbContext.TravelPackages.Add(package);
        dbContext.Recommendations.Add(CreateRecommendation(
            destination.Id,
            "Free walk",
            ContentAccessLevel.Free));
        dbContext.Recommendations.Add(CreateRecommendation(
            destination.Id,
            "Paid route",
            ContentAccessLevel.Paid,
            package));
        dbContext.Recommendations.Add(CreateRecommendation(
            destination.Id,
            "Subscription route",
            ContentAccessLevel.Subscription));

        await dbContext.SaveChangesAsync();

        var controller = new RecommendationsController(
            dbContext,
            new RecommendationTagCatalogService(dbContext))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.GetRecommendations(
            destinationSlug: "japon",
            cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<PagedResultDto<RecommendationDto>>(ok.Value);
        var item = Assert.Single(page.Items);
        Assert.Equal("Free walk", item.Title);
        Assert.Equal(1, page.TotalItems);
    }

    private static TravelCompanionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase($"recommendations-controller-{Guid.NewGuid():N}")
            .Options;

        return new TravelCompanionDbContext(options);
    }

    private static Recommendation CreateRecommendation(
        Guid destinationId,
        string title,
        ContentAccessLevel accessLevel,
        params TravelPackage[] packages)
    {
        return new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = title,
            Category = "Culture",
            Neighborhood = "Tokyo",
            Description = $"{title} description",
            Tags = ["culture"],
            PriceLevel = "medium",
            Latitude = 35.0m,
            Longitude = 139.0m,
            SuggestedDurationMinutes = 90,
            Rating = 4.5,
            OpeningHours = "09:00-18:00",
            AccessLevel = accessLevel,
            Packages = packages.ToList()
        };
    }
}

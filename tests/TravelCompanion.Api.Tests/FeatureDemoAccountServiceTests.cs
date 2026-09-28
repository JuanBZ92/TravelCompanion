using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class FeatureDemoAccountServiceTests
{
    [Fact]
    public async Task EditableDemoHasNoSeededPersonalNotesWhileCuratedCopyKeepsDescriptions()
    {
        await using var db = new TravelCompanionDbContext(new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var destination = new Destination { Id = Guid.NewGuid(), Slug = "japon", Name = "Japón",
            Country = "Japan", HeroImageUrl = "", ShortDescription = "" };
        db.Destinations.Add(destination);
        foreach (var city in PremiumDemoTripFactory.CitySlugs)
            for (var i = 0; i < 3; i++)
                db.Recommendations.Add(new Recommendation { Id = Guid.NewGuid(), DestinationId = destination.Id,
                    Title = $"{city}-{i}", Category = "Culture", Neighborhood = city, CitySlug = city,
                    Description = "Descripción curada" });
        await db.SaveChangesAsync();
        await new FeatureDemoAccountService(db).CreateAsync();
        var builder = await db.Trips.Include(x => x.Reservations).SingleAsync(x => x.ExternalId == "feature-demo-builder-v1");
        var curated = await db.Trips.Include(x => x.Reservations).SingleAsync(x => x.ExternalId == "feature-demo-curated-v1");
        Assert.NotEmpty(builder.Reservations);
        Assert.All(builder.Reservations, x => Assert.Equal("", x.Notes));
        Assert.Contains(curated.Reservations, x => x.Notes == "Descripción curada");
        Assert.Empty(await db.JournalNotes.ToListAsync());
    }
}

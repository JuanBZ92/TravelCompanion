using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Tests;

public sealed class YukuJapanRecommendationImportServiceTests
{
    [Fact]
    public async Task Preview_maps_yuku_workbook_rows_to_recommendation_signals()
    {
        await using var dbContext = CreateDbContext();
        await SeedJapanDestinationAsync(dbContext);
        var service = CreateService(dbContext);
        await using var workbook = CreateWorkbook(
            CreateRow(
                city: "Tokyo",
                place: "Sushi Gin",
                comment: "Sushi edomae recomendado para cena especial.",
                foodType: "Sushi / Edomae",
                reservation: "Solo con reserva",
                price: "Cena ~¥15,000-25,000"),
            CreateRow(
                city: "Tokyo",
                place: "A10",
                comment: "Bar HIFI con buen ambiente y tragos.",
                foodType: "Bar",
                reservation: "Walk-in",
                price: "~¥2,000-5,000"),
            CreateRow(
                city: "Kyoto",
                place: "Coffee Morning",
                comment: "Cafe specialty para desayuno tranquilo.",
                foodType: "Cafe / Desayuno",
                reservation: string.Empty,
                price: "~¥1,200-2,800"));

        var result = await service.PreviewAsync(workbook);

        Assert.False(result.HasErrors);
        Assert.Equal(3, result.TotalRows);
        Assert.Equal(1, result.WarningCount);

        var sushi = Assert.Single(result.Rows, row => row.Title == "Sushi Gin");
        Assert.Equal("Food", sushi.Category);
        Assert.Equal("high", sushi.PriceLevel);
        Assert.Equal(120, sushi.SuggestedDurationMinutes);
        Assert.Contains("sushi", sushi.Tags);
        Assert.Contains("reservation required", sushi.Tags);
        Assert.Contains("premium", sushi.Tags);

        var bar = Assert.Single(result.Rows, row => row.Title == "A10");
        Assert.Equal("Nightlife", bar.Category);
        Assert.Contains("nightlife", bar.Tags);
        Assert.Contains("bar", bar.Tags);
        Assert.Contains("walk-in", bar.Tags);

        var cafe = Assert.Single(result.Rows, row => row.Title == "Coffee Morning");
        Assert.Equal(45, cafe.SuggestedDurationMinutes);
        Assert.Contains("cafe", cafe.Tags);
        Assert.Contains("breakfast", cafe.Tags);
    }

    [Fact]
    public async Task Import_upserts_by_external_id_sets_free_and_clears_packages()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = await SeedJapanDestinationAsync(dbContext);
        var package = new TravelPackage
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Name = "Old Package",
            Slug = "old-package",
            Description = "Old",
            Price = 1,
            Currency = "USD"
        };
        dbContext.TravelPackages.Add(package);
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            ExternalId = "yuku-japan-tokyo-ramen-test",
            Title = "Old title",
            Category = "Food",
            Neighborhood = "Tokyo, Japan",
            Description = "Old",
            PriceLevel = "high",
            Latitude = 35,
            Longitude = 139,
            SuggestedDurationMinutes = 90,
            AccessLevel = ContentAccessLevel.Paid,
            Packages = [package]
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);
        await using var workbook = CreateWorkbook(CreateRow(
            city: "Tokyo",
            place: "Ramen Test",
            comment: "Ramen simple para almuerzo rapido.",
            foodType: "Ramen",
            reservation: "Walk-in",
            price: "~¥1,000-2,000"));

        var result = await service.ImportAsync(workbook);

        Assert.True(result.Imported);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);

        var recommendation = await dbContext.Recommendations
            .Include(existing => existing.Packages)
            .SingleAsync(existing => existing.ExternalId == "yuku-japan-tokyo-ramen-test");
        Assert.Equal("Ramen Test", recommendation.Title);
        Assert.Equal(ContentAccessLevel.Free, recommendation.AccessLevel);
        Assert.Empty(recommendation.Packages);
        Assert.Equal("low", recommendation.PriceLevel);
        Assert.Equal(60, recommendation.SuggestedDurationMinutes);
        Assert.Contains("ramen", recommendation.Tags);
        Assert.Contains("walk-in", recommendation.Tags);
        Assert.Equal("YUKU Japan verificada v1", recommendation.SourceName);
    }

    private static YukuJapanRecommendationImportService CreateService(TravelCompanionDbContext dbContext)
    {
        return new YukuJapanRecommendationImportService(
            dbContext,
            NullLogger<YukuJapanRecommendationImportService>.Instance);
    }

    private static TravelCompanionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TravelCompanionDbContext(options);
    }

    private static async Task<Guid> SeedJapanDestinationAsync(TravelCompanionDbContext dbContext)
    {
        var destinationId = Guid.NewGuid();
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = "https://example.com/japan.jpg",
            ShortDescription = "Japan"
        });
        await dbContext.SaveChangesAsync();
        return destinationId;
    }

    private static MemoryStream CreateWorkbook(params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Master Pendientes Japon");
        var headers = new[]
        {
            "Ciudad",
            "Lugar",
            "Comentario",
            "Tipo de comida",
            "Google Maps Link",
            "Coordenadas formula",
            "Coordenadas Value",
            "Tabelog Score (Numerico - Ordenar)",
            "Reserva",
            "Precio Aprox"
        };

        for (var index = 0; index < headers.Length; index++)
        {
            worksheet.Cell(1, index + 1).Value = headers[index];
        }

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Length; columnIndex++)
            {
                worksheet.Cell(rowIndex + 2, columnIndex + 1).Value = XLCellValue.FromObject(row[columnIndex]);
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static object?[] CreateRow(
        string city,
        string place,
        string comment,
        string foodType,
        string reservation,
        string price)
    {
        return
        [
            city,
            place,
            comment,
            foodType,
            "https://maps.example/place",
            null,
            "35.650000, 139.700000",
            3.55,
            reservation,
            price
        ];
    }
}

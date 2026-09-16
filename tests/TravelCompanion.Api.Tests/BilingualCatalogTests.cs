using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class BilingualCatalogTests
{
    [Fact]
    public async Task Verified_workbook_updates_by_place_id_and_preserves_city_configuration()
    {
        await using var db = await Database();
        var service = new YukuJapanRecommendationImportService(db, NullLogger<YukuJapanRecommendationImportService>.Instance);
        using var first = Workbook("Museum", "Museo", "ChIJtest");
        Assert.True((await service.ImportAsync(first)).Imported);
        var item = await db.Recommendations.SingleAsync();
        var id = item.Id;
        Assert.Equal("Culture", item.Category);
        Assert.DoesNotContain("food", item.Tags);
        Assert.Equal("English donuts description", RecommendationPresentation.ToDto(item, locale: "en-US").Description);
        Assert.Equal("Extra ES", RecommendationPresentation.ToDto(item, locale: "en-US").ExtraDescription);
        Assert.Equal("ChIJtest", RecommendationPresentation.ToDto(item).ProviderPlaceId);
        Assert.False(item.IsPriceKnown);
        Assert.True(CatalogSearch.Score(item, "DONUTS") > 0);
        var city = await db.FreeMapCities.SingleAsync();
        city.CenterLatitude = 34;
        city.IsEnabled = false;
        city.ContactUrl = "https://example.com/contact";
        await db.SaveChangesAsync();
        using var renamed = Workbook("Renamed museum", "Museo", "ChIJtest");
        var result = await service.ImportAsync(renamed);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(id, (await db.Recommendations.SingleAsync()).Id);
        Assert.Equal("Renamed museum", item.Title);
        Assert.Equal(34, city.CenterLatitude);
        Assert.False(city.IsEnabled);
        Assert.Equal("https://example.com/contact", city.ContactUrl);
    }

    [Theory]
    [InlineData("Bar / Whisky japonés", "Nightlife")]
    [InlineData("Club / Música electrónica", "Nightlife")]
    [InlineData("Jardín", "Nature")]
    [InlineData("Artesanía / Compras", "Shopping")]
    [InlineData("Café / Artesanía", "Food")]
    [InlineData("Sushi / Edomae", "Food")]
    public async Task Refined_types_are_not_all_food(string type, string category)
    {
        await using var db = await Database();
        using var workbook = Workbook("Place", type, "ChIJtype");
        var service = new YukuJapanRecommendationImportService(db, NullLogger<YukuJapanRecommendationImportService>.Instance);
        var preview = await service.PreviewAsync(workbook);
        Assert.False(preview.HasErrors);
        Assert.Equal(category, Assert.Single(preview.Rows).Category);
    }

    [Fact]
    public async Task Invalid_coordinates_do_not_change_catalog()
    {
        await using var db = await Database();
        using var workbook = Workbook("Bad", "Café", "ChIJbad", "200,139");
        var service = new YukuJapanRecommendationImportService(db, NullLogger<YukuJapanRecommendationImportService>.Instance);
        Assert.False((await service.ImportAsync(workbook)).Imported);
        Assert.Empty(db.Recommendations);
        Assert.Empty(db.FreeMapCities);
    }

    [Fact]
    public async Task Local_verified_excel_can_be_imported_twice_without_duplicates()
    {
        // Optional local acceptance run; the fixture above remains portable in CI.
        var path = Environment.GetEnvironmentVariable("YUKU_ACCEPTANCE_WORKBOOK");
        if (string.IsNullOrWhiteSpace(path)) return;
        await using var db = await Database();
        var service = new YukuJapanRecommendationImportService(db, NullLogger<YukuJapanRecommendationImportService>.Instance);
        using var first = File.OpenRead(path);
        var result = await service.ImportAsync(first);
        Assert.True(result.Imported, string.Join("; ", result.Errors));
        Assert.Equal(311, result.CreatedCount);
        Assert.Equal(8, await db.FreeMapCities.CountAsync());
        Assert.Equal(311, await db.Recommendations.Select(r => r.ProviderPlaceId).Distinct().CountAsync());
        using var second = File.OpenRead(path);
        result = await service.ImportAsync(second);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(311, result.UpdatedCount);
        Assert.Equal(311, await db.Recommendations.CountAsync());
    }

    private static async Task<TravelCompanionDbContext> Database()
    {
        var db = new TravelCompanionDbContext(new DbContextOptionsBuilder<TravelCompanionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Destinations.Add(new Destination { Id = Guid.NewGuid(), Slug = "japon", Name = "Japan", Country = "Japan", HeroImageUrl = "", ShortDescription = "Japan" });
        await db.SaveChangesAsync();
        return db;
    }

    private static MemoryStream Workbook(string title, string type, string placeId, string coordinates = "35.68,139.76")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Catalog");
        string[] headers = ["Ciudad", "Lugar (verificado)", "Comentario", "Comentario Extra", "Tipo de comida", "Google Maps Link (verificado)", "Coordenadas verificadas (pin)", "Place ID (Google)", "Tabelog Score (Numérico - Ordenar)", "Reserva (verificada)", "Confianza (1-5)", "Fuente reserva", "Precio Aprox", "Verificación", "Comentario EN", "Comentario Extra EN", "Reserva EN", "Tipo EN"];
        string[] values = ["Nara", title, "Descripcion ES", "Extra ES", type, $"https://www.google.com/maps/place/?q=place_id:{placeId}", coordinates, placeId, "3.5", "Walk-in", "5", "Editorial", "", "Verified", "English donuts description", "", "Walk-in", "Museum"];
        for (var column = 0; column < headers.Length; column++) { sheet.Cell(1, column + 1).Value = headers[column]; sheet.Cell(2, column + 1).Value = values[column]; }
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}

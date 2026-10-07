using System.Text.Json;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class MapContentComparisonTests
{
    private static readonly RecommendationDto Place = new(Guid.NewGuid(), Guid.NewGuid(), "Café", "Food", "Tokyo",
        "A quiet place", ["coffee", "garden"], "low", 35, 139, 45, 4.5, "09:00–18:00",
        ContentAccessLevel.Free, [Guid.NewGuid()], 2.5m) { ExtraDescription = "Bring a book", SourceUrl = "https://example.test" };

    [Fact]
    public void Deserialized_catalog_does_not_cause_refresh_but_access_and_editorial_changes_do()
    {
        var recovered = JsonSerializer.Deserialize<RecommendationDto[]>(JsonSerializer.Serialize(new[] { Place }))!;
        Assert.True(MapContentComparison.SameCatalog([Place], recovered));
        Assert.False(MapContentComparison.SameCatalog([Place], [recovered[0] with { PackageIds = [] }]));
        Assert.False(MapContentComparison.SameCatalog([Place], [recovered[0] with { AccessLevel = ContentAccessLevel.Paid }]));
        Assert.False(MapContentComparison.SameCatalog([Place], [recovered[0] with { ExtraDescription = "Changed" }]));
        Assert.False(MapContentComparison.SameCatalog([Place], [recovered[0] with { Latitude = 36 }]));
        Assert.False(MapContentComparison.SameCatalog([Place], []));
    }

    [Fact]
    public void Search_index_reuses_text_when_only_non_search_details_change()
    {
        Assert.True(MapContentComparison.SameSearchInputs(Place, Place with { Rating = 4.8, DistanceKm = 1, ExtraDescription = "New details" }));
        Assert.False(MapContentComparison.SameSearchInputs(Place, Place with { Title = "Another café" }));
        Assert.False(MapContentComparison.SameSearchInputs(Place, Place with { Tags = ["coffee", "family"] }));
    }

    [Fact]
    public void Free_preview_ignores_fetch_timestamp_but_keeps_order_access_location_and_city_changes()
    {
        var first = new FreeMapPreviewDto(DateTimeOffset.UtcNow, new("tokyo", "Tokyo", 35, 139, 3, 0), null, 1, 1,
            [new("open", 35, 139, FreeMapMarkerAccess.Unlocked, Place), new("locked", 36, 139, FreeMapMarkerAccess.Locked, null)]);
        var recovered = JsonSerializer.Deserialize<FreeMapPreviewDto>(JsonSerializer.Serialize(first))!;
        Assert.True(MapContentComparison.SamePreview(first, recovered with { GeneratedAtUtc = first.GeneratedAtUtc.AddMinutes(5) }));
        Assert.False(MapContentComparison.SamePreview(first, recovered with { Markers = recovered.Markers.Reverse().ToArray() }));
        Assert.False(MapContentComparison.SamePreview(first, recovered with { Markers = [recovered.Markers[0] with { Access = FreeMapMarkerAccess.Locked }, recovered.Markers[1]] }));
        Assert.False(MapContentComparison.SamePreview(first, recovered with { Markers = [recovered.Markers[0] with { Latitude = 34 }, recovered.Markers[1]] }));
        Assert.False(MapContentComparison.SamePreview(first, recovered with { City = recovered.City with { FreeRadiusKm = 4 } }));
        Assert.False(MapContentComparison.SamePreview(first, recovered with { ContactUrl = "https://example.test/contact" }));
    }
}

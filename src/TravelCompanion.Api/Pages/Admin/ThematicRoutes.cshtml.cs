using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class ThematicRoutesModel(TravelCompanionDbContext dbContext) : PageModel
{
    [BindProperty] public RouteInput Input { get; set; } = new();
    public IReadOnlyList<ThematicRoute> Routes { get; private set; } = [];
    public IReadOnlyList<Destination> Destinations { get; private set; } = [];
    public IReadOnlyList<Recommendation> Recommendations { get; private set; } = [];

    public async Task OnGetAsync(Guid? destinationId, Guid? routeId)
    {
        await LoadAsync(destinationId);
        if (!routeId.HasValue) return;
        var route = await dbContext.ThematicRoutes.AsNoTracking().Include(item => item.Stops)
            .SingleOrDefaultAsync(item => item.Id == routeId && item.Origin == RouteOrigin.Yuku);
        if (route is null) return;
        Input.Id = route.Id; Input.DestinationId = route.DestinationId; Input.Name = route.Name;
        Input.City = route.City; Input.Theme = route.Theme; Input.Status = route.Status;
        Input.AccessLevel = route.AccessLevel;
        var stops = route.Stops.ToDictionary(item => item.RecommendationId);
        Input.Stops = Recommendations.Select((item, index) => new StopInput
        {
            RecommendationId = item.Id, Title = item.Title, Selected = stops.ContainsKey(item.Id),
            DurationMinutes = stops.TryGetValue(item.Id, out var stop) ? stop.DurationMinutes : Math.Max(15, item.SuggestedDurationMinutes),
            SortOrder = stops.TryGetValue(item.Id, out stop) ? stop.SortOrder : index + route.Stops.Count
        }).ToList();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var selected = Input.Stops.Where(item => item.Selected).OrderBy(item => item.SortOrder).ToList();
        if (Input.Status == RoutePublicationStatus.Published && selected.Count < 2)
            ModelState.AddModelError(string.Empty, "Una ruta publicada necesita al menos dos paradas.");
        var recommendations = await dbContext.Recommendations.Where(item => selected.Select(stop => stop.RecommendationId).Contains(item.Id)).ToListAsync();
        if (recommendations.Count != selected.Count || recommendations.Any(item => item.DestinationId != Input.DestinationId))
            ModelState.AddModelError(string.Empty, "Todas las paradas deben existir en el destino elegido.");
        var city = Normalize(Input.City);
        if (Input.Status == RoutePublicationStatus.Published && recommendations.Any(item =>
                !string.Equals(Normalize(item.CitySlug), city, StringComparison.Ordinal)
                && !Normalize(item.Neighborhood).Contains(city, StringComparison.Ordinal)))
            ModelState.AddModelError(string.Empty, "Todas las paradas publicadas deben corresponder a la ciudad de la ruta.");
        if (!ModelState.IsValid) { await LoadAsync(Input.DestinationId); return Page(); }
        var now = DateTimeOffset.UtcNow;
        var route = Input.Id.HasValue
            ? await dbContext.ThematicRoutes.Include(item => item.Stops).SingleOrDefaultAsync(item => item.Id == Input.Id && item.Origin == RouteOrigin.Yuku)
                ?? throw new InvalidOperationException("La plantilla ya no existe.")
            : new ThematicRoute
            {
                Id = Guid.NewGuid(), Name = string.Empty, City = string.Empty, Origin = RouteOrigin.Yuku,
                Version = 0, CreatedAtUtc = now, WarningsJson = "[]"
            };
        if (!Input.Id.HasValue) dbContext.ThematicRoutes.Add(route);
        else dbContext.ThematicRouteStops.RemoveRange(route.Stops);
        route.DestinationId = Input.DestinationId; route.Name = Input.Name.Trim(); route.Theme = Input.Theme;
        route.City = Input.City.Trim(); route.Status = Input.Status; route.Version++; route.UpdatedAtUtc = now;
        route.AccessLevel = Input.AccessLevel;
        route.VisitMinutes = selected.Sum(item => item.DurationMinutes); route.EstimatedTransferMinutes = Math.Max(0, selected.Count - 1) * 20;
        route.WarningsJson = JsonSerializer.Serialize(new[] { "Los traslados son estimaciones; revisa horarios y disponibilidad." });
        route.Stops = selected.Select((item, index) => new ThematicRouteStop
        {
            Id = Guid.NewGuid(), ThematicRouteId = route.Id, RecommendationId = item.RecommendationId,
            DurationMinutes = Math.Clamp(item.DurationMinutes, 15, 480), SortOrder = index,
            EstimatedTransferMinutes = index == 0 ? null : 20
        }).ToList();
        await dbContext.SaveChangesAsync();
        return RedirectToPage(new { destinationId = Input.DestinationId });
    }

    public async Task<IActionResult> OnPostArchiveAsync(Guid id)
    {
        var route = await dbContext.ThematicRoutes.SingleOrDefaultAsync(item => item.Id == id && item.Origin == RouteOrigin.Yuku);
        if (route is null) return NotFound();
        route.Status = RoutePublicationStatus.Archived; route.Version++; route.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        return RedirectToPage(new { destinationId = route.DestinationId });
    }

    private async Task LoadAsync(Guid? destinationId)
    {
        Destinations = await dbContext.Destinations.AsNoTracking().OrderBy(item => item.Name).ToListAsync();
        var selectedDestination = destinationId ?? Input.DestinationId;
        if (selectedDestination == Guid.Empty) selectedDestination = Destinations.FirstOrDefault()?.Id ?? Guid.Empty;
        Input.DestinationId = selectedDestination;
        Recommendations = await dbContext.Recommendations.AsNoTracking().Where(item => item.DestinationId == selectedDestination)
            .OrderBy(item => item.Title).Take(300).ToListAsync();
        Routes = await dbContext.ThematicRoutes.AsNoTracking().Include(item => item.Stops)
            .Where(item => item.Origin == RouteOrigin.Yuku && item.DestinationId == selectedDestination)
            .OrderBy(item => item.Status).ThenBy(item => item.Name).ToListAsync();
        if (Input.Stops.Count == 0) Input.Stops = Recommendations.Select((item, index) => new StopInput
        { RecommendationId = item.Id, Title = item.Title, DurationMinutes = Math.Max(15, item.SuggestedDurationMinutes), SortOrder = index }).ToList();
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
            != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).Replace(" ", "-");
    }

    public sealed class RouteInput
    {
        public Guid? Id { get; set; }
        public Guid DestinationId { get; set; }
        [Required, MaxLength(140)] public string Name { get; set; } = string.Empty;
        [Required, MaxLength(120)] public string City { get; set; } = string.Empty;
        public RouteTheme Theme { get; set; }
        public RoutePublicationStatus Status { get; set; } = RoutePublicationStatus.Draft;
        public RouteAccessLevel AccessLevel { get; set; } = RouteAccessLevel.Premium;
        public List<StopInput> Stops { get; set; } = [];
    }
    public sealed class StopInput
    {
        public Guid RecommendationId { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool Selected { get; set; }
        [Range(15, 480)] public int DurationMinutes { get; set; } = 60;
        public int SortOrder { get; set; }
    }
}

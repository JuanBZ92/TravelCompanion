using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class DestinationsModel(TravelCompanionDbContext dbContext) : PageModel
{
    public List<DestinationRow> Destinations { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public DestinationInput Input { get; set; } = new();

    public async Task OnGetAsync(Guid? editId)
    {
        await LoadPageDataAsync();

        if (!editId.HasValue)
        {
            return;
        }

        var destination = await dbContext.Destinations.FindAsync(editId.Value);
        if (destination is not null)
        {
            Input = DestinationInput.FromEntity(destination);
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var normalizedSlug = NormalizeSlug(Input.Slug);
        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Name)}", "El nombre es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Slug)}", "El slug es obligatorio.");
        }

        var duplicateExists = await dbContext.Destinations.AnyAsync(destination =>
            destination.Slug == normalizedSlug && destination.Id != Input.Id);
        if (duplicateExists)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Slug)}", "Ya existe un destino con ese slug.");
        }

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        Destination destination;
        if (Input.Id.HasValue)
        {
            destination = await dbContext.Destinations.FindAsync(Input.Id.Value)
                ?? throw new InvalidOperationException("Destination not found.");
        }
        else
        {
            destination = new Destination
            {
                Id = Guid.NewGuid(),
                Name = string.Empty,
                Slug = string.Empty,
                Country = string.Empty,
                HeroImageUrl = string.Empty,
                ShortDescription = string.Empty
            };
            dbContext.Destinations.Add(destination);
        }

        Input.ApplyTo(destination, normalizedSlug);
        await dbContext.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var destination = await dbContext.Destinations
            .Include(existingDestination => existingDestination.Packages)
            .Include(existingDestination => existingDestination.Recommendations)
            .FirstOrDefaultAsync(existingDestination => existingDestination.Id == id);

        if (destination is null)
        {
            return RedirectToPage();
        }

        var tripCount = await dbContext.Trips.CountAsync(trip => trip.DestinationId == id);
        if (destination.Packages.Count > 0 || destination.Recommendations.Count > 0 || tripCount > 0)
        {
            StatusMessage = "No se puede borrar un destino con paquetes, recomendaciones o viajes asociados.";
            return RedirectToPage();
        }

        dbContext.Destinations.Remove(destination);
        await dbContext.SaveChangesAsync();
        return RedirectToPage();
    }

    private async Task LoadPageDataAsync()
    {
        Destinations = await dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .Select(destination => new DestinationRow(
                destination.Id,
                destination.Name,
                destination.Slug,
                destination.Country,
                destination.Packages.Count,
                destination.Recommendations.Count,
                dbContext.Trips.Count(trip => trip.DestinationId == destination.Id)))
            .ToListAsync();
    }

    private static string NormalizeSlug(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '-');
    }

    public sealed record DestinationRow(
        Guid Id,
        string Name,
        string Slug,
        string Country,
        int PackageCount,
        int RecommendationCount,
        int TripCount);

    public sealed class DestinationInput
    {
        public Guid? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? HeroImageUrl { get; set; }
        public string? ShortDescription { get; set; }

        public static DestinationInput FromEntity(Destination destination)
        {
            return new DestinationInput
            {
                Id = destination.Id,
                Name = destination.Name,
                Slug = destination.Slug,
                Country = destination.Country,
                HeroImageUrl = destination.HeroImageUrl,
                ShortDescription = destination.ShortDescription
            };
        }

        public void ApplyTo(Destination destination, string normalizedSlug)
        {
            destination.Name = (Name ?? string.Empty).Trim();
            destination.Slug = normalizedSlug;
            destination.Country = (Country ?? string.Empty).Trim();
            destination.HeroImageUrl = (HeroImageUrl ?? string.Empty).Trim();
            destination.ShortDescription = (ShortDescription ?? string.Empty).Trim();
        }
    }
}

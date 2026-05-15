using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class RecommendationsModel(TravelCompanionDbContext dbContext) : PageModel
{
    public List<RecommendationRow> Recommendations { get; private set; } = [];
    public List<SelectListItem> DestinationOptions { get; private set; } = [];
    public List<SelectListItem> AccessLevelOptions { get; } = ProductAccessModel.ContentAccessOptions
        .Select(definition => new SelectListItem(definition.Label, definition.Level.ToString()))
        .ToList();

    [BindProperty]
    public RecommendationInput Input { get; set; } = new();

    public async Task OnGetAsync(Guid? editId)
    {
        await LoadPageDataAsync();

        if (editId.HasValue)
        {
            var recommendation = await dbContext.Recommendations.FindAsync(editId.Value);
            if (recommendation is not null)
            {
                Input = RecommendationInput.FromEntity(recommendation);
            }
        }
        else if (DestinationOptions.Count > 0)
        {
            Input.DestinationId = Guid.Parse(DestinationOptions[0].Value);
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (Input.DestinationId == Guid.Empty)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.DestinationId)}", "Selecciona un destino.");
        }

        if (string.IsNullOrWhiteSpace(Input.Title))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Title)}", "El titulo es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(Input.Category))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Category)}", "La categoria es obligatoria.");
        }

        if (string.IsNullOrWhiteSpace(Input.Description))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Description)}", "La descripcion es obligatoria.");
        }

        if (Input.SuggestedDurationMinutes < 1)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.SuggestedDurationMinutes)}", "La duracion debe ser mayor a cero.");
        }

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        Recommendation recommendation;
        if (Input.Id.HasValue)
        {
            recommendation = await dbContext.Recommendations.FindAsync(Input.Id.Value)
                ?? throw new InvalidOperationException("Recommendation not found.");
        }
        else
        {
            recommendation = new Recommendation
            {
                Id = Guid.NewGuid(),
                DestinationId = Input.DestinationId,
                Title = string.Empty,
                Category = string.Empty,
                Neighborhood = string.Empty,
                Description = string.Empty
            };
            dbContext.Recommendations.Add(recommendation);
        }

        Input.ApplyTo(recommendation);
        await dbContext.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var recommendation = await dbContext.Recommendations.FindAsync(id);
        if (recommendation is not null)
        {
            dbContext.Recommendations.Remove(recommendation);
            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    private async Task LoadPageDataAsync()
    {
        DestinationOptions = await dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .Select(destination => new SelectListItem(destination.Name, destination.Id.ToString()))
            .ToListAsync();

        Recommendations = await dbContext.Recommendations
            .AsNoTracking()
            .Include(recommendation => recommendation.Destination)
            .OrderBy(recommendation => recommendation.Title)
            .Select(recommendation => new RecommendationRow(
                recommendation.Id,
                recommendation.Destination != null ? recommendation.Destination.Name : "Unknown",
                recommendation.Title,
                recommendation.Category,
                recommendation.AccessLevel,
                recommendation.Neighborhood,
                recommendation.Latitude,
                recommendation.Longitude))
            .ToListAsync();
    }

    public sealed record RecommendationRow(
        Guid Id,
        string DestinationName,
        string Title,
        string Category,
        ContentAccessLevel AccessLevel,
        string Neighborhood,
        decimal Latitude,
        decimal Longitude)
    {
        public string AccessLevelLabel => ProductAccessModel.GetLabel(AccessLevel);
    }

    public sealed class RecommendationInput
    {
        public Guid? Id { get; set; }
        public Guid DestinationId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Neighborhood { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public int SuggestedDurationMinutes { get; set; } = 60;
        public ContentAccessLevel AccessLevel { get; set; } = ContentAccessLevel.Free;

        public static RecommendationInput FromEntity(Recommendation recommendation)
        {
            return new RecommendationInput
            {
                Id = recommendation.Id,
                DestinationId = recommendation.DestinationId,
                Title = recommendation.Title,
                Category = recommendation.Category,
                Neighborhood = recommendation.Neighborhood,
                Description = recommendation.Description,
                Latitude = recommendation.Latitude,
                Longitude = recommendation.Longitude,
                SuggestedDurationMinutes = recommendation.SuggestedDurationMinutes,
                AccessLevel = recommendation.AccessLevel
            };
        }

        public void ApplyTo(Recommendation recommendation)
        {
            recommendation.DestinationId = DestinationId;
            recommendation.Title = Title.Trim();
            recommendation.Category = Category.Trim();
            recommendation.Neighborhood = (Neighborhood ?? string.Empty).Trim();
            recommendation.Description = Description.Trim();
            recommendation.Latitude = Latitude;
            recommendation.Longitude = Longitude;
            recommendation.SuggestedDurationMinutes = SuggestedDurationMinutes;
            recommendation.AccessLevel = AccessLevel;
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class RecommendationsModel(
    TravelCompanionDbContext dbContext,
    IRecommendationTagCatalogService tagCatalogService) : PageModel
{
    public List<RecommendationRow> Recommendations { get; private set; } = [];
    public List<SelectListItem> DestinationOptions { get; private set; } = [];
    public List<SelectListItem> PackageOptions { get; private set; } = [];
    public IReadOnlyList<RecommendationTagDto> TagCatalog { get; private set; } = [];
    public List<SelectListItem> AccessLevelOptions { get; } =
    [
        new("Free", ContentAccessLevel.Free.ToString()),
        new("Suscripcion", ContentAccessLevel.Subscription.ToString()),
        new("Paquete", ContentAccessLevel.Paid.ToString())
    ];

    [BindProperty]
    public RecommendationInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(Guid? editId)
    {
        await LoadPageDataAsync();

        if (editId.HasValue)
        {
            var recommendation = await dbContext.Recommendations
                .Include(existingRecommendation => existingRecommendation.Packages)
                .FirstOrDefaultAsync(existingRecommendation => existingRecommendation.Id == editId.Value);
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

        if (string.IsNullOrWhiteSpace(Input.PriceLevel))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.PriceLevel)}", "El nivel de precio es obligatorio.");
        }

        if (Input.Rating is < 0 or > 5)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Rating)}", "El rating debe estar entre 0 y 5.");
        }

        var selectedPackageIds = Input.AccessLevel == ContentAccessLevel.Paid
            ? Input.PackageIds.Distinct().ToList()
            : [];
        var selectedPackages = selectedPackageIds.Count == 0
            ? []
            : await dbContext.TravelPackages
                .Where(package => selectedPackageIds.Contains(package.Id))
                .ToListAsync();

        if (selectedPackages.Count != selectedPackageIds.Count)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.PackageIds)}", "Selecciona paquetes validos.");
        }

        if (selectedPackages.Any(package => package.DestinationId != Input.DestinationId))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.PackageIds)}", "Los paquetes deben pertenecer al mismo destino que la recomendacion.");
        }

        if (Input.AccessLevel == ContentAccessLevel.Paid && selectedPackageIds.Count == 0)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.PackageIds)}", "Selecciona al menos un paquete.");
        }

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        var tagNormalization = await tagCatalogService.NormalizeTagsAsync(
            RecommendationInput.ParseTags(Input.TagsText),
            cancellationToken: HttpContext.RequestAborted);

        Recommendation recommendation;
        if (Input.Id.HasValue)
        {
            recommendation = await dbContext.Recommendations
                .Include(existingRecommendation => existingRecommendation.Packages)
                .FirstOrDefaultAsync(existingRecommendation => existingRecommendation.Id == Input.Id.Value)
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
        recommendation.Tags = tagNormalization.Tags.ToList();
        recommendation.Packages.Clear();
        recommendation.Packages.AddRange(selectedPackages);

        await dbContext.SaveChangesAsync();
        StatusMessage = CreateSaveStatusMessage(tagNormalization);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var recommendation = await dbContext.Recommendations.FindAsync(id);
        if (recommendation is not null)
        {
            dbContext.Recommendations.Remove(recommendation);
            await dbContext.SaveChangesAsync();
            StatusMessage = "Recomendacion borrada.";
        }

        return RedirectToPage();
    }

    private async Task LoadPageDataAsync()
    {
        TagCatalog = await tagCatalogService.GetCatalogAsync(cancellationToken: HttpContext.RequestAborted);

        DestinationOptions = await dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .Select(destination => new SelectListItem(destination.Name, destination.Id.ToString()))
            .ToListAsync();

        PackageOptions = await dbContext.TravelPackages
            .AsNoTracking()
            .Include(package => package.Destination)
            .OrderBy(package => package.Destination!.Name)
            .ThenBy(package => package.Name)
            .Select(package => new SelectListItem(
                $"{package.Name} ({(package.Destination != null ? package.Destination.Name : "Unknown")})",
                package.Id.ToString()))
            .ToListAsync();

        var recommendations = await dbContext.Recommendations
            .AsNoTracking()
            .Include(recommendation => recommendation.Destination)
            .Include(recommendation => recommendation.Packages)
            .OrderBy(recommendation => recommendation.Title)
            .ToListAsync();

        Recommendations = recommendations
            .Select(recommendation => new RecommendationRow(
                recommendation.Id,
                recommendation.ExternalId,
                recommendation.Destination != null ? recommendation.Destination.Name : "Unknown",
                recommendation.Title,
                recommendation.Category,
                recommendation.AccessLevel,
                recommendation.Packages
                    .OrderBy(package => package.Name)
                    .Select(package => package.Name)
                    .ToList(),
                recommendation.Neighborhood,
                recommendation.PriceLevel,
                recommendation.Rating,
                recommendation.OpeningHours,
                recommendation.SourceName,
                recommendation.Latitude,
                recommendation.Longitude,
                recommendation.Tags))
            .ToList();
    }

    private static string CreateSaveStatusMessage(RecommendationTagNormalizationResult tagNormalization)
    {
        var messages = new List<string> { "Recomendacion guardada." };

        if (tagNormalization.Replacements.Count > 0)
        {
            var replacements = tagNormalization.Replacements
                .Select(replacement => $"{replacement.Key} -> {replacement.Value}");
            messages.Add($"Tags normalizados: {string.Join(", ", replacements)}.");
        }

        if (tagNormalization.UnknownTags.Count > 0)
        {
            messages.Add($"Tags nuevos sin alias conocido: {string.Join(", ", tagNormalization.UnknownTags)}.");
        }

        return string.Join(" ", messages);
    }

    public sealed record RecommendationRow(
        Guid Id,
        string? ExternalId,
        string DestinationName,
        string Title,
        string Category,
        ContentAccessLevel AccessLevel,
        IReadOnlyList<string> PackageNames,
        string Neighborhood,
        string PriceLevel,
        double? Rating,
        string? OpeningHours,
        string? SourceName,
        decimal Latitude,
        decimal Longitude,
        IReadOnlyList<string> Tags)
    {
        public string AccessLevelLabel => ProductAccessModel.GetLabel(AccessLevel);
        public string AccessSummary => PackageNames.Count == 0
            ? AccessLevel switch
            {
                ContentAccessLevel.Free => "Free",
                ContentAccessLevel.Subscription => "Suscripcion",
                _ => AccessLevelLabel
            }
            : string.Join(", ", PackageNames);
    }

    public sealed class RecommendationInput
    {
        public Guid? Id { get; set; }
        public string? ExternalId { get; set; }
        public Guid DestinationId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Neighborhood { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? TagsText { get; set; }
        public string PriceLevel { get; set; } = "medium";
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public int SuggestedDurationMinutes { get; set; } = 60;
        public double? Rating { get; set; }
        public string? OpeningHours { get; set; }
        public string? SourceName { get; set; }
        public string? SourceUrl { get; set; }
        public string? CurationNotes { get; set; }
        public ContentAccessLevel AccessLevel { get; set; } = ContentAccessLevel.Free;
        public List<Guid> PackageIds { get; set; } = [];

        public static RecommendationInput FromEntity(Recommendation recommendation)
        {
            return new RecommendationInput
            {
                Id = recommendation.Id,
                ExternalId = recommendation.ExternalId,
                DestinationId = recommendation.DestinationId,
                Title = recommendation.Title,
                Category = recommendation.Category,
                Neighborhood = recommendation.Neighborhood,
                Description = recommendation.Description,
                TagsText = string.Join(", ", recommendation.Tags),
                PriceLevel = recommendation.PriceLevel,
                Latitude = recommendation.Latitude,
                Longitude = recommendation.Longitude,
                SuggestedDurationMinutes = recommendation.SuggestedDurationMinutes,
                Rating = recommendation.Rating,
                OpeningHours = recommendation.OpeningHours,
                SourceName = recommendation.SourceName,
                SourceUrl = recommendation.SourceUrl,
                CurationNotes = recommendation.CurationNotes,
                AccessLevel = recommendation.AccessLevel,
                PackageIds = recommendation.Packages.Select(package => package.Id).ToList()
            };
        }

        public void ApplyTo(Recommendation recommendation)
        {
            recommendation.ExternalId = NormalizeOptional(ExternalId);
            recommendation.DestinationId = DestinationId;
            recommendation.Title = Title.Trim();
            recommendation.Category = Category.Trim();
            recommendation.Neighborhood = (Neighborhood ?? string.Empty).Trim();
            recommendation.Description = Description.Trim();
            recommendation.Tags = ParseTags(TagsText);
            recommendation.PriceLevel = PriceLevel.Trim();
            recommendation.Latitude = Latitude;
            recommendation.Longitude = Longitude;
            recommendation.SuggestedDurationMinutes = SuggestedDurationMinutes;
            recommendation.Rating = Rating;
            recommendation.OpeningHours = string.IsNullOrWhiteSpace(OpeningHours) ? null : OpeningHours.Trim();
            recommendation.SourceName = NormalizeOptional(SourceName);
            recommendation.SourceUrl = NormalizeOptional(SourceUrl);
            recommendation.CurationNotes = NormalizeOptional(CurationNotes);
            recommendation.AccessLevel = AccessLevel;
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public static List<string> ParseTags(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? []
                : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
    }
}

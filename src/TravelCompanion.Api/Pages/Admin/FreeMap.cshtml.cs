using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class FreeMapModel(TravelCompanionDbContext dbContext) : PageModel
{
    public List<FreeMapCityRow> Cities { get; private set; } = [];
    public List<SelectListItem> DestinationOptions { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public FreeMapCityInput Input { get; set; } = new();

    public async Task OnGetAsync(Guid? editId)
    {
        await LoadPageDataAsync();
        if (!editId.HasValue)
        {
            Input.DestinationId = DestinationOptions.Count == 1
                ? Guid.Parse(DestinationOptions[0].Value)
                : Guid.Empty;
            return;
        }

        var city = await dbContext.FreeMapCities.FindAsync(editId.Value);
        if (city is not null)
        {
            Input = FreeMapCityInput.FromEntity(city);
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var citySlug = RecommendationCitySlug.FromCity(Input.CitySlug);
        ValidateInput(citySlug);

        var duplicate = await dbContext.FreeMapCities.AnyAsync(city =>
            city.DestinationId == Input.DestinationId
            && city.CitySlug == citySlug
            && city.Id != Input.Id);
        if (duplicate)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.CitySlug)}", "Ya existe esa ciudad para el destino.");
        }

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        FreeMapCity city;
        if (Input.Id.HasValue)
        {
            city = await dbContext.FreeMapCities.FindAsync(Input.Id.Value)
                ?? throw new InvalidOperationException("Free map city not found.");
        }
        else
        {
            city = new FreeMapCity
            {
                Id = Guid.NewGuid(),
                DestinationId = Input.DestinationId,
                CitySlug = citySlug,
                DisplayName = string.Empty
            };
            dbContext.FreeMapCities.Add(city);
        }

        Input.ApplyTo(city, citySlug);
        await dbContext.SaveChangesAsync();
        StatusMessage = $"Ciudad {city.DisplayName} guardada.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var city = await dbContext.FreeMapCities.FindAsync(id);
        if (city is not null)
        {
            dbContext.FreeMapCities.Remove(city);
            await dbContext.SaveChangesAsync();
            StatusMessage = "Ciudad eliminada del mapa gratuito.";
        }

        return RedirectToPage();
    }

    private void ValidateInput(string citySlug)
    {
        if (!dbContext.Destinations.Any(destination => destination.Id == Input.DestinationId))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.DestinationId)}", "Selecciona un destino valido.");
        }

        if (string.IsNullOrWhiteSpace(citySlug))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.CitySlug)}", "El slug es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(Input.DisplayName))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.DisplayName)}", "El nombre es obligatorio.");
        }

        if (Input.CenterLatitude is < -90 or > 90)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.CenterLatitude)}", "La latitud debe estar entre -90 y 90.");
        }

        if (Input.CenterLongitude is < -180 or > 180)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.CenterLongitude)}", "La longitud debe estar entre -180 y 180.");
        }

        if (Input.FreeRadiusKm is < 0.25m or > 10m)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.FreeRadiusKm)}", "El radio gratuito debe estar entre 0.25 y 10 km.");
        }

        if (Input.CoverageRadiusKm < Input.FreeRadiusKm || Input.CoverageRadiusKm > 100m)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.CoverageRadiusKm)}", "La cobertura debe ser mayor al radio gratuito y no superar 100 km.");
        }

        if (!string.IsNullOrWhiteSpace(Input.ContactUrl)
            && (!Uri.TryCreate(Input.ContactUrl.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https" or "mailto")))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ContactUrl)}", "Usa una URL http, https o mailto valida.");
        }
    }

    private async Task LoadPageDataAsync()
    {
        DestinationOptions = await dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .Select(destination => new SelectListItem(destination.Name, destination.Id.ToString()))
            .ToListAsync();

        Cities = await dbContext.FreeMapCities
            .AsNoTracking()
            .Include(city => city.Destination)
            .OrderBy(city => city.SortOrder)
            .ThenBy(city => city.DisplayName)
            .Select(city => new FreeMapCityRow(
                city.Id,
                city.DisplayName,
                city.CitySlug,
                city.Destination!.Name,
                city.CenterLatitude,
                city.CenterLongitude,
                city.FreeRadiusKm,
                city.CoverageRadiusKm,
                city.IsEnabled,
                dbContext.Recommendations.Count(recommendation =>
                    recommendation.DestinationId == city.DestinationId
                    && recommendation.CitySlug == city.CitySlug)))
            .ToListAsync();
    }

    public sealed record FreeMapCityRow(
        Guid Id,
        string DisplayName,
        string CitySlug,
        string DestinationName,
        decimal CenterLatitude,
        decimal CenterLongitude,
        decimal FreeRadiusKm,
        decimal CoverageRadiusKm,
        bool IsEnabled,
        int RecommendationCount);

    public sealed class FreeMapCityInput
    {
        public Guid? Id { get; set; }
        public Guid DestinationId { get; set; }
        public string CitySlug { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public decimal CenterLatitude { get; set; }
        public decimal CenterLongitude { get; set; }
        public decimal FreeRadiusKm { get; set; } = 2m;
        public decimal CoverageRadiusKm { get; set; } = 25m;
        public int SortOrder { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string? ContactUrl { get; set; }

        public static FreeMapCityInput FromEntity(FreeMapCity city) => new()
        {
            Id = city.Id,
            DestinationId = city.DestinationId,
            CitySlug = city.CitySlug,
            DisplayName = city.DisplayName,
            CenterLatitude = city.CenterLatitude,
            CenterLongitude = city.CenterLongitude,
            FreeRadiusKm = city.FreeRadiusKm,
            CoverageRadiusKm = city.CoverageRadiusKm,
            SortOrder = city.SortOrder,
            IsEnabled = city.IsEnabled,
            ContactUrl = city.ContactUrl
        };

        public void ApplyTo(FreeMapCity city, string normalizedSlug)
        {
            city.DestinationId = DestinationId;
            city.CitySlug = normalizedSlug;
            city.DisplayName = DisplayName.Trim();
            city.CenterLatitude = CenterLatitude;
            city.CenterLongitude = CenterLongitude;
            city.FreeRadiusKm = FreeRadiusKm;
            city.CoverageRadiusKm = CoverageRadiusKm;
            city.SortOrder = SortOrder;
            city.IsEnabled = IsEnabled;
            city.ContactUrl = string.IsNullOrWhiteSpace(ContactUrl) ? null : ContactUrl.Trim();
        }
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class TripsModel(
    TravelCompanionDbContext dbContext,
    TripPlanEditorService editorService) : PageModel
{
    public IReadOnlyList<TripPlanListItem> Trips { get; private set; } = [];
    public IReadOnlyList<SelectListItem> DestinationOptions { get; private set; } = [];
    public string JapanDestinationName { get; private set; } = "Japón";
    public TripPlanEditorState? Editor { get; private set; }
    public string EditorStateJson { get; private set; } = "{}";

    [BindProperty(SupportsGet = true)]
    public Guid? TripId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty]
    public CreateTripInput CreateInput { get; set; } = new();

    [BindProperty]
    public string DraftJson { get; set; } = string.Empty;

    [BindProperty]
    public int BasePlanRevision { get; set; }

    [BindProperty]
    [RegularExpression("^$|^[0-9]{4}$", ErrorMessage = "El PIN debe tener exactamente 4 números.")]
    public string? NewAccessPin { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadPageAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        ModelState.Remove(nameof(DraftJson));
        ModelState.Remove(nameof(NewAccessPin));
        if (CreateInput.CitySegments.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Agregá al menos una ciudad al viaje.");
        }
        else if (CreateInput.CitySegments.Any(segment => segment.StartsOn == default || segment.EndsOn == default))
        {
            ModelState.AddModelError(string.Empty, "Completá las fechas de todos los tramos de ciudad.");
        }

        if (!ModelState.IsValid)
        {
            await LoadPageAsync();
            return Page();
        }

        try
        {
            var startsOn = CreateInput.CitySegments.Min(segment => segment.StartsOn);
            var endsOn = CreateInput.CitySegments.Max(segment => segment.EndsOn);
            var tripId = await editorService.CreateTripAsync(
                new CreateTripPlanCommand(
                    CreateInput.TravelerName,
                    CreateInput.AccessPin,
                    CreateInput.DestinationId,
                    startsOn,
                    endsOn,
                    "Asia/Tokyo",
                    CreateInput.CitySegments.Select(segment => new CreateTripCitySegment(
                        segment.City,
                        segment.StartsOn,
                        segment.EndsOn,
                        segment.HotelBase)).ToList()),
                HttpContext.RequestAborted);
            StatusMessage = "Viaje creado como borrador. Completá sus días antes de aplicarlo.";
            return RedirectToPage(new { tripId });
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadPageAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSaveDraftAsync()
    {
        ModelState.Remove(nameof(CreateInput));
        if (!TripId.HasValue || string.IsNullOrWhiteSpace(DraftJson))
        {
            ErrorMessage = "No se recibió un borrador válido.";
            return RedirectToPage();
        }

        var result = await editorService.SaveDraftAsync(
            TripId.Value,
            DraftJson,
            BasePlanRevision,
            NewAccessPin,
            HttpContext.RequestAborted);
        if (result.Success)
        {
            StatusMessage = result.Message;
        }
        else
        {
            ErrorMessage = result.Message;
        }

        return RedirectToPage(new { tripId = TripId.Value });
    }

    public async Task<IActionResult> OnPostPublishAsync()
    {
        ModelState.Remove(nameof(CreateInput));
        if (!TripId.HasValue || string.IsNullOrWhiteSpace(DraftJson))
        {
            ErrorMessage = "No se recibió un borrador válido.";
            return RedirectToPage();
        }

        var result = await editorService.PublishAsync(
            TripId.Value,
            DraftJson,
            BasePlanRevision,
            NewAccessPin,
            HttpContext.RequestAborted);
        if (result.Success)
        {
            StatusMessage = result.Message;
        }
        else
        {
            ErrorMessage = result.Message;
        }

        return RedirectToPage(new { tripId = TripId.Value });
    }

    public async Task<IActionResult> OnPostDiscardDraftAsync(Guid id)
    {
        var deletedTrip = await editorService.DiscardDraftAsync(id, HttpContext.RequestAborted);
        StatusMessage = deletedTrip
            ? "Borrador eliminado."
            : "Borrador descartado; se restauró la versión publicada.";
        return deletedTrip ? RedirectToPage() : RedirectToPage(new { tripId = id });
    }

    private async Task LoadPageAsync()
    {
        Trips = await editorService.ListTripsAsync(Search, HttpContext.RequestAborted);
        var destinations = await dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .ToListAsync(HttpContext.RequestAborted);
        DestinationOptions = destinations
            .Select(destination => new SelectListItem(destination.Name, destination.Id.ToString()))
            .ToList();
        var japan = destinations.FirstOrDefault(destination => destination.Slug == "japon")
            ?? destinations.FirstOrDefault(destination => destination.Country == "Japan")
            ?? destinations.FirstOrDefault();
        if (japan is not null)
        {
            JapanDestinationName = japan.Name;
            CreateInput.DestinationId = japan.Id;
        }

        if (TripId.HasValue)
        {
            Editor = await editorService.GetEditorAsync(TripId.Value, HttpContext.RequestAborted);
            if (Editor is null)
            {
                ErrorMessage = "El viaje no existe.";
                TripId = null;
            }
            else
            {
                BasePlanRevision = Editor.BasePlanRevision;
                EditorStateJson = editorService.SerializeForPage(Editor);
            }
        }

        if (CreateInput.CitySegments.Count == 0)
        {
            var startsOn = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
            CreateInput.CitySegments.Add(new CreateTripCityInput
            {
                City = "Tokyo",
                StartsOn = startsOn,
                EndsOn = startsOn.AddDays(3)
            });
        }
    }

    public sealed class CreateTripInput
    {
        [Required(ErrorMessage = "El nombre del cliente es obligatorio.")]
        [StringLength(140)]
        public string TravelerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "El PIN es obligatorio.")]
        [RegularExpression("^[0-9]{4}$", ErrorMessage = "El PIN debe tener exactamente 4 números.")]
        public string AccessPin { get; set; } = string.Empty;

        [Required(ErrorMessage = "Selecciona un destino.")]
        public Guid DestinationId { get; set; }

        public List<CreateTripCityInput> CitySegments { get; set; } = [];
    }

    public sealed class CreateTripCityInput
    {
        [Required(ErrorMessage = "La ciudad es obligatoria.")]
        [StringLength(120)]
        public string City { get; set; } = string.Empty;

        public DateOnly StartsOn { get; set; }
        public DateOnly EndsOn { get; set; }

        [StringLength(180)]
        public string? HotelBase { get; set; }
    }
}

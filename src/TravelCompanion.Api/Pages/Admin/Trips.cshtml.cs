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
        if (!ModelState.IsValid)
        {
            await LoadPageAsync();
            return Page();
        }

        try
        {
            var tripId = await editorService.CreateTripAsync(
                new CreateTripPlanCommand(
                    CreateInput.TravelerName,
                    CreateInput.AccessPin,
                    CreateInput.DestinationId,
                    CreateInput.StartsOn,
                    CreateInput.EndsOn,
                    CreateInput.TimeZoneId),
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
        DestinationOptions = await dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .Select(destination => new SelectListItem(destination.Name, destination.Id.ToString()))
            .ToListAsync(HttpContext.RequestAborted);

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

        if (CreateInput.StartsOn == default)
        {
            CreateInput.StartsOn = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
            CreateInput.EndsOn = CreateInput.StartsOn.AddDays(6);
            CreateInput.TimeZoneId = "Asia/Tokyo";
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

        public DateOnly StartsOn { get; set; }
        public DateOnly EndsOn { get; set; }

        [Required]
        [StringLength(120)]
        public string TimeZoneId { get; set; } = "Asia/Tokyo";
    }
}

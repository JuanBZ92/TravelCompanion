using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class DocsModel(TravelCompanionDbContext dbContext) : PageModel
{
    public List<DocumentRow> Documents { get; private set; } = [];
    public List<SelectListItem> TripOptions { get; private set; } = [];
    public List<SelectListItem> CategoryOptions { get; } =
    [
        new("Hoteles / confirmaciones", TravelDocumentCategory.Hotel.ToString()),
        new("Otros", TravelDocumentCategory.Other.ToString())
    ];

    public Guid? SelectedTripId { get; private set; }

    [BindProperty]
    public DocumentInput Input { get; set; } = new();

    public async Task OnGetAsync(Guid? selectedTripId, Guid? editId)
    {
        SelectedTripId = selectedTripId;
        await LoadPageDataAsync(selectedTripId);

        if (editId.HasValue)
        {
            var document = await dbContext.TravelDocuments.FindAsync(editId.Value);
            if (document is not null)
            {
                Input = DocumentInput.FromEntity(document);
                SelectedTripId = document.TripId;
            }
        }
        else
        {
            SetDefaultInputValues();
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (Input.TripId == Guid.Empty)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.TripId)}", "Selecciona un viaje.");
        }

        if (string.IsNullOrWhiteSpace(Input.Title))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Title)}", "El titulo es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(Input.FileUrl))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.FileUrl)}", "La URL del documento es obligatoria.");
        }

        if (!ModelState.IsValid)
        {
            SelectedTripId = Input.TripId == Guid.Empty ? null : Input.TripId;
            await LoadPageDataAsync(SelectedTripId);
            return Page();
        }

        TravelDocument document;
        if (Input.Id.HasValue)
        {
            document = await dbContext.TravelDocuments.FindAsync(Input.Id.Value)
                ?? throw new InvalidOperationException("Document not found.");
        }
        else
        {
            document = new TravelDocument
            {
                Id = Guid.NewGuid(),
                TripId = Input.TripId,
                Title = string.Empty,
                FileUrl = string.Empty
            };
            dbContext.TravelDocuments.Add(document);
        }

        Input.ApplyTo(document);
        await dbContext.SaveChangesAsync();
        return RedirectToPage(null, null, new { selectedTripId = document.TripId }, "docs-list");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, Guid? selectedTripId)
    {
        var document = await dbContext.TravelDocuments.FindAsync(id);
        if (document is not null)
        {
            selectedTripId = document.TripId;
            dbContext.TravelDocuments.Remove(document);
            await dbContext.SaveChangesAsync();
        }

        return selectedTripId.HasValue
            ? RedirectToPage(null, null, new { selectedTripId }, "docs-list")
            : RedirectToPage();
    }

    private async Task LoadPageDataAsync(Guid? selectedTripId)
    {
        TripOptions = await dbContext.Trips
            .AsNoTracking()
            .Include(trip => trip.Destination)
            .Include(trip => trip.AppUser)
            .OrderByDescending(trip => trip.StartsOn)
            .Select(trip => new SelectListItem(
                $"{trip.TravelerName} - {(trip.Destination != null ? trip.Destination.Name : "Unknown")} ({trip.StartsOn:yyyy-MM-dd})",
                trip.Id.ToString()))
            .ToListAsync();

        var query = dbContext.TravelDocuments
            .AsNoTracking()
            .Include(document => document.Trip)
                .ThenInclude(trip => trip!.Destination)
            .AsQueryable();

        if (selectedTripId.HasValue)
        {
            query = query.Where(document => document.TripId == selectedTripId.Value);
        }

        Documents = await query
            .OrderBy(document => document.Category)
            .ThenBy(document => document.SortOrder)
            .ThenBy(document => document.Title)
            .Select(document => new DocumentRow(
                document.Id,
                document.TripId,
                document.Trip != null
                    ? $"{document.Trip.TravelerName} - {(document.Trip.Destination != null ? document.Trip.Destination.Name : "Unknown")}"
                    : "Unknown",
                document.ExternalId,
                document.Category,
                document.Title,
                document.Subtitle,
                document.FileUrl,
                document.SortOrder))
            .ToListAsync();
    }

    private void SetDefaultInputValues()
    {
        if (Input.TripId == Guid.Empty)
        {
            if (SelectedTripId.HasValue)
            {
                Input.TripId = SelectedTripId.Value;
            }
            else if (TripOptions.Count > 0)
            {
                Input.TripId = Guid.Parse(TripOptions[0].Value);
            }
        }
    }

    public sealed record DocumentRow(
        Guid Id,
        Guid TripId,
        string TripName,
        string? ExternalId,
        TravelDocumentCategory Category,
        string Title,
        string Subtitle,
        string FileUrl,
        int SortOrder)
    {
        public string CategoryLabel => Category == TravelDocumentCategory.Hotel
            ? "Hotel"
            : "Otro";
    }

    public sealed class DocumentInput
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "Selecciona un viaje.")]
        public Guid TripId { get; set; }

        [StringLength(160, ErrorMessage = "El external id no puede superar 160 caracteres.")]
        public string? ExternalId { get; set; }

        [Required(ErrorMessage = "Selecciona una categoria.")]
        public TravelDocumentCategory Category { get; set; } = TravelDocumentCategory.Hotel;

        [Required(ErrorMessage = "El titulo es obligatorio.")]
        [StringLength(160, ErrorMessage = "El titulo no puede superar 160 caracteres.")]
        public string Title { get; set; } = string.Empty;

        [StringLength(220, ErrorMessage = "El subtitulo no puede superar 220 caracteres.")]
        public string? Subtitle { get; set; }

        [Required(ErrorMessage = "La URL del documento es obligatoria.")]
        [StringLength(512, ErrorMessage = "La URL no puede superar 512 caracteres.")]
        public string FileUrl { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public static DocumentInput FromEntity(TravelDocument document)
        {
            return new DocumentInput
            {
                Id = document.Id,
                TripId = document.TripId,
                ExternalId = document.ExternalId,
                Category = document.Category,
                Title = document.Title,
                Subtitle = document.Subtitle,
                FileUrl = document.FileUrl,
                SortOrder = document.SortOrder
            };
        }

        public void ApplyTo(TravelDocument document)
        {
            document.TripId = TripId;
            document.ExternalId = string.IsNullOrWhiteSpace(ExternalId) ? null : ExternalId.Trim();
            document.Category = Category;
            document.Title = Title.Trim();
            document.Subtitle = string.IsNullOrWhiteSpace(Subtitle) ? string.Empty : Subtitle.Trim();
            document.FileUrl = FileUrl.Trim();
            document.SortOrder = SortOrder;
        }
    }
}

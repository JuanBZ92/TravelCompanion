using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class TripImportModel(
    TripWorkbookImportService importService) : PageModel
{
    private const int MaxWorkbookBytes = 4 * 1024 * 1024;

    [BindProperty]
    public IFormFile? Workbook { get; set; }

    [BindProperty]
    public string? PreviewWorkbookBase64 { get; set; }

    public TripWorkbookImportResult? ImportResult { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetTemplateAsync()
    {
        var bytes = await importService.CreateTemplateAsync(HttpContext.RequestAborted);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "travel-trip-template.xlsx");
    }

    public async Task<IActionResult> OnGetExampleAsync()
    {
        var bytes = await importService.CreateExampleWorkbookAsync(HttpContext.RequestAborted);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "travel-trip-example-18-days-japan.xlsx");
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        var workbookBytes = await ReadWorkbookBytesAsync();
        if (workbookBytes is null)
        {
            return Page();
        }

        PreviewWorkbookBase64 = Convert.ToBase64String(workbookBytes);
        await using var stream = new MemoryStream(workbookBytes);
        ImportResult = await importService.PreviewAsync(stream, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        var workbookBytes = await ReadWorkbookBytesAsync(allowPreviewPayload: true);
        if (workbookBytes is null)
        {
            return Page();
        }

        await using var stream = new MemoryStream(workbookBytes);
        ImportResult = await importService.ImportAsync(stream, HttpContext.RequestAborted);
        if (ImportResult.Imported)
        {
            StatusMessage = ImportResult.StatusMessage;
            return RedirectToPage();
        }

        PreviewWorkbookBase64 = Convert.ToBase64String(workbookBytes);
        return Page();
    }

    private async Task<byte[]?> ReadWorkbookBytesAsync(bool allowPreviewPayload = false)
    {
        if (Workbook is not null && Workbook.Length > 0)
        {
            if (Workbook.Length > MaxWorkbookBytes)
            {
                ModelState.AddModelError(nameof(Workbook), "El archivo no puede superar 4 MB.");
                return null;
            }

            var extension = Path.GetExtension(Workbook.FileName);
            if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(Workbook), "Subi un archivo .xlsx.");
                return null;
            }

            await using var stream = Workbook.OpenReadStream();
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, HttpContext.RequestAborted);
            return buffer.ToArray();
        }

        if (allowPreviewPayload && !string.IsNullOrWhiteSpace(PreviewWorkbookBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(PreviewWorkbookBase64);
                if (bytes.Length > MaxWorkbookBytes)
                {
                    ModelState.AddModelError(nameof(Workbook), "El archivo no puede superar 4 MB.");
                    return null;
                }

                return bytes;
            }
            catch (FormatException)
            {
                ModelState.AddModelError(nameof(Workbook), "El preview expiro o esta corrupto. Volve a subir el archivo.");
                return null;
            }
        }

        ModelState.AddModelError(nameof(Workbook), "Subi el Excel de viaje.");
        return null;
    }
}

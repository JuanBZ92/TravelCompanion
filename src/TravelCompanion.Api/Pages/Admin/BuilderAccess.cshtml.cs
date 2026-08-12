using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class BuilderAccessModel(
    TravelCompanionDbContext dbContext,
    IPasswordHasher<BuilderAccessGrant> pinHasher,
    UserSessionService sessionService) : PageModel
{
    [BindProperty]
    public CreateBuilderAccessInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public List<BuilderAccessRow> Grants { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var destination = await dbContext.Destinations.OrderBy(item => item.Slug == "japon" ? 0 : 1).FirstOrDefaultAsync();
        if (destination is null)
        {
            ModelState.AddModelError(string.Empty, "Crea el destino Japon antes de generar accesos.");
            await LoadAsync();
            return Page();
        }

        var grant = new BuilderAccessGrant
        {
            Id = Guid.NewGuid(),
            AppUserId = Guid.NewGuid(),
            DestinationId = destination.Id,
            PinHash = string.Empty,
            OrderReference = Input.OrderReference?.Trim(),
            ExpiresAtUtc = Input.ExpiresOn.HasValue
                ? new DateTimeOffset(Input.ExpiresOn.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
                : null
        };
        var user = new AppUser
        {
            Id = grant.AppUserId,
            Email = $"builder-{grant.Id:N}@travelcompanion.local",
            DisplayName = Input.CustomerName.Trim(),
            MustChangePassword = false
        };
        grant.AppUser = user;
        var pin = await GenerateUniquePinAsync();
        grant.PinHash = pinHasher.HashPassword(grant, pin);
        dbContext.AppUsers.Add(user);
        dbContext.BuilderAccessGrants.Add(grant);
        await dbContext.SaveChangesAsync();
        StatusMessage = $"Acceso creado para {user.DisplayName}. PIN: {pin}. Se muestra una sola vez.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id)
    {
        var grant = await dbContext.BuilderAccessGrants.FindAsync(id);
        if (grant is not null)
        {
            grant.Status = BuilderAccessStatus.Revoked;
            grant.RevokedAtUtc = DateTimeOffset.UtcNow;
            await sessionService.RevokeUserSessionsAsync(grant.AppUserId);
            await dbContext.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRegenerateAsync(Guid id)
    {
        var grant = await dbContext.BuilderAccessGrants.Include(item => item.AppUser).SingleOrDefaultAsync(item => item.Id == id);
        if (grant is not null && grant.Status == BuilderAccessStatus.Active)
        {
            var pin = await GenerateUniquePinAsync();
            grant.PinHash = pinHasher.HashPassword(grant, pin);
            await sessionService.RevokeUserSessionsAsync(grant.AppUserId);
            await dbContext.SaveChangesAsync();
            StatusMessage = $"Nuevo PIN para {grant.AppUser?.DisplayName}: {pin}. Se muestra una sola vez.";
        }
        return RedirectToPage();
    }

    private async Task<string> GenerateUniquePinAsync()
    {
        var active = await dbContext.BuilderAccessGrants.Where(item => item.Status == BuilderAccessStatus.Active).ToListAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            if (active.All(item => pinHasher.VerifyHashedPassword(item, item.PinHash, candidate) == PasswordVerificationResult.Failed))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("No se pudo generar un PIN unico.");
    }

    private async Task LoadAsync()
    {
        Grants = await dbContext.BuilderAccessGrants.AsNoTracking()
            .Include(item => item.AppUser).Include(item => item.Trip)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => new BuilderAccessRow(item.Id, item.AppUser!.DisplayName, item.Status.ToString(), item.TripId.HasValue, item.CreatedAtUtc, item.ExpiresAtUtc, item.OrderReference))
            .ToListAsync();
    }

    public sealed class CreateBuilderAccessInput
    {
        [Required, MaxLength(140)] public string CustomerName { get; set; } = string.Empty;
        [MaxLength(120)] public string? OrderReference { get; set; }
        public DateOnly? ExpiresOn { get; set; }
    }

    public sealed record BuilderAccessRow(Guid Id, string CustomerName, string Status, bool HasTrip, DateTimeOffset CreatedAtUtc, DateTimeOffset? ExpiresAtUtc, string? OrderReference);
}

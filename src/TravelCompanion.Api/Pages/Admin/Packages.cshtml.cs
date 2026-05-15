using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class PackagesModel(TravelCompanionDbContext dbContext) : PageModel
{
    public List<PackageRow> Packages { get; private set; } = [];
    public List<AssignedUserRow> AssignedUsers { get; private set; } = [];
    public List<SelectListItem> DestinationOptions { get; private set; } = [];
    public List<SelectListItem> UserOptions { get; private set; } = [];
    public List<SelectListItem> AvailableUserOptions { get; private set; } = [];
    public PackageRow? SelectedPackage { get; private set; }
    public Guid? SelectedPackageId { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public PackageInput Input { get; set; } = new();

    public async Task OnGetAsync(Guid? editId, Guid? selectedPackageId)
    {
        SelectedPackageId = selectedPackageId;
        await LoadPageDataAsync(selectedPackageId);

        if (editId.HasValue)
        {
            var package = await dbContext.TravelPackages.FindAsync(editId.Value);
            if (package is not null)
            {
                Input = PackageInput.FromEntity(package);
            }
        }
        else
        {
            SetDefaultDestination();
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var normalizedSlug = NormalizeSlug(Input.Slug);
        if (Input.DestinationId == Guid.Empty)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.DestinationId)}", "Selecciona un destino.");
        }

        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Name)}", "El nombre es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Slug)}", "El slug es obligatorio.");
        }

        if (Input.Price < 0)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Price)}", "El precio no puede ser negativo.");
        }

        if (string.IsNullOrWhiteSpace(Input.Currency))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Currency)}", "La moneda es obligatoria.");
        }

        var duplicateExists = await dbContext.TravelPackages.AnyAsync(package =>
            package.Slug == normalizedSlug && package.Id != Input.Id);
        if (duplicateExists)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Slug)}", "Ya existe un paquete con ese slug.");
        }

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync(SelectedPackageId);
            return Page();
        }

        TravelPackage package;
        if (Input.Id.HasValue)
        {
            package = await dbContext.TravelPackages.FindAsync(Input.Id.Value)
                ?? throw new InvalidOperationException("Package not found.");
        }
        else
        {
            package = new TravelPackage
            {
                Id = Guid.NewGuid(),
                DestinationId = Input.DestinationId,
                Name = string.Empty,
                Slug = string.Empty,
                Description = string.Empty,
                Currency = "USD"
            };
            dbContext.TravelPackages.Add(package);
        }

        Input.ApplyTo(package, normalizedSlug);
        await dbContext.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var package = await dbContext.TravelPackages.FindAsync(id);
        if (package is null)
        {
            return RedirectToPage();
        }

        var entitlementCount = await dbContext.UserEntitlements.CountAsync(entitlement => entitlement.TravelPackageId == id);
        if (entitlementCount > 0)
        {
            StatusMessage = "No se puede borrar un paquete con accesos de usuario asociados.";
            return RedirectToPage();
        }

        dbContext.TravelPackages.Remove(package);
        await dbContext.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGrantAccessAsync(Guid packageId, Guid userId, DateOnly? expiresOn)
    {
        var package = await dbContext.TravelPackages.FindAsync(packageId);
        var user = await dbContext.AppUsers.FindAsync(userId);
        if (package is null || user is null)
        {
            StatusMessage = "Selecciona un paquete y un usuario validos.";
            return RedirectToPackageUsers(packageId);
        }

        var now = DateTimeOffset.UtcNow;
        var alreadyHasAccess = await dbContext.UserEntitlements.AnyAsync(entitlement =>
            entitlement.UserId == userId
            && entitlement.TravelPackageId == packageId
            && (entitlement.ExpiresAt == null || entitlement.ExpiresAt > now));

        if (alreadyHasAccess)
        {
            StatusMessage = $"{user.Email} ya tiene acceso activo a {package.Name}.";
            return RedirectToPackageUsers(packageId);
        }

        dbContext.UserEntitlements.Add(new UserEntitlement
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AccessLevel = ContentAccessPolicy.GetPackageGrantLevel(package.IsSubscription),
            DestinationId = package.DestinationId,
            TravelPackageId = package.Id,
            GrantedAt = now,
            ExpiresAt = expiresOn.HasValue
                ? new DateTimeOffset(expiresOn.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
                : null,
            Source = "admin-package"
        });

        await dbContext.SaveChangesAsync();
        StatusMessage = $"Acceso activado: {package.Name} para {user.Email}.";
        return RedirectToPackageUsers(packageId);
    }

    public async Task<IActionResult> OnPostRevokeAccessAsync(Guid entitlementId, Guid selectedPackageId)
    {
        var entitlement = await dbContext.UserEntitlements.FindAsync(entitlementId);
        if (entitlement is not null)
        {
            dbContext.UserEntitlements.Remove(entitlement);
            await dbContext.SaveChangesAsync();
            StatusMessage = "Acceso removido.";
        }

        return RedirectToPackageUsers(selectedPackageId);
    }

    private async Task LoadPageDataAsync(Guid? selectedPackageId)
    {
        DestinationOptions = await dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .Select(destination => new SelectListItem(destination.Name, destination.Id.ToString()))
            .ToListAsync();

        UserOptions = await dbContext.AppUsers
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .Select(user => new SelectListItem($"{user.DisplayName} ({user.Email})", user.Id.ToString()))
            .ToListAsync();

        Packages = await dbContext.TravelPackages
            .AsNoTracking()
            .Include(package => package.Destination)
            .OrderBy(package => package.Destination!.Name)
            .ThenBy(package => package.Name)
            .Select(package => new PackageRow(
                package.Id,
                package.Destination != null ? package.Destination.Name : "Unknown",
                package.Name,
                package.Slug,
                package.Price,
                package.Currency,
                package.IsSubscription,
                dbContext.UserEntitlements.Count(entitlement => entitlement.TravelPackageId == package.Id)))
            .ToListAsync();

        SelectedPackage = selectedPackageId.HasValue
            ? Packages.FirstOrDefault(package => package.Id == selectedPackageId.Value)
            : Packages.FirstOrDefault();
        SelectedPackageId = SelectedPackage?.Id;

        if (SelectedPackageId.HasValue)
        {
            AssignedUsers = await dbContext.UserEntitlements
                .AsNoTracking()
                .Include(entitlement => entitlement.User)
                .Where(entitlement => entitlement.TravelPackageId == SelectedPackageId.Value)
                .OrderBy(entitlement => entitlement.User!.Email)
                .Select(entitlement => new AssignedUserRow(
                    entitlement.Id,
                    entitlement.User != null ? entitlement.User.Email : "Unknown",
                    entitlement.User != null ? entitlement.User.DisplayName : "Unknown",
                    entitlement.AccessLevel,
                    entitlement.GrantedAt,
                    entitlement.ExpiresAt))
                .ToListAsync();

            var activeAssignedUserIds = await dbContext.UserEntitlements
                .AsNoTracking()
                .Where(entitlement => entitlement.TravelPackageId == SelectedPackageId.Value)
                .Where(entitlement => entitlement.ExpiresAt == null || entitlement.ExpiresAt > DateTimeOffset.UtcNow)
                .Select(entitlement => entitlement.UserId)
                .ToListAsync();

            var assignedSet = activeAssignedUserIds.ToHashSet();
            AvailableUserOptions = UserOptions
                .Where(user => Guid.TryParse(user.Value, out var userId) && !assignedSet.Contains(userId))
                .ToList();
        }
    }

    private RedirectToPageResult RedirectToPackageUsers(Guid selectedPackageId)
    {
        return RedirectToPage(null, null, new { selectedPackageId }, "package-users");
    }

    private void SetDefaultDestination()
    {
        if (Input.DestinationId == Guid.Empty && DestinationOptions.Count > 0)
        {
            Input.DestinationId = Guid.Parse(DestinationOptions[0].Value);
        }
    }

    private static string NormalizeSlug(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '-');
    }

    public sealed record PackageRow(
        Guid Id,
        string DestinationName,
        string Name,
        string Slug,
        decimal Price,
        string Currency,
        bool IsSubscription,
        int EntitlementCount)
    {
        public ContentAccessLevel GrantLevel => ContentAccessPolicy.GetPackageGrantLevel(IsSubscription);
        public string ProductTypeLabel => ProductAccessModel.GetLabel(GrantLevel);
    }

    public sealed record AssignedUserRow(
        Guid EntitlementId,
        string Email,
        string DisplayName,
        ContentAccessLevel AccessLevel,
        DateTimeOffset GrantedAt,
        DateTimeOffset? ExpiresAt)
    {
        public string Status => ExpiresAt is not null && ExpiresAt <= DateTimeOffset.UtcNow
            ? "Expirado"
            : "Activo";

        public string AccessLevelLabel => ProductAccessModel.GetLabel(AccessLevel);
    }

    public sealed class PackageInput
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "Selecciona un destino.")]
        public Guid DestinationId { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(120, ErrorMessage = "El nombre no puede superar 120 caracteres.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "El slug es obligatorio.")]
        [StringLength(120, ErrorMessage = "El slug no puede superar 120 caracteres.")]
        public string Slug { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "La descripcion no puede superar 500 caracteres.")]
        public string Description { get; set; } = string.Empty;

        [Range(0, 999999, ErrorMessage = "El precio no puede ser negativo.")]
        public decimal Price { get; set; }

        [Required(ErrorMessage = "La moneda es obligatoria.")]
        [StringLength(3, MinimumLength = 3, ErrorMessage = "La moneda debe tener 3 letras.")]
        public string Currency { get; set; } = "USD";
        public bool IsSubscription { get; set; }

        public static PackageInput FromEntity(TravelPackage package)
        {
            return new PackageInput
            {
                Id = package.Id,
                DestinationId = package.DestinationId,
                Name = package.Name,
                Slug = package.Slug,
                Description = package.Description,
                Price = package.Price,
                Currency = package.Currency,
                IsSubscription = package.IsSubscription
            };
        }

        public void ApplyTo(TravelPackage package, string normalizedSlug)
        {
            package.DestinationId = DestinationId;
            package.Name = (Name ?? string.Empty).Trim();
            package.Slug = normalizedSlug;
            package.Description = (Description ?? string.Empty).Trim();
            package.Price = Price;
            package.Currency = string.IsNullOrWhiteSpace(Currency)
                ? "USD"
                : Currency.Trim().ToUpperInvariant();
            package.IsSubscription = IsSubscription;
        }
    }
}

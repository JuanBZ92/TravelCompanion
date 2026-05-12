using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class UsersModel(TravelCompanionDbContext dbContext) : PageModel
{
    public List<UserRow> Users { get; private set; } = [];
    public List<EntitlementRow> Entitlements { get; private set; } = [];
    public List<SelectListItem> UserOptions { get; private set; } = [];
    public List<SelectListItem> DestinationOptions { get; private set; } = [];
    public List<SelectListItem> PackageOptions { get; private set; } = [];
    public List<SelectListItem> AccessLevelOptions { get; } = Enum.GetValues<ContentAccessLevel>()
        .Where(value => value != ContentAccessLevel.Free && value != ContentAccessLevel.AdminOnly)
        .Select(value => new SelectListItem(value.ToString(), value.ToString()))
        .ToList();

    [BindProperty]
    public UserForm UserInput { get; set; } = new();

    [BindProperty]
    public EntitlementForm EntitlementInput { get; set; } = new();

    public async Task OnGetAsync(Guid? editUserId)
    {
        await LoadPageDataAsync();

        if (editUserId.HasValue)
        {
            var user = await dbContext.AppUsers.FindAsync(editUserId.Value);
            if (user is not null)
            {
                UserInput = UserForm.FromEntity(user);
            }
        }

        SetDefaultEntitlementUser();
    }

    public async Task<IActionResult> OnPostSaveUserAsync()
    {
        ModelState.Remove($"{nameof(EntitlementInput)}.{nameof(EntitlementInput.UserId)}");
        ModelState.Remove($"{nameof(EntitlementInput)}.{nameof(EntitlementInput.Source)}");

        var normalizedEmail = (UserInput.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            ModelState.AddModelError(nameof(UserInput.Email), "El email es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(UserInput.DisplayName))
        {
            ModelState.AddModelError(nameof(UserInput.DisplayName), "El nombre visible es obligatorio.");
        }

        var duplicateExists = await dbContext.AppUsers.AnyAsync(user =>
            user.Email == normalizedEmail && user.Id != UserInput.Id);

        if (duplicateExists)
        {
            ModelState.AddModelError(nameof(UserInput.Email), "Ya existe un usuario con ese email.");
        }

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            SetDefaultEntitlementUser();
            return Page();
        }

        AppUser user;
        if (UserInput.Id.HasValue)
        {
            user = await dbContext.AppUsers.FindAsync(UserInput.Id.Value)
                ?? throw new InvalidOperationException("User not found.");
        }
        else
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = string.Empty,
                DisplayName = string.Empty
            };
            dbContext.AppUsers.Add(user);
        }

        UserInput.ApplyTo(user);
        await dbContext.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteUserAsync(Guid id)
    {
        var user = await dbContext.AppUsers.FindAsync(id);
        if (user is not null)
        {
            dbContext.AppUsers.Remove(user);
            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGrantEntitlementAsync()
    {
        ModelState.Remove($"{nameof(UserInput)}.{nameof(UserInput.Email)}");
        ModelState.Remove($"{nameof(UserInput)}.{nameof(UserInput.DisplayName)}");

        var userExists = await dbContext.AppUsers.AnyAsync(user => user.Id == EntitlementInput.UserId);
        if (!userExists)
        {
            ModelState.AddModelError(nameof(EntitlementInput.UserId), "Selecciona un usuario valido.");
        }

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        var destinationId = EntitlementInput.DestinationId;
        if (EntitlementInput.TravelPackageId.HasValue && !destinationId.HasValue)
        {
            destinationId = await dbContext.TravelPackages
                .Where(package => package.Id == EntitlementInput.TravelPackageId.Value)
                .Select(package => package.DestinationId)
                .FirstOrDefaultAsync();
        }

        var entitlement = new UserEntitlement
        {
            Id = Guid.NewGuid(),
            UserId = EntitlementInput.UserId,
            AccessLevel = EntitlementInput.AccessLevel,
            DestinationId = destinationId,
            TravelPackageId = EntitlementInput.TravelPackageId,
            GrantedAt = DateTimeOffset.UtcNow,
            ExpiresAt = EntitlementInput.ExpiresOn.HasValue
                ? new DateTimeOffset(EntitlementInput.ExpiresOn.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
                : null,
            Source = string.IsNullOrWhiteSpace(EntitlementInput.Source)
                ? "admin"
                : EntitlementInput.Source.Trim()
        };

        dbContext.UserEntitlements.Add(entitlement);
        await dbContext.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteEntitlementAsync(Guid id)
    {
        var entitlement = await dbContext.UserEntitlements.FindAsync(id);
        if (entitlement is not null)
        {
            dbContext.UserEntitlements.Remove(entitlement);
            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    private async Task LoadPageDataAsync()
    {
        Users = await dbContext.AppUsers
            .AsNoTracking()
            .Include(user => user.Entitlements)
            .OrderBy(user => user.Email)
            .Select(user => new UserRow(
                user.Id,
                user.Email,
                user.DisplayName,
                user.Entitlements.Count,
                user.Entitlements.Count(entitlement => entitlement.ExpiresAt == null || entitlement.ExpiresAt > DateTimeOffset.UtcNow)))
            .ToListAsync();

        Entitlements = await dbContext.UserEntitlements
            .AsNoTracking()
            .Include(entitlement => entitlement.User)
            .Include(entitlement => entitlement.Destination)
            .Include(entitlement => entitlement.TravelPackage)
            .OrderBy(entitlement => entitlement.User!.Email)
            .ThenBy(entitlement => entitlement.AccessLevel)
            .Select(entitlement => new EntitlementRow(
                entitlement.Id,
                entitlement.User != null ? entitlement.User.Email : "Unknown",
                entitlement.AccessLevel,
                entitlement.Destination != null ? entitlement.Destination.Name : "Global",
                entitlement.TravelPackage != null ? entitlement.TravelPackage.Name : "-",
                entitlement.GrantedAt,
                entitlement.ExpiresAt,
                entitlement.Source))
            .ToListAsync();

        UserOptions = Users
            .Select(user => new SelectListItem($"{user.DisplayName} ({user.Email})", user.Id.ToString()))
            .ToList();

        DestinationOptions = await dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .Select(destination => new SelectListItem(destination.Name, destination.Id.ToString()))
            .ToListAsync();
        DestinationOptions.Insert(0, new SelectListItem("Global", string.Empty));

        PackageOptions = await dbContext.TravelPackages
            .AsNoTracking()
            .Include(package => package.Destination)
            .OrderBy(package => package.Destination!.Name)
            .ThenBy(package => package.Name)
            .Select(package => new SelectListItem(
                $"{package.Name} ({(package.Destination != null ? package.Destination.Name : "Unknown")})",
                package.Id.ToString()))
            .ToListAsync();
        PackageOptions.Insert(0, new SelectListItem("Sin paquete especifico", string.Empty));
    }

    private void SetDefaultEntitlementUser()
    {
        if (EntitlementInput.UserId == Guid.Empty && UserOptions.Count > 0)
        {
            EntitlementInput.UserId = Guid.Parse(UserOptions[0].Value);
        }
    }

    public sealed record UserRow(
        Guid Id,
        string Email,
        string DisplayName,
        int EntitlementCount,
        int ActiveEntitlementCount);

    public sealed record EntitlementRow(
        Guid Id,
        string UserEmail,
        ContentAccessLevel AccessLevel,
        string DestinationName,
        string PackageName,
        DateTimeOffset GrantedAt,
        DateTimeOffset? ExpiresAt,
        string Source)
    {
        public string Status => ExpiresAt is not null && ExpiresAt <= DateTimeOffset.UtcNow
            ? "Expirado"
            : "Activo";
    }

    public sealed class UserForm
    {
        public Guid? Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public static UserForm FromEntity(AppUser user)
        {
            return new UserForm
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName
            };
        }

        public void ApplyTo(AppUser user)
        {
            user.Email = (Email ?? string.Empty).Trim().ToLowerInvariant();
            user.DisplayName = (DisplayName ?? string.Empty).Trim();
        }
    }

    public sealed class EntitlementForm
    {
        public Guid UserId { get; set; }
        public ContentAccessLevel AccessLevel { get; set; } = ContentAccessLevel.Paid;
        public Guid? DestinationId { get; set; }
        public Guid? TravelPackageId { get; set; }
        public DateOnly? ExpiresOn { get; set; }
        public string Source { get; set; } = "admin";
    }
}

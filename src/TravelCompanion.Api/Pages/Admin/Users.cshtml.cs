using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class UsersModel(
    TravelCompanionDbContext dbContext,
    IPasswordHasher<AppUser> passwordHasher,
    IUserInvitationSender invitationSender,
    UserSessionService sessionService) : PageModel
{
    public List<UserRow> Users { get; private set; } = [];
    public List<EntitlementRow> Entitlements { get; private set; } = [];
    public List<SelectListItem> UserOptions { get; private set; } = [];
    public List<SelectListItem> DestinationOptions { get; private set; } = [];
    public List<SelectListItem> PackageOptions { get; private set; } = [];
    public List<SelectListItem> AccessLevelOptions { get; } = ProductAccessModel.UserGrantOptions
        .Select(definition => new SelectListItem(definition.Label, definition.Level.ToString()))
        .ToList();

    [TempData]
    public string? StatusMessage { get; set; }

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
        ModelState.Remove($"{nameof(EntitlementInput)}.{nameof(EntitlementInput.AccessLevel)}");
        ModelState.Remove($"{nameof(EntitlementInput)}.{nameof(EntitlementInput.UserId)}");
        ModelState.Remove($"{nameof(EntitlementInput)}.{nameof(EntitlementInput.DestinationId)}");
        ModelState.Remove($"{nameof(EntitlementInput)}.{nameof(EntitlementInput.TravelPackageId)}");
        ModelState.Remove($"{nameof(EntitlementInput)}.{nameof(EntitlementInput.ExpiresOn)}");
        ModelState.Remove($"{nameof(EntitlementInput)}.{nameof(EntitlementInput.Source)}");

        var normalizedEmail = (UserInput.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            ModelState.AddModelError($"{nameof(UserInput)}.{nameof(UserInput.Email)}", "El email es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(UserInput.DisplayName))
        {
            ModelState.AddModelError($"{nameof(UserInput)}.{nameof(UserInput.DisplayName)}", "El nombre visible es obligatorio.");
        }

        var duplicateExists = await dbContext.AppUsers.AnyAsync(user =>
            user.Email == normalizedEmail && user.Id != UserInput.Id);

        if (duplicateExists)
        {
            ModelState.AddModelError($"{nameof(UserInput)}.{nameof(UserInput.Email)}", "Ya existe un usuario con ese email.");
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
                DisplayName = string.Empty,
                MustChangePassword = true
            };
            dbContext.AppUsers.Add(user);
        }

        var isNewUser = string.IsNullOrWhiteSpace(user.PasswordHash);
        UserInput.ApplyTo(user);
        string? temporaryPassword = null;
        if (isNewUser)
        {
            temporaryPassword = TemporaryPasswordGenerator.Create();
            user.PasswordHash = passwordHasher.HashPassword(user, temporaryPassword);
            user.MustChangePassword = true;
            user.TemporaryPasswordIssuedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync();

        if (temporaryPassword is not null)
        {
            await invitationSender.SendTemporaryPasswordAsync(user, temporaryPassword);
            StatusMessage = $"Usuario creado. Password temporal: {temporaryPassword}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(Guid id)
    {
        var user = await dbContext.AppUsers.FindAsync(id);
        if (user is not null)
        {
            var temporaryPassword = TemporaryPasswordGenerator.Create();
            user.PasswordHash = passwordHasher.HashPassword(user, temporaryPassword);
            user.MustChangePassword = true;
            user.TemporaryPasswordIssuedAt = DateTimeOffset.UtcNow;
            await sessionService.RevokeUserSessionsAsync(user.Id);
            await dbContext.SaveChangesAsync();
            await invitationSender.SendTemporaryPasswordAsync(user, temporaryPassword);
            StatusMessage = $"Password temporal para {user.Email}: {temporaryPassword}";
        }

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
            ModelState.AddModelError($"{nameof(EntitlementInput)}.{nameof(EntitlementInput.UserId)}", "Selecciona un usuario valido.");
        }

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        TravelPackage? selectedPackage = null;
        if (EntitlementInput.TravelPackageId.HasValue)
        {
            selectedPackage = await dbContext.TravelPackages.FindAsync(EntitlementInput.TravelPackageId.Value);
            if (selectedPackage is null)
            {
                ModelState.AddModelError($"{nameof(EntitlementInput)}.{nameof(EntitlementInput.TravelPackageId)}", "Selecciona un paquete valido.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        if (selectedPackage is null
            && EntitlementInput.AccessLevel == ContentAccessLevel.Subscription
            && !EntitlementInput.DestinationId.HasValue)
        {
            ModelState.AddModelError($"{nameof(EntitlementInput)}.{nameof(EntitlementInput.DestinationId)}", "Selecciona el destino de la suscripcion.");
        }

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        var destinationId = EntitlementInput.DestinationId ?? selectedPackage?.DestinationId;
        var accessLevel = selectedPackage is null
            ? EntitlementInput.AccessLevel
            : ContentAccessLevel.Paid;

        var entitlement = new UserEntitlement
        {
            Id = Guid.NewGuid(),
            UserId = EntitlementInput.UserId,
            AccessLevel = accessLevel,
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
                user.MustChangePassword,
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
        bool MustChangePassword,
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

        public string AccessLevelLabel => ProductAccessModel.GetLabel(AccessLevel);
    }

    public sealed class UserForm
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "El email es obligatorio.")]
        [EmailAddress(ErrorMessage = "Ingresa un email valido.")]
        [StringLength(256, ErrorMessage = "El email no puede superar 256 caracteres.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre visible es obligatorio.")]
        [StringLength(120, ErrorMessage = "El nombre visible no puede superar 120 caracteres.")]
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
        [Required(ErrorMessage = "Selecciona un usuario.")]
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "Selecciona un tipo de acceso.")]
        public ContentAccessLevel AccessLevel { get; set; } = ContentAccessLevel.Subscription;

        public Guid? DestinationId { get; set; }

        public Guid? TravelPackageId { get; set; }

        public DateOnly? ExpiresOn { get; set; }

        [StringLength(80, ErrorMessage = "El origen no puede superar 80 caracteres.")]
        public string? Source { get; set; } = "admin";
    }
}

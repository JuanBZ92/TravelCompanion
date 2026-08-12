using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(
    TravelCompanionDbContext dbContext,
    IPasswordHasher<AppUser> passwordHasher,
    IPasswordHasher<Trip> tripPinHasher,
    IPasswordHasher<BuilderAccessGrant> builderPinHasher,
    UserSessionService sessionService,
    FreePreviewAccountService freePreviewAccountService,
    IOptions<FreePreviewOptions> freePreviewOptions) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<AuthSessionDto>> Login(LoginRequestDto request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var password = request.Password;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return this.ValidationError(nameof(request.Email), "Email and password are required.");
        }

        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(existingUser => existingUser.Email == email, cancellationToken);

        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return Unauthorized();
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var (_, token) = await sessionService.CreateSessionAsync(user, cancellationToken);
        return Ok(ToSessionDto(user, token));
    }

    [HttpPost("pin-login")]
    [EnableRateLimiting("PinLogin")]
    public async Task<ActionResult<AuthSessionDto>> LoginWithPin(
        PinLoginRequestDto request,
        CancellationToken cancellationToken)
    {
        var pin = request.Pin.Trim();
        if ((pin.Length != 4 && pin.Length != 6) || pin.Any(character => !char.IsDigit(character)))
        {
            return this.ValidationError(nameof(request.Pin), "PIN must contain exactly 4 or 6 digits.");
        }

        if (freePreviewOptions.Value.Enabled
            && string.Equals(pin, freePreviewOptions.Value.Pin, StringComparison.Ordinal))
        {
            var previewAccount = await freePreviewAccountService.GetOrCreateAsync(cancellationToken);
            var lifetimeDays = Math.Clamp(freePreviewOptions.Value.SessionLifetimeDays, 1, 30);
            var (_, previewToken) = await sessionService.CreateSessionAsync(
                previewAccount,
                cancellationToken,
                accessMode: SessionAccessMode.FreeMapPreview,
                lifetime: TimeSpan.FromDays(lifetimeDays));
            return Ok(ToSessionDto(
                previewAccount,
                previewToken,
                mustChangePassword: false,
                accessMode: SessionAccessMode.FreeMapPreview,
                experienceMode: ExperienceMode.FreePreview,
                capabilities: TravelerAccessService.CreateCapabilities(ExperienceMode.FreePreview, false)));
        }

        if (pin.Length == 6)
        {
            var now = DateTimeOffset.UtcNow;
            var grants = await dbContext.BuilderAccessGrants
                .Include(grant => grant.AppUser)
                .Include(grant => grant.Destination)
                .Where(grant => grant.Status == BuilderAccessStatus.Active
                    && grant.RevokedAtUtc == null
                    && (!grant.ExpiresAtUtc.HasValue || grant.ExpiresAtUtc > now))
                .ToListAsync(cancellationToken);

            foreach (var grant in grants)
            {
                var verification = builderPinHasher.VerifyHashedPassword(grant, grant.PinHash, pin);
                if (verification == PasswordVerificationResult.Failed || grant.AppUser is null)
                {
                    continue;
                }

                if (verification == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    grant.PinHash = builderPinHasher.HashPassword(grant, pin);
                }

                grant.RedeemedAtUtc ??= now;
                await dbContext.SaveChangesAsync(cancellationToken);
                var (_, builderToken) = await sessionService.CreateSessionAsync(
                    grant.AppUser,
                    cancellationToken,
                    grant.TripId,
                    SessionAccessMode.Builder);
                return Ok(ToSessionDto(
                    grant.AppUser,
                    builderToken,
                    mustChangePassword: false,
                    grant.TripId,
                    grant.Destination?.Name,
                    SessionAccessMode.Builder,
                    ExperienceMode.SelfServiceBuilder,
                    TravelerAccessService.CreateCapabilities(ExperienceMode.SelfServiceBuilder, !grant.TripId.HasValue)));
            }

            return Unauthorized();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trips = await dbContext.Trips
            .Include(trip => trip.AppUser)
            .Include(trip => trip.Destination)
            .Where(trip =>
                trip.PublicationStatus == TripPublicationStatus.Published
                && trip.AppUserId != null
                && trip.AppUser != null
                && !string.IsNullOrWhiteSpace(trip.AccessPinHash))
            .OrderBy(trip => trip.StartsOn < today)
            .ThenBy(trip => trip.StartsOn)
            .ToListAsync(cancellationToken);

        foreach (var trip in trips)
        {
            var verification = tripPinHasher.VerifyHashedPassword(trip, trip.AccessPinHash!, pin);
            if (verification == PasswordVerificationResult.Failed || trip.AppUser is null)
            {
                continue;
            }

            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                trip.AccessPinHash = tripPinHasher.HashPassword(trip, pin);
                trip.AccessPinUpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var (_, token) = await sessionService.CreateSessionAsync(
                trip.AppUser,
                cancellationToken,
                trip.Id);
            return Ok(ToSessionDto(
                trip.AppUser,
                token,
                mustChangePassword: false,
                trip.Id,
                trip.Destination?.Name));
        }

        return Unauthorized();
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequestDto request, CancellationToken cancellationToken)
    {
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return this.ValidationError(nameof(request.NewPassword), "New password is required.");
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return Unauthorized();
        }

        if (!user.MustChangePassword && string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return this.ValidationError(nameof(request.CurrentPassword), "Current password is required.");
        }

        if (!user.MustChangePassword
            && passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword!)
                == PasswordVerificationResult.Failed)
        {
            return Unauthorized();
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sessionService.RevokeCurrentSessionAsync(HttpContext, cancellationToken);
        return NoContent();
    }

    private static AuthSessionDto ToSessionDto(
        AppUser user,
        string token,
        bool? mustChangePassword = null,
        Guid? tripId = null,
        string? destinationName = null,
        SessionAccessMode accessMode = SessionAccessMode.Trip,
        ExperienceMode experienceMode = ExperienceMode.CuratedPremium,
        TravelerCapabilitiesDto? capabilities = null)
    {
        return new AuthSessionDto(
            user.Id,
            user.Email,
            user.DisplayName,
            mustChangePassword ?? user.MustChangePassword,
            token,
            tripId,
            destinationName,
            accessMode,
            experienceMode,
            capabilities ?? TravelerAccessService.CreateCapabilities(experienceMode, false));
    }
}

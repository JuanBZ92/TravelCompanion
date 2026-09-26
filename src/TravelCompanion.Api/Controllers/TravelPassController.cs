using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/pass")]
public sealed class TravelPassController(
    TravelCompanionDbContext dbContext,
    UserSessionService sessionService,
    FreeTrialAccessService freeTrialAccessService,
    IPasswordHasher<BuilderAccessGrant> pinHasher,
    ILogger<TravelPassController> logger) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<TrialAccessStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSessionContextAsync(HttpContext, cancellationToken);
        return session is null
            ? Unauthorized()
            : Ok(await freeTrialAccessService.GetStatusAsync(session.User.Id, cancellationToken));
    }

    [HttpPost("redeem")]
    public async Task<ActionResult<AuthSessionDto>> Redeem(
        RedeemTravelPassRequest request,
        CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSessionContextAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return Unauthorized();
        }
        if (session.AccessMode != SessionAccessMode.FreeMapPreview)
        {
            return Conflict(new { message = "Este acceso ya tiene un pase activo." });
        }

        var trialGrant = await freeTrialAccessService.GetGrantAsync(session.User.Id, cancellationToken);
        if (freeTrialAccessService.ToStatus(trialGrant).State == TrialAccessState.Expired)
        {
            return StatusCode(StatusCodes.Status410Gone,
                new { message = "El borrador ya superó los 7 días de recuperación." });
        }
        if (trialGrant?.TripId is null)
        {
            return BadRequest(new { message = "Crea primero tu itinerario gratuito para conservarlo con el pase." });
        }

        var now = DateTimeOffset.UtcNow;
        var paidGrants = await dbContext.BuilderAccessGrants
            .Include(grant => grant.Destination)
            .Where(grant => !grant.IsTrial
                && grant.Status == BuilderAccessStatus.Active
                && grant.RevokedAtUtc == null
                && grant.TripId == null
                && (!grant.ExpiresAtUtc.HasValue || grant.ExpiresAtUtc > now))
            .ToListAsync(cancellationToken);
        var paidGrant = paidGrants.FirstOrDefault(grant =>
            !string.IsNullOrWhiteSpace(grant.PinHash)
            && pinHasher.VerifyHashedPassword(grant, grant.PinHash, request.Pin.Trim()) != PasswordVerificationResult.Failed);
        if (paidGrant is null || paidGrant.DestinationId != trialGrant.DestinationId)
        {
            return Unauthorized(new { message = "El código del pase no es válido para este viaje." });
        }

        var trip = await dbContext.Trips
            .SingleAsync(item => item.Id == trialGrant.TripId && item.AppUserId == session.User.Id, cancellationToken);
        trialGrant.TripId = null;
        trialGrant.ConvertedAtUtc = now;
        trialGrant.Status = BuilderAccessStatus.Revoked;
        trialGrant.RevokedAtUtc = now;
        paidGrant.AppUserId = session.User.Id;
        paidGrant.TripId = trip.Id;
        paidGrant.FreePolicy = trialGrant.FreePolicy;
        paidGrant.TrialEditingStartedAtUtc = trialGrant.TrialEditingStartedAtUtc;
        paidGrant.RedeemedAtUtc ??= now;
        var tripAccessEnd = new DateTimeOffset(
            trip.EndsOn.AddDays(8).ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);
        paidGrant.ExpiresAtUtc = new[] { tripAccessEnd, now.AddYears(1) }.Min();
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Travel pass activated from free trial. UserId={UserId}; TripId={TripId}; OrderReference={OrderReference}; ExpiresAtUtc={ExpiresAtUtc}.",
            session.User.Id,
            trip.Id,
            paidGrant.OrderReference,
            paidGrant.ExpiresAtUtc);

        await sessionService.RevokeCurrentSessionAsync(HttpContext, cancellationToken);
        var (_, token) = await sessionService.CreateSessionAsync(
            session.User,
            cancellationToken,
            trip.Id,
            SessionAccessMode.Builder,
            paidGrant.ExpiresAtUtc - now);
        var capabilities = TravelerAccessService.CreateCapabilities(ExperienceMode.SelfServiceBuilder, false);
        return Ok(new AuthSessionDto(
            session.User.Id,
            session.User.Email,
            session.User.DisplayName,
            false,
            token,
            trip.Id,
            paidGrant.Destination?.Name,
            SessionAccessMode.Builder,
            ExperienceMode.SelfServiceBuilder,
            capabilities,
            freeTrialAccessService.ToStatus(paidGrant),
            session.User.EmailVerified));
    }
}

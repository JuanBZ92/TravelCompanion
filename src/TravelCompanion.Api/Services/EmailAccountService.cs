using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class EmailAccountService(
    TravelCompanionDbContext dbContext,
    UserSessionService sessionService,
    ITransactionalEmailSender emailSender,
    IOptions<EmailVerificationOptions> options,
    ProductAnalyticsService? analytics = null,
    FreeTrialAccessService? freeTrialAccessService = null)
{
    public async Task<EmailCodeRequestedDto> RequestCodeAsync(
        HttpContext httpContext, RequestEmailCodeDto request, CancellationToken cancellationToken)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => RequestCodeAsync(httpContext, request, cancellationToken), cancellationToken);
        var email = NormalizeEmail(request.Email);
        var now = DateTimeOffset.UtcNow;
        var settings = options.Value;
        var requestIpHash = HashValue(httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await AcquireOtpLocksAsync(email, requestIpHash, cancellationToken);
        var latest = await dbContext.EmailVerificationChallenges
            .Where(item => item.Email == email && item.ConsumedAtUtc == null)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest?.ResendAvailableAtUtc > now)
        {
            return new(latest.ExpiresAtUtc, latest.ResendAvailableAtUtc);
        }

        var recentCount = await dbContext.EmailVerificationChallenges.CountAsync(
            item => item.Email == email && item.CreatedAtUtc > now.AddHours(-1), cancellationToken);
        var recentIpCount = await dbContext.EmailVerificationChallenges.CountAsync(
            item => item.RequestIpHash == requestIpHash && item.CreatedAtUtc > now.AddHours(-1), cancellationToken);
        if (recentCount >= Math.Clamp(settings.MaximumRequestsPerHour, 1, 20)
            || recentIpCount >= Math.Clamp(settings.MaximumRequestsPerHour, 1, 20))
        {
            throw new InvalidOperationException("Espera antes de solicitar otro código.");
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var challenge = new EmailVerificationChallenge
        {
            Id = Guid.NewGuid(),
            Email = email,
            CodeHash = HashCode(email, code, settings.HashSecret),
            RequestIpHash = requestIpHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(Math.Clamp(settings.LifetimeMinutes, 1, 30)),
            ResendAvailableAtUtc = now.AddSeconds(Math.Clamp(settings.ResendSeconds, 30, 300))
        };
        dbContext.EmailVerificationChallenges.Add(challenge);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await emailSender.SendVerificationCodeAsync(email, code, request.Locale ?? "es", cancellationToken);
        return new(challenge.ExpiresAtUtc, challenge.ResendAvailableAtUtc);
    }

    public async Task<AuthSessionDto> VerifyCodeAsync(
        HttpContext httpContext, VerifyEmailCodeDto request, CancellationToken cancellationToken)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => VerifyCodeAsync(httpContext, request, cancellationToken), cancellationToken);
        var source = await sessionService.GetSessionContextAsync(httpContext, cancellationToken);
        var email = NormalizeEmail(request.Email);
        var now = DateTimeOffset.UtcNow;
        var settings = options.Value;
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await AcquireOtpLocksAsync(email, null, cancellationToken);
        var challenge = await dbContext.EmailVerificationChallenges
            .Where(item => item.Email == email && item.ConsumedAtUtc == null)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("El código no es válido o ha caducado.");
        if (challenge.ExpiresAtUtc <= now || challenge.FailedAttempts >= Math.Clamp(settings.MaximumAttempts, 1, 10))
        {
            throw new InvalidOperationException("El código no es válido o ha caducado.");
        }
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(challenge.CodeHash),
                Convert.FromHexString(HashCode(email, request.Code, settings.HashSecret))))
        {
            challenge.FailedAttempts++;
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            throw new InvalidOperationException("El código no es válido o ha caducado.");
        }
        challenge.ConsumedAtUtc = now;
        var target = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Email == email && user.DeletedAtUtc == null, cancellationToken);
        if (target is null)
        {
            if (source is null)
                throw new InvalidOperationException("El código no es válido o ha caducado.");
            target = source.User;
            target.Email = email;
            target.DisplayName = email.Split('@')[0];
        }
        else if (source is not null && target.Id != source.User.Id && IsAnonymous(source.User))
        {
            var sourceTrips = await dbContext.Trips.Where(trip => trip.AppUserId == source.User.Id).ToListAsync(cancellationToken);
            foreach (var trip in sourceTrips) trip.AppUserId = target.Id;
            var sourceGrants = await dbContext.BuilderAccessGrants.Where(grant => grant.AppUserId == source.User.Id).ToListAsync(cancellationToken);
            foreach (var grant in sourceGrants) grant.AppUserId = target.Id;
            var sourcePurchaseIntents = await dbContext.StorePurchaseIntents
                .Where(item => item.AppUserId == source.User.Id).ToListAsync(cancellationToken);
            foreach (var intent in sourcePurchaseIntents) intent.AppUserId = target.Id;
            var sourceRoutes = await dbContext.ThematicRoutes
                .Where(item => item.AppUserId == source.User.Id).ToListAsync(cancellationToken);
            foreach (var route in sourceRoutes) route.AppUserId = target.Id;
            var sourceProposals = await dbContext.ItineraryProposals
                .Where(item => item.AppUserId == source.User.Id).ToListAsync(cancellationToken);
            foreach (var proposal in sourceProposals) proposal.AppUserId = target.Id;
            var sourceOperations = await dbContext.ItineraryOperations
                .Where(item => item.AppUserId == source.User.Id).ToListAsync(cancellationToken);
            foreach (var operation in sourceOperations) operation.AppUserId = target.Id;
            var sourceAssignments = await dbContext.ProductExperimentAssignments
                .Where(item => item.AppUserId == source.User.Id).ToListAsync(cancellationToken);
            var sourceExperiments = sourceAssignments.Select(item => item.Experiment).ToList();
            var targetAssignments = await dbContext.ProductExperimentAssignments
                .Where(item => item.AppUserId == target.Id && sourceExperiments.Contains(item.Experiment))
                .ToListAsync(cancellationToken);
            foreach (var assignment in sourceAssignments)
            {
                var targetAssignment = targetAssignments.SingleOrDefault(item => item.Experiment == assignment.Experiment);
                if (targetAssignment is null) assignment.AppUserId = target.Id;
                else
                {
                    targetAssignment.Variant = assignment.Variant;
                    targetAssignment.AssignedAtUtc = assignment.AssignedAtUtc;
                    dbContext.ProductExperimentAssignments.Remove(assignment);
                }
            }
        }
        if (source is not null && (target.Id == source.User.Id || IsAnonymous(source.User)))
        {
            var sourceEvents = await dbContext.ProductAnalyticsEvents.Where(item =>
                item.AnonymousUserId == source.User.Id || item.AppUserId == source.User.Id).ToListAsync(cancellationToken);
            foreach (var analyticsEvent in sourceEvents)
            {
                analyticsEvent.AppUserId = target.Id;
                analyticsEvent.AnonymousUserId = source.User.Id;
            }
        }
        target.EmailVerified = true;
        target.EmailVerifiedAtUtc = now;
        target.MustChangePassword = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        if (source is not null) await sessionService.RevokeUserSessionsAsync(source.User.Id, cancellationToken);
        if (source is null || target.Id != source.User.Id) await sessionService.RevokeUserSessionsAsync(target.Id, cancellationToken);
        var tripId = source?.TripId ?? await dbContext.Trips.Where(item => item.AppUserId == target.Id)
            .OrderByDescending(item => item.UpdatedAtUtc).Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
        var pass = tripId.HasValue ? await dbContext.BuilderAccessGrants.AsNoTracking().SingleOrDefaultAsync(grant =>
            grant.AppUserId == target.Id && grant.TripId == tripId, cancellationToken)
            : freeTrialAccessService is null ? null : await freeTrialAccessService.GetGrantAsync(target.Id, cancellationToken);
        var trial = pass?.IsTrial == true && freeTrialAccessService is not null
            ? await freeTrialAccessService.GetStatusAsync(pass, cancellationToken) : null;
        var mode = AccessGrantPolicy.ResolveSessionMode(pass, now);
        var (_, token) = await sessionService.CreateSessionAsync(target, cancellationToken, tripId, mode);
        if (analytics is not null)
            await analytics.RecordServerEventAsync(target.Id, tripId, "email_verification_completed", "account", null, cancellationToken);
        return new(target.Id, target.Email, target.DisplayName, false, token, tripId,
            AccessMode: mode, ExperienceMode: pass is not null ? ExperienceMode.SelfServiceBuilder : ExperienceMode.FreePreview,
            Capabilities: CreateAccountCapabilities(mode, trial, !tripId.HasValue), TrialAccess: trial, EmailVerified: true);
    }

    public async Task<TravelerAccountDto> GetAccountAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        var now = DateTimeOffset.UtcNow;
        var trips = await dbContext.Trips.AsNoTracking().Where(item => item.AppUserId == session.User.Id)
            .OrderByDescending(item => item.UpdatedAtUtc).ToListAsync(cancellationToken);
        var grants = await dbContext.BuilderAccessGrants.AsNoTracking().Where(item => item.AppUserId == session.User.Id)
            .ToListAsync(cancellationToken);
        return new(session.User.Id, session.User.Email, session.User.EmailVerified,
            trips.Select(trip =>
            {
                var grant = grants.SingleOrDefault(item => item.TripId == trip.Id);
                var state = AccessGrantPolicy.ResolveState(grant, now);
                var expiresAt = grant?.IsTrial == true ? grant.TrialEditingExpiresAtUtc : grant?.ExpiresAtUtc;
                return new AccountTripDto(trip.Id, trip.TravelerName, trip.StartsOn, trip.EndsOn, state, expiresAt, trip.IsArchived);
            }).ToList(), session.User.BehaviorAnalyticsConsent);
    }

    public async Task UpdateAnalyticsConsentAsync(HttpContext httpContext, bool granted, CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        session.User.BehaviorAnalyticsConsent = granted;
        await dbContext.SaveChangesAsync(cancellationToken);
        sessionService.InvalidateRequestCache(httpContext);
    }

    public async Task<AuthSessionDto> SelectTripAsync(HttpContext httpContext, Guid tripId, CancellationToken cancellationToken)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => SelectTripAsync(httpContext, tripId, cancellationToken), cancellationToken);
        var session = await sessionService.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await TripConcurrencyLock.LockAsync(dbContext, tripId, cancellationToken);
        var trip = await dbContext.Trips.Include(item => item.Destination).SingleOrDefaultAsync(item =>
            item.Id == tripId && item.AppUserId == session.User.Id, cancellationToken)
            ?? throw new KeyNotFoundException();
        if (trip.IsArchived)
        {
            trip.IsArchived = false;
            trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        var now = DateTimeOffset.UtcNow;
        var pass = await dbContext.BuilderAccessGrants.AsNoTracking().SingleOrDefaultAsync(grant => grant.TripId == trip.Id
            && grant.AppUserId == session.User.Id, cancellationToken);
        await sessionService.RevokeCurrentSessionAsync(httpContext, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        var mode = AccessGrantPolicy.ResolveSessionMode(pass, now);
        var (_, token) = await sessionService.CreateSessionAsync(session.User, cancellationToken, trip.Id, mode);
        var trial = pass?.IsTrial == true && freeTrialAccessService is not null
            ? await freeTrialAccessService.GetStatusAsync(pass, cancellationToken) : null;
        return new(session.User.Id, session.User.Email, session.User.DisplayName, false, token, trip.Id,
            trip.Destination?.Name, mode, pass is not null ? ExperienceMode.SelfServiceBuilder : ExperienceMode.FreePreview,
            CreateAccountCapabilities(mode, trial), TrialAccess: trial, EmailVerified: session.User.EmailVerified);
    }

    public async Task<AuthSessionDto> ArchiveTripAsync(HttpContext httpContext, Guid tripId, CancellationToken cancellationToken)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => ArchiveTripAsync(httpContext, tripId, cancellationToken), cancellationToken);
        var session = await sessionService.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await TripConcurrencyLock.LockAsync(dbContext, tripId, cancellationToken);
        var trip = await dbContext.Trips.SingleOrDefaultAsync(item =>
            item.Id == tripId && item.AppUserId == session.User.Id, cancellationToken)
            ?? throw new KeyNotFoundException();
        trip.IsArchived = true;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var boundSessions = await dbContext.AppUserSessions.Where(item => item.UserId == session.User.Id
            && item.TripId == tripId && item.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var boundSession in boundSessions)
        {
            boundSession.TripId = null;
            boundSession.AccessMode = SessionAccessMode.FreeMapPreview;
        }
        if (session.TripId == tripId)
            await sessionService.RevokeCurrentSessionAsync(httpContext, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        if (session.TripId == tripId)
        {
            var (_, token) = await sessionService.CreateSessionAsync(session.User, cancellationToken,
                accessMode: SessionAccessMode.FreeMapPreview);
            sessionService.InvalidateRequestCache(httpContext);
            return new(session.User.Id, session.User.Email, session.User.DisplayName, false, token,
                AccessMode: SessionAccessMode.FreeMapPreview, ExperienceMode: ExperienceMode.FreePreview,
                Capabilities: CreateAccountCapabilities(SessionAccessMode.FreeMapPreview),
                EmailVerified: session.User.EmailVerified);
        }
        sessionService.InvalidateRequestCache(httpContext);
        var currentToken = httpContext.Request.Headers.Authorization.ToString();
        currentToken = currentToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? currentToken[7..].Trim() : string.Empty;
        return new(session.User.Id, session.User.Email, session.User.DisplayName, false, currentToken,
            session.TripId, AccessMode: session.AccessMode, ExperienceMode: ExperienceMode.SelfServiceBuilder,
            Capabilities: CreateAccountCapabilities(session.AccessMode), EmailVerified: session.User.EmailVerified);
    }

    private static bool IsAnonymous(AppUser user) =>
        user.Email.StartsWith(FreePreviewAccountService.AccountEmailPrefix, StringComparison.OrdinalIgnoreCase)
        || user.Email == FreePreviewAccountService.AccountEmail;

    public async Task DeleteAccountAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
        {
            await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => DeleteAccountAsync(httpContext, cancellationToken), cancellationToken);
            return;
        }
        var session = await sessionService.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var user = await dbContext.AppUsers.SingleAsync(item => item.Id == session.User.Id, cancellationToken);
        var originalEmail = user.Email;
        var now = DateTimeOffset.UtcNow;
        user.DeletedAtUtc = now;
        user.EmailVerified = false;
        user.Email = $"deleted+{user.Id:N}@travelcompanion.invalid";
        user.DisplayName = "Deleted traveler";
        user.PasswordHash = null;
        user.BehaviorAnalyticsConsent = false;
        var trips = await dbContext.Trips.Where(item => item.AppUserId == user.Id).ToListAsync(cancellationToken);
        var tripIds = trips.Select(item => item.Id).ToList();
        foreach (var tripId in tripIds.Order())
            await TripConcurrencyLock.LockAsync(dbContext, tripId, cancellationToken);
        dbContext.Reservations.RemoveRange(await dbContext.Reservations.Where(item => tripIds.Contains(item.TripId)).ToListAsync(cancellationToken));
        dbContext.TravelDocuments.RemoveRange(await dbContext.TravelDocuments.Where(item => tripIds.Contains(item.TripId)).ToListAsync(cancellationToken));
        dbContext.ThematicRoutes.RemoveRange(await dbContext.ThematicRoutes.Where(item => item.AppUserId == user.Id).ToListAsync(cancellationToken));
        dbContext.ItineraryProposals.RemoveRange(await dbContext.ItineraryProposals.Where(item => item.AppUserId == user.Id).ToListAsync(cancellationToken));
        dbContext.ItineraryOperations.RemoveRange(await dbContext.ItineraryOperations.Where(item => item.AppUserId == user.Id).ToListAsync(cancellationToken));
        dbContext.TripPlanDrafts.RemoveRange(await dbContext.TripPlanDrafts.Where(item => tripIds.Contains(item.TripId)).ToListAsync(cancellationToken));
        dbContext.TripDayPlans.RemoveRange(await dbContext.TripDayPlans.Where(item => tripIds.Contains(item.TripId)).ToListAsync(cancellationToken));
        foreach (var trip in trips)
        {
            trip.TravelerName = "Deleted traveler";
            trip.BuilderSegmentsJson = null;
            trip.AccessPinHash = null;
            trip.AccessPinUpdatedAt = null;
            trip.ExternalId = null;
            trip.IsArchived = true;
            trip.UpdatedAtUtc = now;
        }
        var intents = await dbContext.StorePurchaseIntents.Where(item => item.AppUserId == user.Id).ToListAsync(cancellationToken);
        foreach (var intent in intents)
        {
            intent.DraftSnapshotJson = null;
            intent.ProtectedEvidence = null;
            if (intent.State is not PurchaseIntentState.Active and not PurchaseIntentState.Refunded)
            {
                intent.State = PurchaseIntentState.Cancelled;
                intent.ErrorCode = "account_deleted";
            }
        }
        var grants = await dbContext.BuilderAccessGrants.Where(item => item.AppUserId == user.Id).ToListAsync(cancellationToken);
        foreach (var grant in grants)
        {
            grant.Status = BuilderAccessStatus.Revoked;
            grant.RevokedAtUtc = now;
        }
        var analyticsEvents = await dbContext.ProductAnalyticsEvents
            .Where(item => item.AppUserId == user.Id || item.AnonymousUserId == user.Id).ToListAsync(cancellationToken);
        var behavioralEvents = analyticsEvents.Where(item => item.BehaviorConsent).ToList();
        dbContext.ProductAnalyticsEvents.RemoveRange(behavioralEvents);
        foreach (var analyticsEvent in analyticsEvents.Except(behavioralEvents))
        {
            analyticsEvent.AppUserId = null;
            analyticsEvent.AnonymousUserId = null;
            analyticsEvent.TripId = null;
        }
        dbContext.ProductExperimentAssignments.RemoveRange(await dbContext.ProductExperimentAssignments
            .Where(item => item.AppUserId == user.Id).ToListAsync(cancellationToken));
        var preference = await dbContext.TravelPreferenceProfiles.SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (preference is not null) dbContext.TravelPreferenceProfiles.Remove(preference);
        dbContext.TravelChatConversations.RemoveRange(await dbContext.TravelChatConversations
            .Where(item => item.UserId == user.Id).ToListAsync(cancellationToken));
        dbContext.RecommendationInteractionSignals.RemoveRange(await dbContext.RecommendationInteractionSignals
            .Where(item => item.UserId == user.Id).ToListAsync(cancellationToken));
        dbContext.NotificationDeviceRegistrations.RemoveRange(await dbContext.NotificationDeviceRegistrations
            .Where(item => item.UserId == user.Id).ToListAsync(cancellationToken));
        dbContext.NotificationOutboxItems.RemoveRange(await dbContext.NotificationOutboxItems
            .Where(item => item.UserId == user.Id).ToListAsync(cancellationToken));
        dbContext.EmailVerificationChallenges.RemoveRange(await dbContext.EmailVerificationChallenges
            .Where(item => item.Email == originalEmail).ToListAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await sessionService.RevokeUserSessionsAsync(user.Id, cancellationToken);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    private static TravelerCapabilitiesDto CreateAccountCapabilities(SessionAccessMode mode,
        TrialAccessStatusDto? trial = null, bool requiresSetup = false) => mode switch
    {
        SessionAccessMode.FreeMapPreview when trial?.IsTrial == true => new(false, true, trial.CanEdit, false, requiresSetup, false),
        SessionAccessMode.Builder => TravelerAccessService.CreateCapabilities(ExperienceMode.SelfServiceBuilder, false),
        SessionAccessMode.BuilderReadOnly => new(false, false, false, false, false, false),
        _ => TravelerAccessService.CreateCapabilities(ExperienceMode.FreePreview, false)
    };
    private static string HashCode(string email, string code, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) secret = "development-only-email-code-secret";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{email}:{code}")));
    }
    private static string HashValue(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private async Task AcquireOtpLocksAsync(string email, string? ipHash, CancellationToken cancellationToken)
    {
        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true
            || dbContext.Database.CurrentTransaction is null)
            return;
        var keys = new[] { $"otp:email:{email}", ipHash is null ? null : $"otp:ip:{ipHash}" }
            .Where(item => item is not null).Select(item => item!).Order(StringComparer.Ordinal);
        foreach (var key in keys)
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
}

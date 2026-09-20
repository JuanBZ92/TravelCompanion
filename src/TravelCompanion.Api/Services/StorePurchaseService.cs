using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed record VerifiedStorePurchase(
    bool IsValid,
    bool IsPending,
    string? TransactionId,
    string? OriginalTransactionId,
    string ProductId,
    DateTimeOffset PurchasedAtUtc,
    string? Currency = null,
    decimal? GrossAmount = null,
    string? ErrorCode = null,
    string? OpaqueAccountId = null,
    StoreEnvironment? VerifiedEnvironment = null,
    bool IsRevoked = false);

public interface IStoreReceiptVerifier
{
    StoreProvider Provider { get; }
    Task<VerifiedStorePurchase> VerifyAsync(string evidence, StoreEnvironment environment, CancellationToken cancellationToken);
}

public interface IStorePurchaseFinalizer
{
    StoreProvider Provider { get; }
    Task<bool> FinalizeAsync(string providerToken, string productId, CancellationToken cancellationToken);
}

public sealed class StorePurchaseService(
    TravelCompanionDbContext dbContext,
    UserSessionService sessionService,
    IEnumerable<IStoreReceiptVerifier> verifiers,
    IEnumerable<IStorePurchaseFinalizer> finalizers,
    IDataProtectionProvider dataProtectionProvider,
    ProductAnalyticsService analytics,
    IOptions<StorePurchaseOptions> options,
    AppleSignedDataVerifier? appleSignedDataVerifier = null)
{
    private readonly IDataProtector evidenceProtector = dataProtectionProvider.CreateProtector("TravelCompanion.StorePurchaseEvidence.v1");
    public async Task<PurchaseIntentDto> GetIntentAsync(HttpContext httpContext, Guid intentId, CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        var intent = await dbContext.StorePurchaseIntents.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == intentId && item.AppUserId == session.User.Id, cancellationToken)
            ?? throw new KeyNotFoundException();
        return ToDto(intent);
    }

    public async Task<PurchaseIntentDto> CancelIntentAsync(HttpContext httpContext, Guid intentId, CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        var intent = await dbContext.StorePurchaseIntents.SingleOrDefaultAsync(item =>
            item.Id == intentId && item.AppUserId == session.User.Id, cancellationToken)
            ?? throw new KeyNotFoundException();
        if (intent.State is PurchaseIntentState.Active or PurchaseIntentState.Refunded)
            throw new InvalidOperationException("Esta compra ya no se puede cancelar.");
        intent.State = PurchaseIntentState.Cancelled;
        intent.ErrorCode = "customer_cancelled";
        intent.LastAttemptAtUtc = DateTimeOffset.UtcNow;
        intent.ProtectedEvidence = null;
        intent.DraftSnapshotJson = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await analytics.RecordServerEventAsync(session.User.Id, intent.TripId, "checkout_cancelled",
            intent.EntryPoint.ToString(), intent.PaywallVariant, cancellationToken,
            platform: intent.Platform, appVersion: intent.AppVersion);
        return ToDto(intent);
    }

    public async Task<PurchaseIntentDto> CreateIntentAsync(HttpContext httpContext, CreatePurchaseIntentDto request, CancellationToken cancellationToken)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => CreateIntentAsync(httpContext, request, cancellationToken), cancellationToken);
        var session = await sessionService.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        if (!session.User.EmailVerified) throw new InvalidOperationException("Verifica tu email antes de comprar.");
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await TripConcurrencyLock.LockAsync(dbContext, request.TripId, cancellationToken);
        var trip = await dbContext.Trips.Include(item => item.Reservations)
            .Include(item => item.DayPlans).ThenInclude(item => item.Blocks)
            .Include(item => item.Documents).Include(item => item.PlanDraft)
            .Include(item => item.ThematicRoutes).ThenInclude(item => item.Stops)
            .SingleOrDefaultAsync(item => item.Id == request.TripId && item.AppUserId == session.User.Id, cancellationToken)
            ?? throw new KeyNotFoundException();
        var settings = options.Value;
        if (!settings.NewPurchasesEnabled) throw new InvalidOperationException("Las compras nuevas no están disponibles temporalmente.");
        var expectedProduct = request.Provider == StoreProvider.Apple ? settings.AppleProductId : settings.GoogleProductId;
        if (request.Provider == StoreProvider.AdminPin || request.ProductId != expectedProduct)
            throw new ArgumentException("El producto no corresponde a esta plataforma.");

        var now = DateTimeOffset.UtcNow;
        if (!IsTripWithinCoverage(trip, now.AddYears(1)))
            throw new InvalidOperationException("Las fechas del viaje quedan fuera de la cobertura máxima de un año.");
        var existing = await dbContext.StorePurchaseIntents
            .Where(item => item.AppUserId == session.User.Id && item.TripId == trip.Id
                && item.Provider == request.Provider && item.ProductId == expectedProduct
                && (item.State == PurchaseIntentState.AwaitingConfirmation || item.State == PurchaseIntentState.Pending || item.State == PurchaseIntentState.Verifying)
                && item.ExpiresAtUtc > now)
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return ToDto(existing);
        }

        var intent = new StorePurchaseIntent
        {
            Id = Guid.NewGuid(), AppUserId = session.User.Id, TripId = trip.Id,
            Provider = request.Provider, ProductId = expectedProduct,
            // Apple StoreKit's appAccountToken requires a UUID. The same opaque value also fits
            // Google Play's obfuscated account identifier limit.
            OpaqueAccountId = Guid.NewGuid().ToString("D"),
            State = PurchaseIntentState.AwaitingConfirmation, EntryPoint = request.EntryPoint,
            PaywallVariant = request.PaywallVariant.Trim(), CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(24),
            AppVersion = request.AppVersion, Platform = request.Platform,
            DraftSnapshotJson = TripDraftSnapshotCodec.Capture(trip)
        };
        dbContext.StorePurchaseIntents.Add(intent);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await analytics.RecordServerEventAsync(session.User.Id, trip.Id, "checkout_started", request.EntryPoint.ToString(), request.PaywallVariant,
            cancellationToken, platform: request.Platform, appVersion: request.AppVersion);
        return ToDto(intent);
    }

    public async Task<PassAccessDto> VerifyAsync(HttpContext httpContext, VerifyPurchaseDto request, CancellationToken cancellationToken)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => VerifyAsync(httpContext, request, cancellationToken), cancellationToken);
        var session = await sessionService.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        var intent = await dbContext.StorePurchaseIntents.Include(item => item.Trip)
            .SingleOrDefaultAsync(item => item.Id == request.IntentId && item.AppUserId == session.User.Id, cancellationToken)
            ?? throw new KeyNotFoundException();
        if (intent.State == PurchaseIntentState.Active)
            return await GetPassAsync(session.User.Id, intent.TripId, cancellationToken);
        if (intent.State is PurchaseIntentState.Cancelled or PurchaseIntentState.Failed or PurchaseIntentState.Refunded)
            throw new InvalidOperationException("Esta intención de compra ya no admite confirmación.");
        intent.State = PurchaseIntentState.Verifying;
        intent.LastAttemptAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var verifier = verifiers.Single(item => item.Provider == intent.Provider);
        VerifiedStorePurchase result;
        try
        {
            result = await verifier.VerifyAsync(request.Evidence, request.Environment, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or TaskCanceledException or CryptographicException)
        {
            intent.State = PurchaseIntentState.Pending;
            intent.ErrorCode = "verification_retry";
            intent.Environment = request.Environment;
            intent.ProtectedEvidence = evidenceProtector.Protect(request.Evidence);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new(intent.TripId, TrialAccessState.PurchasePending, intent.Provider, null, null, null, 0);
        }
        var verifiedEnvironment = result.VerifiedEnvironment ?? request.Environment;
        if (result.IsPending)
        {
            intent.State = PurchaseIntentState.Pending;
            intent.Environment = verifiedEnvironment;
            intent.ProtectedEvidence = evidenceProtector.Protect(request.Evidence);
            await dbContext.SaveChangesAsync(cancellationToken);
            await analytics.RecordServerEventAsync(session.User.Id, intent.TripId, "checkout_pending", intent.EntryPoint.ToString(), intent.PaywallVariant,
                cancellationToken, platform: intent.Platform, appVersion: intent.AppVersion);
            return new(intent.TripId, TrialAccessState.PurchasePending, intent.Provider, null, null, null, 0);
        }
        if (result.IsRevoked)
        {
            intent.State = PurchaseIntentState.Refunded;
            intent.ErrorCode = result.ErrorCode ?? "purchase_revoked";
            intent.ProtectedEvidence = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("La tienda ha revocado esta compra.");
        }
        if (!result.IsValid || string.IsNullOrWhiteSpace(result.TransactionId) || result.ProductId != intent.ProductId
            || !string.Equals(result.OpaqueAccountId, intent.OpaqueAccountId, StringComparison.OrdinalIgnoreCase))
        {
            intent.State = PurchaseIntentState.Failed;
            intent.ErrorCode = result.IsValid && result.ProductId == intent.ProductId
                ? "purchase_account_mismatch" : result.ErrorCode ?? "invalid_evidence";
            intent.ProtectedEvidence = null;
            intent.DraftSnapshotJson = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            await analytics.RecordServerEventAsync(session.User.Id, intent.TripId, "checkout_failed",
                intent.EntryPoint.ToString(), intent.PaywallVariant, cancellationToken,
                verifiedEnvironment == StoreEnvironment.Sandbox, intent.Platform, intent.AppVersion);
            throw new InvalidOperationException("La tienda no pudo verificar esta compra.");
        }
        if (!WasPurchasedWithinIntentWindow(intent, result))
        {
            intent.State = PurchaseIntentState.Failed;
            intent.ErrorCode = "purchase_outside_intent_window";
            intent.ProtectedEvidence = null;
            intent.DraftSnapshotJson = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("La compra se inició fuera de la vigencia de esta intención.");
        }

        return await ActivateVerifiedPurchaseAsync(intent, session.User.Id, result, verifiedEnvironment, request.Evidence, cancellationToken);
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
        {
            await DbExecutionStrategy.ExecuteAsync(dbContext, () => ReconcileAsync(cancellationToken), cancellationToken);
            return;
        }
        var pending = await dbContext.StorePurchaseIntents.Include(item => item.Trip)
            .Where(item => item.State == PurchaseIntentState.Pending && item.ProtectedEvidence != null)
            .OrderBy(item => item.LastAttemptAtUtc).Take(25).ToListAsync(cancellationToken);
        foreach (var intent in pending)
        {
            try
            {
                var evidence = evidenceProtector.Unprotect(intent.ProtectedEvidence!);
                var environment = intent.Environment ?? StoreEnvironment.Production;
                var result = await verifiers.Single(item => item.Provider == intent.Provider)
                    .VerifyAsync(evidence, environment, cancellationToken);
                intent.LastAttemptAtUtc = DateTimeOffset.UtcNow;
                intent.RetryCount++;
                if (result.IsRevoked)
                {
                    intent.State = PurchaseIntentState.Refunded;
                    intent.ErrorCode = result.ErrorCode ?? "purchase_revoked";
                    intent.ProtectedEvidence = null;
                    intent.DraftSnapshotJson = null;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                else if (result.IsValid && !string.IsNullOrWhiteSpace(result.TransactionId)
                    && result.ProductId == intent.ProductId
                    && string.Equals(result.OpaqueAccountId, intent.OpaqueAccountId, StringComparison.OrdinalIgnoreCase)
                    && WasPurchasedWithinIntentWindow(intent, result))
                    await ActivateVerifiedPurchaseAsync(intent, intent.AppUserId, result,
                        result.VerifiedEnvironment ?? environment, evidence, cancellationToken);
                else
                {
                    if (result.IsValid && result.ProductId == intent.ProductId
                        && string.Equals(result.OpaqueAccountId, intent.OpaqueAccountId, StringComparison.OrdinalIgnoreCase)
                        && !WasPurchasedWithinIntentWindow(intent, result))
                    {
                        intent.State = PurchaseIntentState.Failed;
                        intent.ErrorCode = "purchase_outside_intent_window";
                        intent.ProtectedEvidence = null;
                        intent.DraftSnapshotJson = null;
                    }
                    if (result.IsValid && result.ProductId == intent.ProductId)
                    {
                        if (!string.Equals(result.OpaqueAccountId, intent.OpaqueAccountId, StringComparison.OrdinalIgnoreCase))
                        {
                            intent.State = PurchaseIntentState.Failed;
                            intent.ErrorCode = "purchase_account_mismatch";
                            intent.ProtectedEvidence = null;
                            intent.DraftSnapshotJson = null;
                        }
                    }
                    if (result.ErrorCode == "google_purchase_cancelled")
                    {
                        intent.State = PurchaseIntentState.Cancelled;
                        intent.ErrorCode = result.ErrorCode;
                        intent.ProtectedEvidence = null;
                        intent.DraftSnapshotJson = null;
                    }
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or CryptographicException or JsonException or InvalidOperationException)
            {
                intent.RetryCount++;
                intent.ErrorCode = "reconciliation_retry";
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        var unfinalized = await dbContext.StorePurchaseTransactions
            .Where(item => !item.AcknowledgedOrConsumed && item.ProtectedProviderToken != null)
            .OrderBy(item => item.VerifiedAtUtc).Take(25).ToListAsync(cancellationToken);
        foreach (var transaction in unfinalized)
            await TryFinalizeAsync(transaction, cancellationToken);
    }

    public async Task<bool> ProcessProviderNotificationAsync(StoreProvider provider, StoreEnvironment environment,
        string payload, CancellationToken cancellationToken)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => ProcessProviderNotificationAsync(provider, environment, payload, cancellationToken), cancellationToken);
        var evidence = ExtractNotificationEvidence(provider, payload);
        if (evidence is null)
            return true;

        var verifier = verifiers.Single(item => item.Provider == provider);
        var result = await verifier.VerifyAsync(evidence, environment, cancellationToken);
        if (result.IsPending)
            return false;
        if (!result.IsValid || result.IsRevoked || string.IsNullOrWhiteSpace(result.TransactionId))
            return result.ErrorCode is not ("verification_retry" or "apple_server_api_not_configured" or "google_verifier_not_configured");

        var verifiedEnvironment = result.VerifiedEnvironment ?? environment;
        var existingTransaction = await dbContext.StorePurchaseTransactions.AsNoTracking().AnyAsync(item =>
            item.Provider == provider && item.Environment == verifiedEnvironment
            && item.ProviderTransactionId == result.TransactionId, cancellationToken);
        if (existingTransaction)
            return true;
        if (string.IsNullOrWhiteSpace(result.OpaqueAccountId))
            return true;

        var intent = await dbContext.StorePurchaseIntents.Include(item => item.Trip)
            .Where(item => item.Provider == provider && item.ProductId == result.ProductId
                && item.OpaqueAccountId == result.OpaqueAccountId
                && item.State != PurchaseIntentState.Active
                && item.State != PurchaseIntentState.Refunded
                && item.State != PurchaseIntentState.Cancelled
                && item.State != PurchaseIntentState.Failed)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (intent is null)
            return false;
        if (!WasPurchasedWithinIntentWindow(intent, result))
        {
            intent.State = PurchaseIntentState.Failed;
            intent.ErrorCode = "purchase_outside_intent_window";
            intent.ProtectedEvidence = null;
            intent.DraftSnapshotJson = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        intent.State = PurchaseIntentState.Verifying;
        intent.Environment = verifiedEnvironment;
        intent.LastAttemptAtUtc = DateTimeOffset.UtcNow;
        intent.ProtectedEvidence = evidenceProtector.Protect(evidence);
        await dbContext.SaveChangesAsync(cancellationToken);
        await ActivateVerifiedPurchaseAsync(intent, intent.AppUserId, result, verifiedEnvironment, evidence, cancellationToken);
        return true;
    }

    private string? ExtractNotificationEvidence(StoreProvider provider, string payload)
    {
        if (provider == StoreProvider.Apple)
        {
            if (appleSignedDataVerifier is null
                || !appleSignedDataVerifier.TryReadVerifiedPayload(payload, out var notification))
                throw new CryptographicException("The Apple notification signature is invalid.");
            if (!notification.TryGetProperty("data", out var data)
                || !data.TryGetProperty("signedTransactionInfo", out var transaction))
                return null;
            return transaction.GetString();
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("oneTimeProductNotification", out var purchase)
            || !purchase.TryGetProperty("purchaseToken", out var token))
            return null;
        var purchaseToken = token.GetString();
        return string.IsNullOrWhiteSpace(purchaseToken)
            ? null
            : JsonSerializer.Serialize(new { purchaseToken });
    }

    private async Task<PassAccessDto> ActivateVerifiedPurchaseAsync(StorePurchaseIntent intent, Guid userId,
        VerifiedStorePurchase result, StoreEnvironment environment, string evidence, CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        await TripConcurrencyLock.LockAsync(dbContext, intent.TripId, cancellationToken);
        await LockProviderTransactionAsync(intent.Provider, environment, result.TransactionId!, cancellationToken);
        await dbContext.Entry(intent).ReloadAsync(cancellationToken);
        if (intent.State == PurchaseIntentState.Active)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return await GetPassAsync(userId, intent.TripId, cancellationToken);
        }
        intent.Trip = await dbContext.Trips.SingleAsync(item => item.Id == intent.TripId, cancellationToken);
        var evidenceFingerprint = FingerprintEvidence(intent.Provider, evidence);
        var revocation = await dbContext.StoreRevocationMarkers.FirstOrDefaultAsync(item =>
            item.Provider == intent.Provider && item.Environment == environment
            && (item.ProviderTransactionId == result.TransactionId
                || item.EvidenceFingerprint != null && item.EvidenceFingerprint == evidenceFingerprint), cancellationToken);
        if (revocation is not null)
        {
            var revokedTransaction = new StorePurchaseTransaction
            {
                Id = Guid.NewGuid(), PurchaseIntentId = intent.Id, Provider = intent.Provider, Environment = environment,
                ProviderTransactionId = result.TransactionId!, ProviderOriginalTransactionId = result.OriginalTransactionId,
                ProductId = result.ProductId, Currency = result.Currency, GrossAmount = result.GrossAmount,
                PurchasedAtUtc = result.PurchasedAtUtc, VerifiedAtUtc = DateTimeOffset.UtcNow,
                RevokedAtUtc = revocation.ReceivedAtUtc, RevocationReason = revocation.Reason,
                EvidenceFingerprint = evidenceFingerprint, AcknowledgedOrConsumed = intent.Provider == StoreProvider.Apple
            };
            dbContext.StorePurchaseTransactions.Add(revokedTransaction);
            revocation.AppliedAtUtc = DateTimeOffset.UtcNow;
            intent.State = PurchaseIntentState.Refunded;
            intent.ErrorCode = "purchase_revoked_before_activation";
            intent.ProtectedEvidence = null;
            intent.DraftSnapshotJson = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            await analytics.RecordServerEventAsync(userId, intent.TripId, "refund", revocation.Reason,
                intent.PaywallVariant, cancellationToken, environment == StoreEnvironment.Sandbox,
                intent.Platform, intent.AppVersion);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            throw new InvalidOperationException("La tienda revocó esta compra antes de activarla.");
        }
        var duplicate = await dbContext.StorePurchaseTransactions.FirstOrDefaultAsync(item =>
            item.Provider == intent.Provider && item.Environment == environment
            && item.ProviderTransactionId == result.TransactionId, cancellationToken);
        if (duplicate is not null && duplicate.PurchaseIntentId != intent.Id)
            throw new InvalidOperationException("Esta transacción ya está vinculada a otra compra.");
        if (intent.Trip.IsArchived)
            await TripDraftSnapshotCodec.RestoreAsync(dbContext, intent.Trip, intent.DraftSnapshotJson, cancellationToken);
        var storeTransaction = duplicate ?? new StorePurchaseTransaction
        {
            Id = Guid.NewGuid(), PurchaseIntentId = intent.Id, Provider = intent.Provider,
            Environment = environment, ProviderTransactionId = result.TransactionId!,
            ProviderOriginalTransactionId = result.OriginalTransactionId, ProductId = result.ProductId,
            Currency = result.Currency, GrossAmount = result.GrossAmount, PurchasedAtUtc = result.PurchasedAtUtc,
            VerifiedAtUtc = DateTimeOffset.UtcNow, EvidenceFingerprint = evidenceFingerprint,
            ProtectedProviderToken = intent.Provider == StoreProvider.Google
                ? evidenceProtector.Protect(ExtractGoogleToken(evidence)) : null,
            AcknowledgedOrConsumed = intent.Provider == StoreProvider.Apple
        };
        if (duplicate is null) dbContext.StorePurchaseTransactions.Add(storeTransaction);
        var grant = await dbContext.BuilderAccessGrants.SingleOrDefaultAsync(item => item.TripId == intent.TripId, cancellationToken);
        grant ??= new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = userId, DestinationId = intent.Trip!.DestinationId,
            TripId = intent.TripId, CreatedAtUtc = DateTimeOffset.UtcNow, PinHash = null
        };
        if (dbContext.Entry(grant).State == EntityState.Detached) dbContext.BuilderAccessGrants.Add(grant);
        if (duplicate is null && !grant.IsTrial && grant.Status == BuilderAccessStatus.Active
            && grant.PurchaseTransactionId.HasValue && grant.PurchaseTransactionId != storeTransaction.Id)
        {
            storeTransaction.RevocationReason = "duplicate_trip_purchase_review";
            intent.State = PurchaseIntentState.Active;
            intent.ErrorCode = "duplicate_trip_purchase_review";
            await dbContext.SaveChangesAsync(cancellationToken);
            await analytics.RecordServerEventAsync(userId, intent.TripId, "duplicate_trip_purchase",
                intent.EntryPoint.ToString(), intent.PaywallVariant, cancellationToken, environment == StoreEnvironment.Sandbox,
                intent.Platform, intent.AppVersion);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return await GetPassAsync(userId, intent.TripId, cancellationToken);
        }
        var maximum = result.PurchasedAtUtc.AddYears(1);
        grant.AppUserId = userId; grant.IsTrial = false; grant.Status = BuilderAccessStatus.Active;
        grant.Origin = intent.Provider; grant.PurchaseTransactionId = storeTransaction.Id;
        grant.PurchasedAtUtc = result.PurchasedAtUtc; grant.MaximumExpiresAtUtc = maximum;
        grant.ExpiresAtUtc = CalculateExpiry(intent.Trip!, maximum); grant.RedeemedAtUtc ??= DateTimeOffset.UtcNow;
        grant.ConvertedAtUtc ??= DateTimeOffset.UtcNow; grant.RevokedAtUtc = null;
        intent.State = PurchaseIntentState.Active; intent.ErrorCode = null; intent.ProtectedEvidence = null;
        intent.DraftSnapshotJson = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await analytics.RecordServerEventAsync(userId, intent.TripId, "purchase_verified", intent.EntryPoint.ToString(),
            intent.PaywallVariant, cancellationToken, environment == StoreEnvironment.Sandbox, intent.Platform, intent.AppVersion);
        await analytics.RecordServerEventAsync(userId, intent.TripId, "pass_activated", intent.EntryPoint.ToString(),
            intent.PaywallVariant, cancellationToken, environment == StoreEnvironment.Sandbox, intent.Platform, intent.AppVersion);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await TryFinalizeAsync(storeTransaction, cancellationToken);
        return new(intent.TripId, TrialAccessState.Paid, intent.Provider, result.PurchasedAtUtc, grant.ExpiresAtUtc,
            maximum, Math.Clamp(options.Value.DailyAssistantLimit, 1, 100));
    }

    private async Task TryFinalizeAsync(StorePurchaseTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.AcknowledgedOrConsumed || transaction.ProtectedProviderToken is null) return;
        var finalizer = finalizers.SingleOrDefault(item => item.Provider == transaction.Provider);
        if (finalizer is null) return;
        try
        {
            transaction.AcknowledgedOrConsumed = await finalizer.FinalizeAsync(
                evidenceProtector.Unprotect(transaction.ProtectedProviderToken), transaction.ProductId, cancellationToken);
            if (transaction.AcknowledgedOrConsumed) await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or CryptographicException or JsonException)
        {
            // A later reconciliation attempt uses the protected token.
        }
    }

    public async Task<IReadOnlyList<PassAccessDto>> RestoreAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        var passes = await dbContext.BuilderAccessGrants.AsNoTracking()
            .Where(item => item.AppUserId == session.User.Id && !item.IsTrial)
            .Select(item => new PassAccessDto(item.TripId!.Value,
                item.Status == BuilderAccessStatus.Active && item.ExpiresAtUtc > DateTimeOffset.UtcNow ? TrialAccessState.Paid
                    : item.Status == BuilderAccessStatus.Revoked ? TrialAccessState.Revoked : TrialAccessState.Expired,
                item.Origin, item.PurchasedAtUtc, item.ExpiresAtUtc, item.MaximumExpiresAtUtc,
                Math.Clamp(options.Value.DailyAssistantLimit, 1, 100)))
            .ToListAsync(cancellationToken);
        if (passes.Count > 0)
            await analytics.RecordServerEventAsync(session.User.Id, session.TripId ?? passes[0].TripId,
                "purchase_restored", "account", null, cancellationToken);
        return passes;
    }

    public async Task<PassAccessDto> GetPassAsync(Guid userId, Guid tripId, CancellationToken cancellationToken)
    {
        var grant = await dbContext.BuilderAccessGrants.AsNoTracking().SingleAsync(item => item.AppUserId == userId && item.TripId == tripId, cancellationToken);
        var state = grant.Status == BuilderAccessStatus.Active && grant.ExpiresAtUtc > DateTimeOffset.UtcNow
            ? TrialAccessState.Paid : grant.Status == BuilderAccessStatus.Revoked ? TrialAccessState.Revoked : TrialAccessState.Expired;
        return new(tripId, state, grant.Origin, grant.PurchasedAtUtc, grant.ExpiresAtUtc, grant.MaximumExpiresAtUtc,
            Math.Clamp(options.Value.DailyAssistantLimit, 1, 100));
    }

    public static DateTimeOffset CalculateExpiry(Trip trip, DateTimeOffset maximum)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(trip.TimeZoneId); }
        catch { zone = TimeZoneInfo.Utc; }
        var local = DateTime.SpecifyKind(trip.EndsOn.AddDays(8).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var offset = zone.GetUtcOffset(local);
        var tripExpiry = new DateTimeOffset(local, offset).ToUniversalTime();
        return tripExpiry < maximum ? tripExpiry : maximum;
    }

    public static bool IsTripWithinCoverage(Trip trip, DateTimeOffset maximum)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(trip.TimeZoneId); }
        catch { zone = TimeZoneInfo.Utc; }
        var local = DateTime.SpecifyKind(trip.EndsOn.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime() <= maximum;
    }

    private static PurchaseIntentDto ToDto(StorePurchaseIntent intent) =>
        new(intent.Id, intent.TripId, intent.Provider, intent.ProductId, intent.OpaqueAccountId,
            intent.State, intent.CreatedAtUtc, intent.ExpiresAtUtc, intent.ErrorCode);

    private static bool WasPurchasedWithinIntentWindow(StorePurchaseIntent intent, VerifiedStorePurchase purchase)
    {
        if (purchase.PurchasedAtUtc == default) return false;
        var earliest = intent.CreatedAtUtc.AddMinutes(-10);
        return purchase.PurchasedAtUtc >= earliest;
    }

    private static string FingerprintEvidence(StoreProvider provider, string evidence)
    {
        var value = evidence;
        if (provider == StoreProvider.Google && evidence.TrimStart().StartsWith('{'))
        {
            using var document = JsonDocument.Parse(evidence);
            value = document.RootElement.TryGetProperty("purchaseToken", out var token) ? token.GetString() ?? evidence : evidence;
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string ExtractGoogleToken(string evidence)
    {
        if (!evidence.TrimStart().StartsWith('{')) return evidence.Trim();
        using var document = JsonDocument.Parse(evidence);
        return document.RootElement.GetProperty("purchaseToken").GetString()
            ?? throw new InvalidOperationException("La tienda no devolvió un token válido.");
    }

    private async Task LockProviderTransactionAsync(StoreProvider provider, StoreEnvironment environment,
        string transactionId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true
            || dbContext.Database.CurrentTransaction is null)
            return;
        var key = $"store:{provider}:{environment}:{transactionId}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
}

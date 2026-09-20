using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
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
[Route("api/providers/store-notifications")]
public sealed class StoreNotificationsController(
    TravelCompanionDbContext dbContext,
    IHttpClientFactory clients,
    IOptions<StorePurchaseOptions> options,
    AppleSignedDataVerifier appleSignedDataVerifier,
    StorePurchaseService purchaseService,
    ProductAnalyticsService analytics,
    UserSessionService sessions,
    IDataProtectionProvider dataProtectionProvider) : ControllerBase
{
    private readonly IDataProtector payloadProtector = dataProtectionProvider.CreateProtector("TravelCompanion.StoreNotificationPayload.v1");
    [HttpPost("apple")]
    public async Task<ActionResult> Apple(JsonElement body, CancellationToken ct)
    {
        if (!body.TryGetProperty("signedPayload", out var signedNode)
            || !appleSignedDataVerifier.TryReadVerifiedPayload(signedNode.GetString() ?? string.Empty, out var payload))
            return Unauthorized();
        var id = payload.TryGetProperty("notificationUUID", out var idNode) ? idNode.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var environment = payload.TryGetProperty("data", out var data)
            && data.TryGetProperty("environment", out var env) && env.GetString() == "Sandbox"
            ? StoreEnvironment.Sandbox : StoreEnvironment.Production;
        var receipt = await PersistReceiptAsync(StoreProvider.Apple, environment, id,
            signedNode.GetString() ?? string.Empty, ct);
        if (receipt.ProcessedAtUtc.HasValue) return Ok();
        var type = payload.TryGetProperty("notificationType", out var typeNode) ? typeNode.GetString() : null;
        var revocationHandled = false;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("signedTransactionInfo", out var transactionNode)
            && appleSignedDataVerifier.TryReadVerifiedPayload(transactionNode.GetString() ?? string.Empty, out var transaction))
        {
            var transactionId = transaction.TryGetProperty("transactionId", out var txNode) ? txNode.GetString() : null;
            if (!string.IsNullOrWhiteSpace(transactionId) && type is "REFUND" or "REVOKE")
            {
                await RevokeAsync(StoreProvider.Apple, environment, transactionId, type, ct);
                revocationHandled = true;
            }
        }
        var handled = type is "REFUND" or "REVOKE" ? revocationHandled
            : await purchaseService.ProcessProviderNotificationAsync(StoreProvider.Apple, environment,
                signedNode.GetString() ?? string.Empty, ct);
        if (handled) receipt.ProcessedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost("google")]
    public async Task<ActionResult> Google(JsonElement body, CancellationToken ct)
    {
        if (!await ValidateGoogleOidcAsync(ct)) return Unauthorized();
        if (!body.TryGetProperty("message", out var message)
            || !message.TryGetProperty("messageId", out var idNode)
            || !message.TryGetProperty("data", out var dataNode)) return BadRequest();
        var id = idNode.GetString(); if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var raw = Encoding.UTF8.GetString(Convert.FromBase64String(dataNode.GetString() ?? string.Empty));
        var receipt = await PersistReceiptAsync(StoreProvider.Google, StoreEnvironment.Production, id, raw, ct);
        if (receipt.ProcessedAtUtc.HasValue) return Ok();
        using var notification = JsonDocument.Parse(raw);
        var handled = false;
        if (notification.RootElement.TryGetProperty("oneTimeProductNotification", out _))
        {
            handled = await purchaseService.ProcessProviderNotificationAsync(
                StoreProvider.Google, StoreEnvironment.Production, raw, ct);
        }
        else if (notification.RootElement.TryGetProperty("voidedPurchaseNotification", out var voided))
        {
            var orderId = voided.TryGetProperty("orderId", out var orderNode) ? orderNode.GetString() : null;
            var token = voided.TryGetProperty("purchaseToken", out var tokenNode) ? tokenNode.GetString() : null;
            var fingerprint = string.IsNullOrWhiteSpace(token) ? null
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            var transaction = await dbContext.StorePurchaseTransactions.FirstOrDefaultAsync(item =>
                item.Provider == StoreProvider.Google && (item.ProviderTransactionId == orderId
                    || (fingerprint != null && item.EvidenceFingerprint == fingerprint)), ct);
            if (string.IsNullOrWhiteSpace(orderId) && string.IsNullOrWhiteSpace(fingerprint))
            {
                handled = false;
            }
            else if (transaction is not null)
            {
                await RevokeAsync(StoreProvider.Google, transaction.Environment, transaction.ProviderTransactionId,
                    "VOIDED_PURCHASE", ct, fingerprint);
                handled = true;
            }
            else
            {
                await RevokeAsync(StoreProvider.Google, StoreEnvironment.Production, orderId,
                    "VOIDED_PURCHASE", ct, fingerprint);
                handled = true;
            }
        }
        else
        {
            handled = true;
        }
        if (handled) receipt.ProcessedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
        return Ok();
    }

    private async Task<StoreNotificationReceipt> PersistReceiptAsync(StoreProvider provider, StoreEnvironment environment,
        string id, string payload, CancellationToken ct)
    {
        var existing = await dbContext.StoreNotificationReceipts.FirstOrDefaultAsync(item => item.Provider == provider
            && item.Environment == environment && item.ProviderNotificationId == id, ct);
        if (existing is not null) return existing;
        var receipt = new StoreNotificationReceipt
        {
            Id = Guid.NewGuid(), Provider = provider, Environment = environment, ProviderNotificationId = id,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            ProtectedPayload = payloadProtector.Protect(payload)
        };
        dbContext.StoreNotificationReceipts.Add(receipt);
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return receipt;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(receipt).State = EntityState.Detached;
            return await dbContext.StoreNotificationReceipts.SingleAsync(item => item.Provider == provider
                && item.Environment == environment && item.ProviderNotificationId == id, ct);
        }
    }

    private async Task RevokeAsync(StoreProvider provider, StoreEnvironment environment, string? transactionId, string reason,
        CancellationToken ct, string? evidenceFingerprint = null)
    {
        var transaction = await dbContext.StorePurchaseTransactions.FirstOrDefaultAsync(item => item.Provider == provider
            && item.Environment == environment && (item.ProviderTransactionId == transactionId
                || evidenceFingerprint != null && item.EvidenceFingerprint == evidenceFingerprint), ct);
        if (transaction is null)
        {
            var exists = await dbContext.StoreRevocationMarkers.AnyAsync(item => item.Provider == provider
                && item.Environment == environment && item.ProviderTransactionId == transactionId
                && item.EvidenceFingerprint == evidenceFingerprint, ct);
            if (!exists)
                dbContext.StoreRevocationMarkers.Add(new StoreRevocationMarker
                {
                    Id = Guid.NewGuid(), Provider = provider, Environment = environment,
                    ProviderTransactionId = transactionId, EvidenceFingerprint = evidenceFingerprint,
                    Reason = reason, ReceivedAtUtc = DateTimeOffset.UtcNow
                });
            await dbContext.SaveChangesAsync(ct);
            return;
        }
        if (transaction.RevokedAtUtc.HasValue) return;
        transaction.RevokedAtUtc = DateTimeOffset.UtcNow; transaction.RevocationReason = reason;
        var grant = await dbContext.BuilderAccessGrants.FirstOrDefaultAsync(item => item.PurchaseTransactionId == transaction.Id, ct);
        if (grant is not null) { grant.Status = BuilderAccessStatus.Revoked; grant.RevokedAtUtc = DateTimeOffset.UtcNow; }
        var intent = await dbContext.StorePurchaseIntents.AsNoTracking().SingleAsync(item => item.Id == transaction.PurchaseIntentId, ct);
        await analytics.RecordServerEventAsync(intent.AppUserId, intent.TripId, "refund", reason,
            intent.PaywallVariant, ct, environment == StoreEnvironment.Sandbox, intent.Platform, intent.AppVersion);
        await sessions.RevokeUserSessionsAsync(intent.AppUserId, ct);
    }

    private async Task<bool> ValidateGoogleOidcAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.GoogleRtdnAudience) || string.IsNullOrWhiteSpace(settings.GoogleRtdnServiceAccountEmail)) return false;
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var token = authorization[7..].Trim();
        var client = clients.CreateClient("GoogleOidc");
        using var response = await client.GetAsync($"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(token)}", ct);
        if (!response.IsSuccessStatusCode) return false;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;
        return root.TryGetProperty("aud", out var audience) && audience.GetString() == settings.GoogleRtdnAudience
            && root.TryGetProperty("email", out var email) && email.GetString() == settings.GoogleRtdnServiceAccountEmail
            && root.TryGetProperty("email_verified", out var verified) && verified.GetString() == "true";
    }
}

using System.Text.Json;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class StorePurchaseRecoveryService(
    AuthSessionService sessions,
    TravelCompanionApiClient api,
    IStorePurchaseService store,
    PendingStorePurchaseStore pendingStore,
    OfflineSyncCoordinator syncCoordinator)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        if (!await gate.WaitAsync(0, cancellationToken)) return;
        try
        {
            var userId = sessions.CurrentUserId;
            var contextVersion = sessions.ContextVersion;
            bool Current() => !cancellationToken.IsCancellationRequested && sessions.HasSession
                && sessions.CurrentUserId == userId && sessions.ContextVersion == contextVersion;
            if (!Current() || userId is null) return;
            var pending = await pendingStore.GetAsync(userId.Value);
            if (!Current()) return;
            var token = await sessions.GetTokenAsync();
            if (!Current() || pending is null || string.IsNullOrWhiteSpace(token)
                || pending.UserId != userId || pending.Provider != store.Provider)
                return;

            var passes = await api.RestorePassesAsync(token, cancellationToken);
            if (!Current()) return;
            if (passes.Any(item => item.TripId == pending.TripId && item.State == TrialAccessState.Paid))
            {
                if (!string.IsNullOrWhiteSpace(pending.Evidence))
                    await store.FinishAsync(pending.Evidence, cancellationToken);
                if (!Current()) return;
                if (await ActivateTripAsync(token, pending, contextVersion, cancellationToken))
                    await pendingStore.ClearAsync(pending);
                return;
            }

            var evidence = pending.Evidence;
            if (string.IsNullOrWhiteSpace(evidence))
            {
                var restored = await store.RestoreAsync(cancellationToken);
                evidence = restored.FirstOrDefault(item => EvidenceMatches(item, pending.OpaqueAccountId))
                    ?? (restored.Count == 1 ? restored[0] : null);
                if (string.IsNullOrWhiteSpace(evidence)) return;
                pending = pending with
                {
                    Evidence = evidence,
                    Environment = DetectEnvironment(evidence),
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                await pendingStore.SaveAsync(pending);
                if (!Current()) return;
            }

            var pass = await api.VerifyPurchaseAsync(token,
                new(pending.IntentId, evidence, pending.Environment, $"resume-{pending.IntentId:N}"), cancellationToken);
            if (!Current() || pass?.State != TrialAccessState.Paid || pass.TripId != pending.TripId) return;
            await store.FinishAsync(evidence, cancellationToken);
            if (!Current()) return;
            if (await ActivateTripAsync(token, pending, contextVersion, cancellationToken))
                await pendingStore.ClearAsync(pending);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Purchase recovery will retry later: {exception.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> ActivateTripAsync(string token, PendingStorePurchase pending, long contextVersion, CancellationToken cancellationToken)
    {
        var selected = await api.SelectAccountTripAsync(token, pending.TripId, cancellationToken);
        if (cancellationToken.IsCancellationRequested || !sessions.HasSession
            || sessions.CurrentUserId != pending.UserId || sessions.ContextVersion != contextVersion) return false;
        if (selected is null || selected.UserId != pending.UserId || selected.TripId != pending.TripId) return false;
        if (!await sessions.SaveIfCurrentAsync(selected, contextVersion)) return false;
        if (cancellationToken.IsCancellationRequested || !sessions.HasSession
            || sessions.CurrentUserId != pending.UserId || sessions.CurrentTripId != pending.TripId) return false;
        if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
        syncCoordinator.TriggerSynchronize();
        return true;
    }

    private static bool EvidenceMatches(string evidence, string opaqueAccountId)
    {
        try
        {
            if (evidence.Count(character => character == '.') == 2)
            {
                using var payload = ReadJwsPayload(evidence);
                return payload.RootElement.TryGetProperty("appAccountToken", out var account)
                    && string.Equals(account.GetString(), opaqueAccountId, StringComparison.OrdinalIgnoreCase);
            }
            using var document = JsonDocument.Parse(evidence);
            return (document.RootElement.TryGetProperty("obfuscatedAccountId", out var googleAccount)
                    || document.RootElement.TryGetProperty("obfuscatedExternalAccountId", out googleAccount))
                && string.Equals(googleAccount.GetString(), opaqueAccountId, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static StoreEnvironment DetectEnvironment(string evidence)
    {
        try
        {
            if (evidence.Count(character => character == '.') != 2) return StoreEnvironment.Production;
            using var payload = ReadJwsPayload(evidence);
            return payload.RootElement.TryGetProperty("environment", out var environment)
                && string.Equals(environment.GetString(), "Sandbox", StringComparison.OrdinalIgnoreCase)
                ? StoreEnvironment.Sandbox : StoreEnvironment.Production;
        }
        catch { return StoreEnvironment.Production; }
    }

    private static JsonDocument ReadJwsPayload(string evidence)
    {
        var value = evidence.Split('.')[1].Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(value));
    }
}

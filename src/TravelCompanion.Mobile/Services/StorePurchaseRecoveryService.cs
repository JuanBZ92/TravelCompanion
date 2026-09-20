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
            var pending = await pendingStore.GetAsync();
            var token = await sessions.GetTokenAsync();
            if (pending is null || string.IsNullOrWhiteSpace(token)
                || pending.UserId != sessions.CurrentUserId || pending.Provider != store.Provider)
                return;

            var passes = await api.RestorePassesAsync(token, cancellationToken);
            if (passes.Any(item => item.TripId == pending.TripId && item.State == TrialAccessState.Paid))
            {
                if (!string.IsNullOrWhiteSpace(pending.Evidence))
                    await store.FinishAsync(pending.Evidence, cancellationToken);
                await ActivateTripAsync(token, pending.TripId, cancellationToken);
                pendingStore.Clear();
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
            }

            var pass = await api.VerifyPurchaseAsync(token,
                new(pending.IntentId, evidence, pending.Environment, $"resume-{pending.IntentId:N}"), cancellationToken);
            if (pass?.State != TrialAccessState.Paid) return;
            await store.FinishAsync(evidence, cancellationToken);
            await ActivateTripAsync(token, pending.TripId, cancellationToken);
            pendingStore.Clear();
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

    private async Task ActivateTripAsync(string token, Guid tripId, CancellationToken cancellationToken)
    {
        var selected = await api.SelectAccountTripAsync(token, tripId, cancellationToken);
        if (selected is not null) await sessions.SaveAsync(selected);
        if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
        syncCoordinator.TriggerSynchronize();
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

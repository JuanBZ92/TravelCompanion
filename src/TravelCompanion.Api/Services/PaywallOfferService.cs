using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class PaywallOfferService(
    TravelCompanionDbContext dbContext,
    UserSessionService sessions,
    IOptions<StorePurchaseOptions> purchaseOptions,
    IOptions<ProductFeatureOptions> features)
{
    private const string CopyExperiment = "paywall-copy-v1";

    public async Task<PaywallOfferDto> GetAsync(HttpContext httpContext, Guid tripId,
        PaywallEntryPoint entryPoint, string platform, CancellationToken cancellationToken)
    {
        if (!features.Value.PaywallEnabled) throw new InvalidOperationException("El paywall está desactivado.");
        var session = await sessions.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        var trip = await dbContext.Trips.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == tripId && item.AppUserId == session.User.Id, cancellationToken)
            ?? throw new KeyNotFoundException();
        var grant = await dbContext.BuilderAccessGrants.AsNoTracking().SingleOrDefaultAsync(item => item.TripId == trip.Id, cancellationToken);
        var days = await dbContext.Reservations.AsNoTracking().Where(item => item.TripId == trip.Id)
            .Select(item => item.Date).Distinct().OrderBy(item => item).ToListAsync(cancellationToken);
        var itemCount = await dbContext.Reservations.CountAsync(item => item.TripId == trip.Id, cancellationToken);
        var routes = await dbContext.ThematicRoutes.CountAsync(item => item.TripId == trip.Id && item.AppUserId == session.User.Id, cancellationToken);
        var additional = await dbContext.Recommendations.CountAsync(item => item.DestinationId == trip.DestinationId
            && item.AccessLevel != ContentAccessLevel.Free, cancellationToken);
        var variant = await GetOrCreateVariantAsync(session.User.Id, cancellationToken);
        var product = platform.Equals("ios", StringComparison.OrdinalIgnoreCase)
            ? purchaseOptions.Value.AppleProductId : purchaseOptions.Value.GoogleProductId;
        var isEnglish = !ProductLanguage.IsSpanish(httpContext);
        var benefits = variant == "continuity"
            ? isEnglish
                ? new[] { "Keep and recover this trip", "Edit until seven days after your trip", "30 Assistant requests per day" }
                : new[] { "Conserva y recupera este viaje", "Edita hasta siete días después del viaje", "30 consultas diarias al Assistant" }
            : isEnglish
                ? new[] { "Complete recommendation catalog", "Builder, routes, and day reorganization", "30 Assistant requests per day" }
                : new[] { "Catálogo completo de recomendaciones", "Builder, rutas y reorganización del día", "30 consultas diarias al Assistant" };
        var now = DateTimeOffset.UtcNow;
        var passExpiry = grant is { IsTrial: false, Status: BuilderAccessStatus.Active }
            ? grant.ExpiresAtUtc : StorePurchaseService.CalculateExpiry(trip, now.AddYears(1));
        var nextUtcDay = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        return new(trip.Id, product, null,
            $"{purchaseOptions.Value.ReferencePrice:0.00} {purchaseOptions.Value.ReferenceCurrency}",
            purchaseOptions.Value.NewPurchasesEnabled && (platform.Equals("ios", StringComparison.OrdinalIgnoreCase)
                || platform.Equals("android", StringComparison.OrdinalIgnoreCase)),
            variant, entryPoint, days, itemCount, routes, additional,
            grant?.TrialDraftExpiresAtUtc, passExpiry,
            Math.Clamp(purchaseOptions.Value.DailyAssistantLimit, 1, 100), benefits, nextUtcDay);
    }

    private async Task<string> GetOrCreateVariantAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.ProductExperimentAssignments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.AppUserId == userId && item.Experiment == CopyExperiment, cancellationToken);
        if (existing is not null) return existing.Variant;

        var variant = AssignVariant(userId, CopyExperiment);
        var assignment = new ProductExperimentAssignment
        {
            Id = Guid.NewGuid(), AppUserId = userId, Experiment = CopyExperiment,
            Variant = variant, AssignedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.ProductExperimentAssignments.Add(assignment);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return variant;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(assignment).State = EntityState.Detached;
            return await dbContext.ProductExperimentAssignments.AsNoTracking()
                .Where(item => item.AppUserId == userId && item.Experiment == CopyExperiment)
                .Select(item => item.Variant).SingleAsync(cancellationToken);
        }
    }

    private static string AssignVariant(Guid userId, string experiment)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{experiment}:{userId:N}"));
        return (hash[0] & 1) == 0 ? "continuity" : "catalog-tools";
    }
}

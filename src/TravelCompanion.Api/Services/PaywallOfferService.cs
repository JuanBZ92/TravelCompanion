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
        var additional = await dbContext.Recommendations.CountAsync(item => item.DestinationId == trip.DestinationId
            && item.AccessLevel != ContentAccessLevel.Free && item.AccessLevel != ContentAccessLevel.AdminOnly, cancellationToken);
        const string variant = "contextual-v2";
        var product = platform.Equals("ios", StringComparison.OrdinalIgnoreCase)
            ? purchaseOptions.Value.AppleProductId : purchaseOptions.Value.GoogleProductId;
        var isEnglish = !ProductLanguage.IsSpanish(httpContext);
        var dailyLimit = Math.Clamp(purchaseOptions.Value.DailyAssistantLimit, 1, 100);
        var lead = entryPoint switch
        {
            PaywallEntryPoint.Assistant => isEnglish ? "Get help completing your next day" : "Recibe ayuda para completar tu próximo día",
            PaywallEntryPoint.Map => isEnglish ? "Discover places beyond the free area" : "Descubre lugares más allá de la zona gratuita",
            PaywallEntryPoint.Offline => isEnglish ? "Take your full itinerary with you, even without a connection" : "Lleva tu itinerario completo contigo, incluso sin conexión",
            _ => isEnglish ? "Plan every day of your trip" : "Planea todos los días de tu viaje"
        };
        var benefits = isEnglish
            ? new[] { lead, "Keep editing throughout your pass", $"{dailyLimit} Assistant requests per day", "Prepare your full itinerary for offline use", "Keep your tickets and PDFs on this device" }
            : new[] { lead, "Sigue editando mientras tu pase esté activo", $"{dailyLimit} consultas diarias al Asistente", "Prepara tu itinerario completo para consultarlo sin conexión", "Guarda tus tickets y PDF en este dispositivo" };
        var now = DateTimeOffset.UtcNow;
        var passExpiry = grant is { IsTrial: false, Status: BuilderAccessStatus.Active }
            ? grant.ExpiresAtUtc : StorePurchaseService.CalculateExpiry(trip, now.AddYears(1));
        var nextUtcDay = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        return new(trip.Id, product, null,
            $"{purchaseOptions.Value.ReferencePrice:0.00} {purchaseOptions.Value.ReferenceCurrency}",
            purchaseOptions.Value.NewPurchasesEnabled && (platform.Equals("ios", StringComparison.OrdinalIgnoreCase)
                || platform.Equals("android", StringComparison.OrdinalIgnoreCase)),
            variant, entryPoint, days, itemCount, 0, additional,
            grant?.FreePolicy == FreeAccessPolicy.PersistentFree ? null : grant?.TrialDraftExpiresAtUtc, passExpiry,
            Math.Clamp(purchaseOptions.Value.DailyAssistantLimit, 1, 100), benefits, nextUtcDay);
    }

}

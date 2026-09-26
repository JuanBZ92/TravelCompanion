using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class FeatureDemoAccountService(TravelCompanionDbContext db)
{
    public async Task<Guid> CreateAsync(CancellationToken ct = default)
    {
        const string email = "features-demo@travelcompanion.local";
        if (await db.AppUsers.AnyAsync(x => x.Email == email, ct))
            throw new InvalidOperationException("Feature demo already exists; refusing to overwrite test edits.");
        foreach (var pin in new[] { "3333", "3334" })
            if (!await AccessPinAvailability.IsAvailableAsync(db, pin, cancellationToken: ct))
                throw new InvalidOperationException($"PIN {pin} is already used; no changes saved.");
        var destination = await db.Destinations.SingleAsync(x => x.Slug == "japon", ct);
        var recommendations = await db.Recommendations.Where(x => x.DestinationId == destination.Id).ToListAsync(ct);
        var start = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Tokyo").DateTime);
        var user = new AppUser { Id = Guid.NewGuid(), Email = email, DisplayName = "Alex Demo · Japón completo",
            IsDemo = true, IsInternal = true, MustChangePassword = false };
        db.AppUsers.Add(user);
        foreach (var editable in new[] { true, false })
        {
            var trip = PremiumDemoTripFactory.Create(user.Id, editable ? "Alex Demo · Editable" : "Alex Demo · Documentos", destination.Id, start, recommendations);
            trip.ExperienceMode = editable ? ExperienceMode.SelfServiceBuilder : ExperienceMode.CuratedPremium;
            trip.ExternalId = editable ? "feature-demo-builder-v1" : "feature-demo-curated-v1";
            trip.BuilderSegmentsJson = System.Text.Json.JsonSerializer.Serialize(trip.DayPlans.GroupBy(x => x.City)
                .Select(g => new BuilderTripSetupSegmentDto(g.Key, g.Min(x => x.Date), g.Max(x => x.Date),
                    g.First().HotelBase, g.First().BaseAddress, g.First().BaseLatitude, g.First().BaseLongitude)));
            // Keep day 1 relaxed, day 2 intentionally conflicting, day 3 empty for AI planning.
            var firstDayItems = trip.Reservations.Where(x => x.Date == start && x.Type == ReservationType.Event).OrderBy(x => x.StartsAt).ToList();
            var remove = trip.Reservations.Where(x => x.Date == start.AddDays(2)).Concat(firstDayItems.Skip(3)).ToHashSet();
            trip.Reservations.RemoveAll(remove.Contains);
            foreach (var block in trip.DayPlans.SelectMany(x => x.Blocks)) block.Reservations.RemoveAll(remove.Contains);
            var hour = 9;
            foreach (var item in firstDayItems.Take(3))
            {
                item.StartsAt = new TimeOnly(hour, 0); item.EndsAt = item.StartsAt.AddMinutes(60);
                item.Flexibility = ItineraryFlexibility.FixedByTraveler;
                item.DurationMinutes = 60; item.TimePrecision = ItineraryTimePrecision.Exact; hour += 4;
            }
            foreach (var item in trip.Reservations.Where(x => x.Date == start.AddDays(1)).Take(3))
            {
                item.StartsAt = new TimeOnly(10, 0); item.EndsAt = new TimeOnly(12, 0);
                item.Flexibility = ItineraryFlexibility.FixedByTraveler;
                item.DurationMinutes = 120; item.TimePrecision = ItineraryTimePrecision.Exact;
                item.Notes = "DEMO: solapamiento intencional para probar Revisar mi viaje y Mejorar el día.";
            }
            foreach (var item in trip.Reservations)
            {
                item.ReminderEnabled = item.Type == ReservationType.Flight || item.PlanningKind == ScheduleItemKind.ConfirmedReservation;
                if (editable) item.Owner = ItineraryItemOwner.Traveler;
                item.ConfirmationCode = "DEMO-" + item.ConfirmationCode;
            }
            foreach (var name in new[] { "hotel", "flight", "train", "guide" })
                trip.Documents.Add(new TravelDocument { Id = Guid.NewGuid(), TripId = trip.Id,
                    Category = name == "hotel" ? TravelDocumentCategory.Hotel : TravelDocumentCategory.Other,
                    Title = name switch { "hotel" => "DEMO · Voucher de hoteles", "flight" => "DEMO · Billete aéreo", "train" => "DEMO · Billetes de tren", _ => "DEMO · Guía de pruebas" },
                    Subtitle = "Documento ficticio. Sin validez para viajar.", FileUrl = $"/demo-documents/{name}.pdf" });
            if (editable)
            {
                var grant = new BuilderAccessGrant { Id = Guid.NewGuid(), AppUserId = user.Id, DestinationId = destination.Id,
                    TripId = trip.Id, IsTrial = false, ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(90), OrderReference = "DEMO-FEATURES-NOT-A-PURCHASE" };
                grant.PinHash = new PasswordHasher<BuilderAccessGrant>().HashPassword(grant, "3333");
                db.BuilderAccessGrants.Add(grant);
            }
            else trip.AccessPinHash = new PasswordHasher<Trip>().HashPassword(trip, "3334");
            db.Trips.Add(trip);
        }
        db.UserEntitlements.Add(new UserEntitlement { Id = Guid.NewGuid(), UserId = user.Id, DestinationId = destination.Id,
            AccessLevel = ContentAccessLevel.Paid, Source = "Feature demo", ExpiresAt = DateTimeOffset.UtcNow.AddDays(90) });
        await db.SaveChangesAsync(ct);
        return user.Id;
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

// Synthetic review data belongs exclusively to this local database. No app settings or secrets are read.
const string databaseName = "tc_dayplanner_review";
const string password = "JournalReview2026!";
const string paidPin = "700701";
const string largeDocsPin = "600702";
const string navigationPin = "800703";
var largeLists = args.Contains("--large-lists", StringComparer.Ordinal);
var longTrip = args.Contains("--long-trip", StringComparer.Ordinal);
if (args.Any(argument => argument is not ("--large-lists" or "--long-trip")))
    throw new ArgumentException("Only --large-lists and --long-trip are supported by this synthetic review tool.");
var connection = new NpgsqlConnectionStringBuilder(
    Environment.GetEnvironmentVariable("TRAVELCOMPANION_REVIEW_POSTGRES")
    ?? "Host=127.0.0.1;Port=55439;Database=tc_dayplanner_review;Username=postgres;Pooling=false");
if (connection.Host != "127.0.0.1" || connection.Port != 55439 || connection.Database != databaseName
    || !string.IsNullOrWhiteSpace(connection.SearchPath))
    throw new InvalidOperationException("Review seeding only supports 127.0.0.1:55439/tc_dayplanner_review without a custom search path.");
await using var db = new TravelCompanionDbContext(new DbContextOptionsBuilder<TravelCompanionDbContext>()
    .UseNpgsql(connection.ConnectionString).Options);
await db.Database.MigrateAsync();
var userHasher = new PasswordHasher<AppUser>();
await DatabaseSeeder.SeedAsync(db, userHasher);
var japan = await db.Destinations.SingleAsync(item => item.Slug == "japon");
japan.HeroImageUrl = "";
japan.ShortDescription = "Viaje sintético para revisar la experiencia de planificación local.";
await SeedCatalogAsync(db, japan.Id);
var start = new DateOnly(2026, 10, 20);
await SeedTravelerAsync(db, japan.Id, "planner-free@example.test", "Lucía · viaje gratuito", 7, true);
await SeedTravelerAsync(db, japan.Id, "planner-pass@example.test", "Mateo · viaje con pase", 10, false);
await db.SaveChangesAsync();
if (largeLists) await SeedLargeListsAsync(db, japan.Id);
if (longTrip)
{
    await SeedTravelerAsync(db, japan.Id, "planner-navigation@example.test", "Sofía · navegación de 30 días", 30, false,
        pin: navigationPin, segments:
        [
            new("Tokyo", start, start.AddDays(4), HotelName: "Hotel Marunouchi"),
            new("Kyoto", start.AddDays(4), start.AddDays(29),
                HotelName: "Hotel de revisión junto a los jardines y las calles históricas de Kyoto · Una base tranquila para descubrir templos, mercados y cafés del barrio")
        ]);
    await db.SaveChangesAsync();
    Console.WriteLine($"Navigation account: planner-navigation@example.test; PIN: {navigationPin}; 30 days from {start:yyyy-MM-dd} through {start.AddDays(29):yyyy-MM-dd}.");
}
Console.WriteLine($"Local synthetic review database ready: 127.0.0.1:55439/{databaseName}");
Console.WriteLine($"Review accounts: planner-free@example.test / planner-pass@example.test; password: {password}");
Console.WriteLine($"Paid account PIN: {paidPin}. Free account uses email/password and Select trip (no custom trial PIN).");
Console.WriteLine("Existing review accounts, plans and generation counts are preserved when this tool runs again.");

async Task SeedTravelerAsync(TravelCompanionDbContext context, Guid destinationId, string email, string name, int days, bool trial,
    string? pin = null, IReadOnlyList<BuilderTripSetupSegmentDto>? segments = null)
{
    var existing = await context.AppUsers.AsNoTracking().SingleOrDefaultAsync(item => item.Email == email);
    if (existing is not null)
    {
        var existingTrip = await context.Trips.AsNoTracking().FirstOrDefaultAsync(item => item.AppUserId == existing.Id
            && item.ExperienceMode == ExperienceMode.SelfServiceBuilder);
        Console.WriteLine($"Reused {email}; trip: {existingTrip?.Id}");
        return;
    }
    var now = DateTimeOffset.UtcNow;
    var user = new AppUser
    {
        Id = Guid.NewGuid(), Email = email, DisplayName = name, EmailVerified = true,
        EmailVerifiedAtUtc = now, MustChangePassword = false, BehaviorAnalyticsConsent = true
    };
    user.PasswordHash = userHasher.HashPassword(user, password);
    user.TravelPreferenceProfile = new TravelPreferenceProfile
    {
        UserId = user.Id, BudgetLevel = "medium", TravelPace = "balanced",
        Interests = ["Culture", "Food"], MaxWalkingMinutes = 90
    };
    user.Entitlements.Add(new UserEntitlement
    {
        Id = Guid.NewGuid(), UserId = user.Id, DestinationId = destinationId,
        AccessLevel = trial ? ContentAccessLevel.Free : ContentAccessLevel.Subscription,
        GrantedAt = now, ExpiresAt = trial ? null : now.AddDays(60), Source = "local-day-planner-review"
    });
    var transition = start.AddDays(4);
    var tripSegments = segments ?? new BuilderTripSetupSegmentDto[]
    {
        new("Tokyo", start, transition), new("Kyoto", transition, start.AddDays(days - 1))
    };
    var trip = new Trip
    {
        Id = Guid.NewGuid(), AppUserId = user.Id, AppUser = user, DestinationId = destinationId,
        TravelerName = name, StartsOn = start, EndsOn = start.AddDays(days - 1), TimeZoneId = "Asia/Tokyo",
        ExperienceMode = ExperienceMode.SelfServiceBuilder, PublicationStatus = TripPublicationStatus.Published,
        PublishedAtUtc = now, BuilderSegmentsJson = JsonSerializer.Serialize(tripSegments)
    };
    var grant = new BuilderAccessGrant
    {
        Id = Guid.NewGuid(), AppUserId = user.Id, AppUser = user, DestinationId = destinationId,
        TripId = trip.Id, Trip = trip, Status = BuilderAccessStatus.Active, IsTrial = trial,
        FreePolicy = FreeAccessPolicy.PersistentFree, TrialEditingStartedAtUtc = trial ? now : null,
        CreatedAtUtc = now, RedeemedAtUtc = now, ExpiresAtUtc = trial ? null : now.AddDays(60),
        OrderReference = "local-day-planner-review"
    };
    if (!trial) grant.PinHash = new PasswordHasher<BuilderAccessGrant>().HashPassword(grant, pin ?? paidPin);
    for (var offset = 0; offset < days; offset++)
    {
        var date = start.AddDays(offset);
        var segment = tripSegments.OrderBy(item => item.StartsOn).Last(item => date >= item.StartsOn && date <= item.EndsOn);
        var day = new TripDayPlan
        {
            Id = Guid.NewGuid(), TripId = trip.Id, Trip = trip, Date = date, DayNumber = offset + 1,
            City = segment.City, HotelBase = segment.HotelName ?? $"Hotel de revisión en {segment.City}",
            Introduction = offset == 4 ? "Traslado de Tokyo a Kyoto: ideas flexibles para aprovechar ambas ciudades." : "Un día para descubrir Japón a tu ritmo."
        };
        foreach (var period in TripPlanPeriods.All)
            day.Blocks.Add(new TripDayBlock
            {
                Id = Guid.NewGuid(), TripDayPlanId = day.Id, TripDayPlan = day,
                PeriodKey = period.Key, SortOrder = period.SortOrder
            });
        trip.DayPlans.Add(day);
    }
    trip.Reservations.Add(new Reservation
    {
        Id = Guid.NewGuid(), TripId = trip.Id, Trip = trip, Date = start, StartsAt = new TimeOnly(10, 0),
        EndsAt = new TimeOnly(11, 30), City = "Tokyo", Title = "Entrada reservada: exposición de arte y jardines del centro de Tokyo",
        LocationName = "Museo de revisión", Address = "Tokyo, Japan", ConfirmationCode = "REVIEW-001",
        Notes = "Reserva sintética con horario fijo. El nuevo plan conserva este compromiso.",
        Owner = ItineraryItemOwner.Yuku, Flexibility = ItineraryFlexibility.ConfirmedReservation,
        TimePrecision = ItineraryTimePrecision.Exact, Type = ReservationType.Event
    });
    context.AddRange(user, trip, grant);
    context.JournalFreeEntries.AddRange(
        new JournalFreeEntry
        {
            Id = Guid.NewGuid(), UserId = user.Id, TripId = trip.Id, Date = start.AddDays(-1),
            Title = "La emoción de salir hacia Japón", Place = "Antes del viaje",
            Notes = "Dejé el equipaje preparado junto a la puerta. Me gustaría recordar la emoción de empezar este viaje sin tener que llenar cada hora del itinerario.",
            Revision = 1, MutationId = Guid.NewGuid(), UpdatedAt = now
        },
        new JournalFreeEntry
        {
            Id = Guid.NewGuid(), UserId = user.Id, TripId = trip.Id, Date = start,
            Title = "", Place = "Una cafetería tranquila en Tokyo",
            Notes = "Nos detuvimos a tomar café, a mirar la calle y a conversar. Este recuerdo sintético permite revisar el diseño cuando el lugar sustituye al título.",
            Revision = 1, MutationId = Guid.NewGuid(), UpdatedAt = now
        });
    Console.WriteLine($"Created {email}; trip: {trip.Id}; {start:yyyy-MM-dd} through {trip.EndsOn:yyyy-MM-dd}; {(trial ? "free" : "pass")}");
}

static async Task SeedCatalogAsync(TravelCompanionDbContext context, Guid destinationId)
{
    var known = await context.Recommendations.AsNoTracking()
        .Where(item => item.ExternalId != null && item.ExternalId.StartsWith("local-day-planner-review-"))
        .Select(item => item.ExternalId!).ToListAsync();
    var foodTitles = new[] { "Desayuno de temporada y café de especialidad", "Una mesa tranquila para probar ramen artesanal", "Sabores locales en un mercado de barrio", "Cena de cocina japonesa con vistas al río" };
    var cultureTitles = new[] { "Arte y pequeñas historias en un museo de barrio", "Paseo por jardines, templos y calles con historia", "Una librería y una exposición para descubrir sin prisa", "Arquitectura tradicional junto a una plaza tranquila" };
    foreach (var city in new[] { "Tokyo", "Kyoto" })
    for (var i = 0; i < 64; i++)
    {
        var key = $"local-day-planner-review-{city.ToLowerInvariant()}-{i:00}";
        if (known.Contains(key)) continue;
        var food = i % 3 == 0;
        var titles = food ? foodTitles : cultureTitles;
        context.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(), ExternalId = key, DestinationId = destinationId,
            Title = $"{titles[(i / 3) % titles.Length]} · {city} {i + 1}",
            Category = food ? "Food" : "Culture", Neighborhood = $"{city}, Japan", CitySlug = city.ToLowerInvariant(),
            Description = $"Una propuesta sintética en {city} para revisar títulos largos, preferencias y organización por momentos del día. No es una recomendación comercial ni una reserva real.",
            Tags = food ? ["food", "restaurant", "breakfast", "lunch", "dinner"] : ["culture", "museum", "art", "walk"],
            PriceLevel = i % 4 == 0 ? "low" : "medium", IsPriceKnown = true,
            Latitude = (city == "Tokyo" ? 35.681236m : 35.003700m) + (i % 6) * 0.001m,
            Longitude = (city == "Tokyo" ? 139.767125m : 135.768800m) + (i % 5) * 0.001m,
            SuggestedDurationMinutes = food ? 60 : 90, Rating = 4.4 + (i % 3) * 0.1,
            OpeningHours = "08:00-23:00", SourceName = "Catálogo sintético local",
            EditorialReviewedOn = new DateOnly(2026, 10, 4),
            AccessLevel = i % 10 == 9 ? ContentAccessLevel.Subscription : ContentAccessLevel.Free
        });
    }
}

async Task SeedLargeListsAsync(TravelCompanionDbContext context, Guid destinationId)
{
    var pdfPath = Path.GetFullPath(Path.Combine("src", "TravelCompanion.Api", "wwwroot", "demo-documents", "guide.pdf"));
    var pdf = await File.ReadAllBytesAsync(pdfPath);
    if (!pdf.AsSpan().StartsWith("%PDF-"u8)
        || !System.Text.Encoding.ASCII.GetString(pdf.AsSpan(Math.Max(0, pdf.Length - 128))).Contains("%%EOF", StringComparison.Ordinal))
        throw new InvalidDataException("The existing local synthetic guide must be a complete PDF.");
    var user = await context.AppUsers.SingleAsync(item => item.Email == "planner-pass@example.test" && item.DeletedAtUtc == null);
    var trip = await context.Trips.SingleAsync(item => item.AppUserId == user.Id
        && item.ExperienceMode == ExperienceMode.SelfServiceBuilder && !item.IsArchived);
    var day = await context.TripDayPlans.Include(item => item.Blocks)
        .SingleAsync(item => item.TripId == trip.Id && item.Date == start);
    const string planPrefix = "local-large-list-plan-";
    var knownPlans = (await context.Reservations.AsNoTracking().Where(item => item.TripId == trip.Id
        && item.ExternalId != null && item.ExternalId.StartsWith(planPrefix)).Select(item => item.ExternalId!).ToListAsync()).ToHashSet();
    var addedPlans = 0;
    for (var index = 0; index < 100; index++)
    {
        var key = $"{planPrefix}{index:000}";
        if (knownPlans.Contains(key)) continue;
        var period = TripPlanPeriods.All[index % TripPlanPeriods.All.Count];
        var block = day.Blocks.Single(item => item.PeriodKey == period.Key);
        context.Reservations.Add(new Reservation
        {
            Id = Guid.NewGuid(), ExternalId = key, TripId = trip.Id, TripDayBlockId = block.Id,
            Date = start, StartsAt = period.StartsAt, City = "Tokyo", Title = $"Plan de revisión {index + 1:000} · Cafeterías, jardines y pequeñas historias de Tokyo",
            LocationName = $"Lugar sintético {index + 1:000}", Address = "Tokyo, Japan", ConfirmationCode = "",
            Notes = "Plan manual sintético para revisar listas largas, desplazamiento y textos ampliados. No representa una reserva real.",
            Type = ReservationType.Event, PlanningKind = ScheduleItemKind.ManualEvent,
            Owner = ItineraryItemOwner.Traveler, ItemSource = ItineraryItemSource.Manual,
            TimePrecision = ItineraryTimePrecision.PeriodOnly, Flexibility = ItineraryFlexibility.Flexible,
            DurationMinutes = 60, SortOrder = 1000 + index / TripPlanPeriods.All.Count, TimeZoneId = trip.TimeZoneId
        });
        addedPlans++;
    }
    if (addedPlans > 0) { trip.PlanRevision++; trip.UpdatedAtUtc = DateTimeOffset.UtcNow; }

    // Builder sessions cannot open curated documents. A separate synthetic curated trip keeps those rules intact.
    const string docsTripKey = "local-day-planner-review-large-documents";
    var docsTrip = await context.Trips.SingleOrDefaultAsync(item => item.AppUserId == user.Id && item.ExternalId == docsTripKey);
    if (docsTrip is null)
    {
        docsTrip = new Trip
        {
            Id = Guid.NewGuid(), ExternalId = docsTripKey, AppUserId = user.Id, DestinationId = destinationId,
            TravelerName = "Mateo · revisión de 100 documentos", StartsOn = start, EndsOn = start.AddDays(9),
            TimeZoneId = "Asia/Tokyo", ExperienceMode = ExperienceMode.CuratedPremium,
            PublicationStatus = TripPublicationStatus.Published, PublishedAtUtc = DateTimeOffset.UtcNow
        };
        docsTrip.AccessPinHash = new PasswordHasher<Trip>().HashPassword(docsTrip, largeDocsPin);
        docsTrip.AccessPinUpdatedAt = DateTimeOffset.UtcNow;
        context.Trips.Add(docsTrip);
    }
    const string documentPrefix = "local-large-list-document-";
    var knownDocuments = (await context.TravelDocuments.AsNoTracking().Where(item => item.TripId == docsTrip.Id
        && item.ExternalId != null && item.ExternalId.StartsWith(documentPrefix)).Select(item => item.ExternalId!).ToListAsync()).ToHashSet();
    var addedDocuments = 0;
    for (var index = 0; index < 100; index++)
    {
        var key = $"{documentPrefix}{index:000}";
        if (knownDocuments.Contains(key)) continue;
        context.TravelDocuments.Add(new TravelDocument
        {
            Id = Guid.NewGuid(), ExternalId = key, TripId = docsTrip.Id, Category = TravelDocumentCategory.Other,
            Title = $"Documento de revisión {index + 1:000} · Información útil para disfrutar del viaje por Japón",
            Subtitle = "Guía PDF sintética local para revisar listas largas y accesibilidad.",
            FileUrl = $"/demo-documents/guide.pdf?synthetic={index + 1:000}", SortOrder = index
        });
        addedDocuments++;
    }
    await context.SaveChangesAsync();
    Console.WriteLine($"Large-list review: added {addedPlans} manual plans on {start:yyyy-MM-dd}; added {addedDocuments} curated documents.");
    Console.WriteLine($"Builder trip: {trip.Id}; curated document trip: {docsTrip.Id}; curated PIN: {largeDocsPin}.");
    Console.WriteLine("Both lists retain their identifiers on repeat runs. Existing accounts, grants and quotas are preserved.");
}

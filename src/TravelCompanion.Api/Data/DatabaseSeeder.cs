using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Data;

public static class DatabaseSeeder
{
    private static readonly Guid JapanDestinationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly string[] LegacySeedUserEmails =
    [
        "demo@travelcompanion.local",
        "usuariofree@travelcompanion.local",
        "usuariosub@travelcompanion.local",
        "usuariopaid@travelcompanion.local"
    ];

    private static readonly string[] LegacySeedPackageSlugs =
    [
        "japon-essentials",
        "japon-premium-pack"
    ];

    public static async Task SeedAsync(
        TravelCompanionDbContext dbContext,
        IPasswordHasher<AppUser> passwordHasher)
    {
        _ = passwordHasher;

        var japan = await EnsureJapanDestinationAsync(dbContext);
        await RemoveLegacySeedDataAsync(dbContext, japan.Id);
        await EnsureFreePreviewAccountAsync(dbContext);
        await EnsureFreeMapCitiesAsync(dbContext, japan.Id);
        await NormalizeImportedYukuRecommendationsAsync(dbContext, japan.Id);
        await BackfillTripDayPlansAsync(dbContext);
        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureFreePreviewAccountAsync(TravelCompanionDbContext dbContext)
    {
        if (await dbContext.AppUsers.AnyAsync(user => user.Email == FreePreviewAccountService.AccountEmail))
        {
            return;
        }

        dbContext.AppUsers.Add(new AppUser
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Email = FreePreviewAccountService.AccountEmail,
            DisplayName = "YUKU Preview",
            PasswordHash = string.Empty,
            MustChangePassword = false
        });
    }

    private static async Task EnsureFreeMapCitiesAsync(
        TravelCompanionDbContext dbContext,
        Guid destinationId)
    {
        var definitions = new[]
        {
            new FreeMapCityDefinition("tokyo", "Tokyo", 35.681236m, 139.767125m, 1, 30m),
            new FreeMapCityDefinition("kyoto", "Kyoto", 35.003700m, 135.768800m, 2, 18m),
            new FreeMapCityDefinition("osaka", "Osaka", 34.668700m, 135.501300m, 3, 20m)
        };

        var existing = await dbContext.FreeMapCities
            .Where(city => city.DestinationId == destinationId)
            .ToListAsync();
        foreach (var definition in definitions)
        {
            if (existing.Any(city => city.CitySlug == definition.Slug))
            {
                continue;
            }

            dbContext.FreeMapCities.Add(new FreeMapCity
            {
                Id = Guid.NewGuid(),
                DestinationId = destinationId,
                CitySlug = definition.Slug,
                DisplayName = definition.Name,
                CenterLatitude = definition.Latitude,
                CenterLongitude = definition.Longitude,
                FreeRadiusKm = 2m,
                CoverageRadiusKm = definition.CoverageRadiusKm,
                SortOrder = definition.SortOrder,
                IsEnabled = true
            });
        }
    }

    private static async Task<Destination> EnsureJapanDestinationAsync(TravelCompanionDbContext dbContext)
    {
        var destination = await dbContext.Destinations
            .FirstOrDefaultAsync(existing => existing.Slug == "japon");
        if (destination is null)
        {
            destination = new Destination
            {
                Id = JapanDestinationId,
                Name = "Japon",
                Slug = "japon",
                Country = "Japan",
                TimeZoneId = "Asia/Tokyo",
                HeroImageUrl = "https://images.unsplash.com/photo-1542051841857-5f90071e7989",
                ShortDescription = "Tokyo, Kyoto y Osaka con recomendaciones reales de YUKU Japan."
            };
            dbContext.Destinations.Add(destination);
            return destination;
        }

        destination.Name = string.IsNullOrWhiteSpace(destination.Name) ? "Japon" : destination.Name;
        destination.Country = string.IsNullOrWhiteSpace(destination.Country) ? "Japan" : destination.Country;
        destination.TimeZoneId = string.IsNullOrWhiteSpace(destination.TimeZoneId)
            ? "Asia/Tokyo"
            : destination.TimeZoneId;
        destination.ShortDescription = "Tokyo, Kyoto y Osaka con recomendaciones reales de YUKU Japan.";
        return destination;
    }

    private static async Task RemoveLegacySeedDataAsync(
        TravelCompanionDbContext dbContext,
        Guid japanDestinationId)
    {
        var legacyUserIds = await dbContext.AppUsers
            .Where(user => LegacySeedUserEmails.Contains(user.Email))
            .Select(user => user.Id)
            .ToListAsync();
        var legacyTripIds = await dbContext.Trips
            .Where(trip =>
                (trip.AppUserId.HasValue && legacyUserIds.Contains(trip.AppUserId.Value))
                || (trip.ExternalId == null
                    && (trip.TravelerName == "Demo Traveler"
                        || trip.TravelerName == "Usuario Free"
                        || trip.TravelerName == "Usuario Sub"
                        || trip.TravelerName == "Usuario Paid")))
            .Select(trip => trip.Id)
            .ToListAsync();
        var legacyReservationIds = await dbContext.Reservations
            .Where(reservation => legacyTripIds.Contains(reservation.TripId))
            .Select(reservation => reservation.Id)
            .ToListAsync();
        var nonYukuRecommendations = await dbContext.Recommendations
            .Include(recommendation => recommendation.Packages)
            .Where(recommendation => recommendation.DestinationId == japanDestinationId
                && recommendation.SourceName != YukuJapanRecommendationImportService.SourceName)
            .ToListAsync();
        var nonYukuRecommendationIds = nonYukuRecommendations
            .Select(recommendation => recommendation.Id)
            .ToList();

        dbContext.NotificationOutboxItems.RemoveRange(await dbContext.NotificationOutboxItems
            .Where(notification =>
                legacyUserIds.Contains(notification.UserId)
                || (notification.ReservationId.HasValue && legacyReservationIds.Contains(notification.ReservationId.Value))
                || (notification.RecommendationId.HasValue && nonYukuRecommendationIds.Contains(notification.RecommendationId.Value)))
            .ToListAsync());
        dbContext.RecommendationInteractionSignals.RemoveRange(await dbContext.RecommendationInteractionSignals
            .Where(signal =>
                legacyUserIds.Contains(signal.UserId)
                || (signal.TripId.HasValue && legacyTripIds.Contains(signal.TripId.Value))
                || nonYukuRecommendationIds.Contains(signal.RecommendationId))
            .ToListAsync());
        dbContext.TravelAssistantFeedbackItems.RemoveRange(await dbContext.TravelAssistantFeedbackItems
            .Where(feedback =>
                legacyUserIds.Contains(feedback.UserId)
                || nonYukuRecommendationIds.Contains(feedback.RecommendationId))
            .ToListAsync());
        dbContext.TravelChatConversations.RemoveRange(await dbContext.TravelChatConversations
            .Where(conversation => legacyUserIds.Contains(conversation.UserId))
            .ToListAsync());
        dbContext.TravelPreferenceProfiles.RemoveRange(await dbContext.TravelPreferenceProfiles
            .Where(profile => legacyUserIds.Contains(profile.UserId))
            .ToListAsync());
        dbContext.NotificationDeviceRegistrations.RemoveRange(await dbContext.NotificationDeviceRegistrations
            .Where(device => legacyUserIds.Contains(device.UserId))
            .ToListAsync());
        dbContext.AppUserSessions.RemoveRange(await dbContext.AppUserSessions
            .Where(session => legacyUserIds.Contains(session.UserId))
            .ToListAsync());
        dbContext.UserEntitlements.RemoveRange(await dbContext.UserEntitlements
            .Where(entitlement => legacyUserIds.Contains(entitlement.UserId))
            .ToListAsync());
        dbContext.TravelDocuments.RemoveRange(await dbContext.TravelDocuments
            .Where(document => legacyTripIds.Contains(document.TripId))
            .ToListAsync());
        dbContext.Reservations.RemoveRange(await dbContext.Reservations
            .Where(reservation => legacyTripIds.Contains(reservation.TripId))
            .ToListAsync());
        dbContext.Trips.RemoveRange(await dbContext.Trips
            .Where(trip => legacyTripIds.Contains(trip.Id))
            .ToListAsync());
        dbContext.AppUsers.RemoveRange(await dbContext.AppUsers
            .Where(user => legacyUserIds.Contains(user.Id))
            .ToListAsync());

        foreach (var recommendation in nonYukuRecommendations)
        {
            recommendation.Packages.Clear();
        }

        dbContext.Recommendations.RemoveRange(nonYukuRecommendations);

        var legacyPackages = await dbContext.TravelPackages
            .Include(package => package.Recommendations)
            .Where(package => package.DestinationId == japanDestinationId
                && LegacySeedPackageSlugs.Contains(package.Slug))
            .ToListAsync();
        foreach (var package in legacyPackages)
        {
            package.Recommendations.Clear();
        }

        dbContext.TravelPackages.RemoveRange(legacyPackages);
    }

    private static async Task NormalizeImportedYukuRecommendationsAsync(
        TravelCompanionDbContext dbContext,
        Guid japanDestinationId)
    {
        var importedRecommendations = await dbContext.Recommendations
            .Include(recommendation => recommendation.Packages)
            .Where(recommendation => recommendation.DestinationId == japanDestinationId
                && recommendation.SourceName == YukuJapanRecommendationImportService.SourceName)
            .ToListAsync();

        foreach (var recommendation in importedRecommendations)
        {
            recommendation.AccessLevel = ContentAccessLevel.Free;
            recommendation.CitySlug = RecommendationCitySlug.FromCity(recommendation.CitySlug)
                is { Length: > 0 } citySlug
                    ? citySlug
                    : RecommendationCitySlug.FromCity(recommendation.Neighborhood);
            recommendation.Packages.Clear();
        }
    }

    private static async Task BackfillTripDayPlansAsync(TravelCompanionDbContext dbContext)
    {
        var trips = await dbContext.Trips
            .Include(trip => trip.DayPlans)
            .Include(trip => trip.Reservations)
            .Where(trip => trip.PublicationStatus == TripPublicationStatus.Published
                && !trip.DayPlans.Any())
            .ToListAsync();
        foreach (var trip in trips)
        {
            if (dbContext.Entry(trip).State == EntityState.Deleted)
            {
                continue;
            }

            var dayNumber = 1;
            for (var date = trip.StartsOn; date <= trip.EndsOn; date = date.AddDays(1))
            {
                var dayItems = trip.Reservations
                    .Where(item => item.Date == date)
                    .OrderBy(item => item.StartsAt)
                    .ToList();
                var lodging = trip.Reservations
                    .Where(item => item.Type == ReservationType.Lodging
                        && item.Date <= date
                        && (item.EndsOn ?? item.Date) >= date)
                    .OrderByDescending(item => item.Date)
                    .FirstOrDefault();
                var day = new TripDayPlan
                {
                    Id = Guid.NewGuid(),
                    TripId = trip.Id,
                    Date = date,
                    DayNumber = dayNumber++,
                    City = dayItems.Select(item => item.City).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                        ?? lodging?.City
                        ?? string.Empty,
                    HotelBase = lodging?.LocationName ?? string.Empty,
                    BaseLatitude = lodging?.Latitude,
                    BaseLongitude = lodging?.Longitude
                };
                dbContext.TripDayPlans.Add(day);
                foreach (var period in TripPlanPeriods.All)
                {
                    var periodItems = dayItems
                        .Where(item => TripPlanPeriods.Resolve(item.StartsAt).Key == period.Key)
                        .ToList();
                    var block = new TripDayBlock
                    {
                        Id = Guid.NewGuid(),
                        TripDayPlanId = day.Id,
                        PeriodKey = period.Key,
                        SortOrder = period.SortOrder,
                        CuratedDescription = periodItems
                            .Select(item => ExtractLegacyDescription(item.Notes))
                            .FirstOrDefault(value => value.Length > 0) ?? string.Empty,
                        AutofillEnabled = periodItems.Count == 0
                    };
                    dbContext.TripDayBlocks.Add(block);
                    foreach (var item in periodItems)
                    {
                        item.TripDayBlockId = block.Id;
                    }
                }
            }
        }
    }

    private static string ExtractLegacyDescription(string notes)
    {
        const string prefix = "Descripcion:";
        var index = notes.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var value = notes[(index + prefix.Length)..];
        var separator = value.IndexOf(" | ", StringComparison.Ordinal);
        return (separator >= 0 ? value[..separator] : value).Trim();
    }

    private sealed record FreeMapCityDefinition(
        string Slug,
        string Name,
        decimal Latitude,
        decimal Longitude,
        int SortOrder,
        decimal CoverageRadiusKm);
}

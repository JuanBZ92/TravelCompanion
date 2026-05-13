using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Data;

public static class DatabaseSeeder
{
    private static readonly Guid JapanDestinationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JapanEssentialsPackageId = Guid.Parse("22222222-2222-2222-2222-222222222201");
    private static readonly Guid PremiumPackageId = Guid.Parse("22222222-2222-2222-2222-222222222202");
    private static readonly Guid FushimiInariRecommendationId = Guid.Parse("33333333-3333-3333-3333-333333333302");
    private static readonly Guid DotonboriRecommendationId = Guid.Parse("33333333-3333-3333-3333-333333333303");
    private static readonly Guid OmakaseReservationId = Guid.Parse("55555555-5555-5555-5555-555555555502");
    private static readonly Guid DemoUserId = Guid.Parse("66666666-6666-6666-6666-666666666601");
    private const string DemoUserEmail = "demo@travelcompanion.local";

    public static async Task SeedAsync(TravelCompanionDbContext dbContext, IPasswordHasher<AppUser> passwordHasher)
    {
        if (!await dbContext.Destinations.AnyAsync())
        {
            SeedJapanContent(dbContext);
        }

        if (!await dbContext.AppUsers.AnyAsync(user => user.Email == DemoUserEmail))
        {
            SeedDemoUser(dbContext, passwordHasher);
        }

        await NormalizeDemoDataAsync(dbContext, passwordHasher);

        await dbContext.SaveChangesAsync();
    }

    private static void SeedJapanContent(TravelCompanionDbContext dbContext)
    {
        var japan = new Destination
        {
            Id = JapanDestinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = "https://images.unsplash.com/photo-1542051841857-5f90071e7989",
            ShortDescription = "Tokyo, Kyoto y Osaka con planes curados, barrios caminables y reservas organizadas."
        };

        japan.Packages.AddRange(
        [
            new TravelPackage
            {
                Id = JapanEssentialsPackageId,
                DestinationId = japan.Id,
                Name = "Japon Essentials",
                Slug = "japon-essentials",
                Description = "Guia curada con recomendaciones, mapa y tips practicos para un primer viaje.",
                Price = 19.99m,
                Currency = "USD",
                IsSubscription = false
            },
            new TravelPackage
            {
                Id = PremiumPackageId,
                DestinationId = japan.Id,
                Name = "Travel Companion Premium",
                Slug = "travel-companion-premium",
                Description = "Acceso a todos los destinos, updates y soporte prioritario.",
                Price = 8.99m,
                Currency = "USD",
                IsSubscription = true
            }
        ]);

        japan.Recommendations.AddRange(
        [
            new Recommendation
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333301"),
                DestinationId = japan.Id,
                Title = "Tsukiji Outer Market",
                Category = "Food",
                Neighborhood = "Chuo, Tokyo",
                Description = "Ideal para desayuno temprano, snacks de mar y caminar sin apuro antes de Ginza.",
                Latitude = 35.665486m,
                Longitude = 139.770667m,
                SuggestedDurationMinutes = 90,
                AccessLevel = ContentAccessLevel.Free
            },
            new Recommendation
            {
                Id = FushimiInariRecommendationId,
                DestinationId = japan.Id,
                Title = "Fushimi Inari Taisha",
                Category = "Culture",
                Neighborhood = "Fushimi, Kyoto",
                Description = "Santuario de torii rojos. Conviene ir muy temprano o al atardecer para evitar multitudes.",
                Latitude = 34.967140m,
                Longitude = 135.772671m,
                SuggestedDurationMinutes = 120,
                AccessLevel = ContentAccessLevel.Paid
            },
            new Recommendation
            {
                Id = DotonboriRecommendationId,
                DestinationId = japan.Id,
                Title = "Dotonbori",
                Category = "Nightlife",
                Neighborhood = "Namba, Osaka",
                Description = "Neones, street food y una buena primera noche en Osaka sin complicarse.",
                Latitude = 34.668723m,
                Longitude = 135.501297m,
                SuggestedDurationMinutes = 120,
                AccessLevel = ContentAccessLevel.Subscription
            }
        ]);

        var demoTrip = new Trip
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444401"),
            AppUserId = DemoUserId,
            DestinationId = japan.Id,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 5),
            EndsOn = new DateOnly(2026, 10, 15),
            Reservations =
            [
                new Reservation
                {
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555501"),
                    Date = new DateOnly(2026, 10, 6),
                    StartsAt = new TimeOnly(9, 30),
                    Title = "TeamLab Borderless",
                    City = "Tokyo",
                    LocationName = "Azabudai Hills",
                    Address = "1 Chome-2-4 Azabudai, Minato City, Tokyo",
                    ConfirmationCode = "DEMO-TLB-1026",
                    Notes = "Llegar 15 minutos antes. Llevar QR en el telefono."
                },
                new Reservation
                {
                    Id = OmakaseReservationId,
                    Date = new DateOnly(2026, 10, 9),
                    StartsAt = new TimeOnly(18, 0),
                    Title = "Cena omakase",
                    City = "Tokyo",
                    LocationName = "Sushi demo",
                    Address = "Shibuya City, Tokyo",
                    ConfirmationCode = "DEMO-SUSHI-1026",
                    Notes = "Avisar alergias con 48 horas de anticipacion."
                }
            ]
        };

        dbContext.Destinations.Add(japan);
        dbContext.Trips.Add(demoTrip);
    }

    private static void SeedDemoUser(TravelCompanionDbContext dbContext, IPasswordHasher<AppUser> passwordHasher)
    {
        var now = DateTimeOffset.UtcNow;
        var demoUser = new AppUser
        {
            Id = DemoUserId,
            Email = DemoUserEmail,
            DisplayName = "Demo Traveler",
            MustChangePassword = true,
            TemporaryPasswordIssuedAt = now,
            Entitlements =
            [
                new UserEntitlement
                {
                    Id = Guid.Parse("77777777-7777-7777-7777-777777777701"),
                    UserId = DemoUserId,
                    AccessLevel = ContentAccessLevel.Bundle,
                    DestinationId = JapanDestinationId,
                    TravelPackageId = JapanEssentialsPackageId,
                    GrantedAt = now,
                    Source = "seed-package"
                },
                new UserEntitlement
                {
                    Id = Guid.Parse("77777777-7777-7777-7777-777777777702"),
                    UserId = DemoUserId,
                    AccessLevel = ContentAccessLevel.Subscription,
                    DestinationId = JapanDestinationId,
                    TravelPackageId = PremiumPackageId,
                    GrantedAt = now,
                    ExpiresAt = now.AddYears(1),
                    Source = "seed-subscription"
                }
            ]
        };

        demoUser.PasswordHash = passwordHasher.HashPassword(demoUser, "TravelDemo!2026");
        dbContext.AppUsers.Add(demoUser);
    }

    private static async Task NormalizeDemoDataAsync(TravelCompanionDbContext dbContext, IPasswordHasher<AppUser> passwordHasher)
    {
        var recommendations = await dbContext.Recommendations
            .Where(recommendation => recommendation.Id == FushimiInariRecommendationId
                || recommendation.Id == DotonboriRecommendationId)
            .ToListAsync();

        foreach (var recommendation in recommendations)
        {
            if (recommendation.Id == FushimiInariRecommendationId
                && recommendation.AccessLevel == ContentAccessLevel.Free)
            {
                recommendation.AccessLevel = ContentAccessLevel.Paid;
            }

            if (recommendation.Id == DotonboriRecommendationId
                && recommendation.AccessLevel == ContentAccessLevel.Free)
            {
                recommendation.AccessLevel = ContentAccessLevel.Subscription;
            }
        }

        var reservationsWithoutCity = await dbContext.Reservations
            .Include(reservation => reservation.Trip)
                .ThenInclude(trip => trip!.Destination)
            .Where(reservation => string.IsNullOrWhiteSpace(reservation.City))
            .ToListAsync();

        foreach (var reservation in reservationsWithoutCity)
        {
            reservation.City = InferCity(reservation);
        }

        var demoTrip = await dbContext.Trips
            .FirstOrDefaultAsync(trip => trip.Id == Guid.Parse("44444444-4444-4444-4444-444444444401"));

        if (demoTrip is not null && demoTrip.AppUserId is null)
        {
            demoTrip.AppUserId = DemoUserId;
        }

        var demoUser = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Id == DemoUserId);
        if (demoUser is not null && string.IsNullOrWhiteSpace(demoUser.PasswordHash))
        {
            demoUser.MustChangePassword = true;
            demoUser.TemporaryPasswordIssuedAt = DateTimeOffset.UtcNow;
            demoUser.PasswordHash = passwordHasher.HashPassword(demoUser, "TravelDemo!2026");
        }
    }

    private static string InferCity(Reservation reservation)
    {
        if (reservation.Address.Contains("Tokyo", StringComparison.OrdinalIgnoreCase))
        {
            return "Tokyo";
        }

        if (reservation.Address.Contains("Kyoto", StringComparison.OrdinalIgnoreCase))
        {
            return "Kyoto";
        }

        if (reservation.Address.Contains("Osaka", StringComparison.OrdinalIgnoreCase))
        {
            return "Osaka";
        }

        return reservation.Trip?.Destination?.Name ?? "Sin ciudad";
    }
}

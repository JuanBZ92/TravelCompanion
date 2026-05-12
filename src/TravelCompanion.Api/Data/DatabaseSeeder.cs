using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(TravelCompanionDbContext dbContext)
    {
        if (await dbContext.Destinations.AnyAsync())
        {
            return;
        }

        var japan = new Destination
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
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
                Id = Guid.Parse("22222222-2222-2222-2222-222222222201"),
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
                Id = Guid.Parse("22222222-2222-2222-2222-222222222202"),
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
                SuggestedDurationMinutes = 90
            },
            new Recommendation
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333302"),
                DestinationId = japan.Id,
                Title = "Fushimi Inari Taisha",
                Category = "Culture",
                Neighborhood = "Fushimi, Kyoto",
                Description = "Santuario de torii rojos. Conviene ir muy temprano o al atardecer para evitar multitudes.",
                Latitude = 34.967140m,
                Longitude = 135.772671m,
                SuggestedDurationMinutes = 120
            },
            new Recommendation
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333303"),
                DestinationId = japan.Id,
                Title = "Dotonbori",
                Category = "Nightlife",
                Neighborhood = "Namba, Osaka",
                Description = "Neones, street food y una buena primera noche en Osaka sin complicarse.",
                Latitude = 34.668723m,
                Longitude = 135.501297m,
                SuggestedDurationMinutes = 120
            }
        ]);

        var demoTrip = new Trip
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444401"),
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
                    LocationName = "Azabudai Hills",
                    Address = "1 Chome-2-4 Azabudai, Minato City, Tokyo",
                    ConfirmationCode = "DEMO-TLB-1026",
                    Notes = "Llegar 15 minutos antes. Llevar QR en el telefono."
                },
                new Reservation
                {
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555502"),
                    Date = new DateOnly(2026, 10, 9),
                    StartsAt = new TimeOnly(18, 0),
                    Title = "Cena omakase",
                    LocationName = "Sushi demo",
                    Address = "Shibuya City, Tokyo",
                    ConfirmationCode = "DEMO-SUSHI-1026",
                    Notes = "Avisar alergias con 48 horas de anticipacion."
                }
            ]
        };

        dbContext.Destinations.Add(japan);
        dbContext.Trips.Add(demoTrip);
        await dbContext.SaveChangesAsync();
    }
}

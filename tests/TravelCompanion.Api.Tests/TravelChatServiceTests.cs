using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class TravelChatServiceTests
{
    [Fact]
    public async Task CreatePlanAsync_returns_structured_cards_from_user_reservations()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Tsukiji Snack Walk",
            Category = "Food",
            Neighborhood = "Chuo, Tokyo",
            Description = "Local snacks in Tokyo before dinner.",
            Latitude = 35.665486m,
            Longitude = 139.770667m,
            SuggestedDurationMinutes = 90,
            AccessLevel = ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Proponeme un plan entre mis reservas de hoy",
                null,
                "Tokyo",
                new DateOnly(2026, 10, 6),
                new GeoPointDto(35.665000m, 139.770000m),
                "es-ES"),
            CancellationToken.None);

        Assert.Equal("plan_between_reservations", response.Intent);
        Assert.Null(response.MissingContext);
        Assert.NotEmpty(response.ConversationId);
        Assert.Single(response.Cards);
        Assert.Equal("Tsukiji Snack Walk", response.Cards[0].Title);
        Assert.NotEmpty(response.Cards[0].WhyItFits);
        Assert.NotEmpty(response.SuggestedReplies);
    }

    [Fact]
    public async Task CreatePlanAsync_returns_missing_context_when_no_reservations_exist()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10)
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.NotNull(response.MissingContext);
        Assert.Equal("date", response.MissingContext.Field);
        Assert.Empty(response.Cards);
    }

    [Fact]
    public async Task CreatePlanAsync_uses_model_message_when_available()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Tsukiji Snack Walk",
            Category = "Food",
            Neighborhood = "Chuo, Tokyo",
            Description = "Local snacks in Tokyo before dinner.",
            Latitude = 35.665486m,
            Longitude = 139.770667m,
            SuggestedDurationMinutes = 90,
            AccessLevel = ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(
            dbContext,
            new FakeTravelAiModelClient(new TravelAiModelResult(
                "Modelo: tenes una ventana tranquila para Tsukiji.",
                ["Menos caminata", "Guardar plan"])));

        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.Equal("Modelo: tenes una ventana tranquila para Tsukiji.", response.Message);
        Assert.Equal(["Menos caminata", "Guardar plan"], response.SuggestedReplies);
        Assert.Single(response.Cards);
    }

    [Fact]
    public async Task CreatePlanAsync_falls_back_when_model_fails()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Tsukiji Snack Walk",
            Category = "Food",
            Neighborhood = "Chuo, Tokyo",
            Description = "Local snacks in Tokyo before dinner.",
            Latitude = 35.665486m,
            Longitude = 139.770667m,
            SuggestedDurationMinutes = 90,
            AccessLevel = ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, new ThrowingTravelAiModelClient());
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.Contains("Te propongo este plan", response.Message);
        Assert.Single(response.Cards);
        Assert.NotEmpty(response.SuggestedReplies);
    }

    [Fact]
    public async Task CreatePlanAsync_changes_fallback_for_less_walking_request()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.AddRange(
            new Recommendation
            {
                Id = Guid.NewGuid(),
                DestinationId = destinationId,
                Title = "Nearby tea stop",
                Category = "Food",
                Neighborhood = "Chuo, Tokyo",
                Description = "Local tea and snacks in Tokyo.",
                Latitude = 35.665100m,
                Longitude = 139.770100m,
                SuggestedDurationMinutes = 45,
                AccessLevel = ContentAccessLevel.Free
            },
            new Recommendation
            {
                Id = Guid.NewGuid(),
                DestinationId = destinationId,
                Title = "Far culture walk",
                Category = "Culture",
                Neighborhood = "Chuo, Tokyo",
                Description = "A longer culture walk in Tokyo.",
                Latitude = 35.720000m,
                Longitude = 139.810000m,
                SuggestedDurationMinutes = 90,
                AccessLevel = ContentAccessLevel.Free
            });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Algo con menos caminata",
                null,
                "Tokyo",
                new DateOnly(2026, 10, 6),
                new GeoPointDto(35.665000m, 139.770000m),
                "es-ES"),
            CancellationToken.None);

        Assert.StartsWith("Busque una opcion con menos caminata", response.Message);
        Assert.Equal("Nearby tea stop", response.Cards[0].Title);
        Assert.Contains("Algo mas corto", response.SuggestedReplies);
    }

    [Fact]
    public async Task CreatePlanAsync_uses_directed_response_for_specific_follow_up_even_when_model_returns_generic_text()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Tsukiji Snack Walk",
            Category = "Food",
            Neighborhood = "Chuo, Tokyo",
            Description = "Local snacks in Tokyo before dinner.",
            Latitude = 35.665486m,
            Longitude = 139.770667m,
            SuggestedDurationMinutes = 90,
            AccessLevel = ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(
            dbContext,
            new FakeTravelAiModelClient(new TravelAiModelResult(
                "Modelo generico que no atiende el pedido.",
                ["Respuesta fija"])));
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Quiero algo de comida local", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.StartsWith("Busque algo de comida local", response.Message);
        Assert.Contains("Algo cultural", response.SuggestedReplies);
    }

    private static TravelCompanionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TravelCompanionDbContext(options);
    }

    private static TravelChatService CreateService(
        TravelCompanionDbContext dbContext,
        ITravelAiModelClient? modelClient = null)
    {
        return new TravelChatService(
            dbContext,
            new DeterministicRecommendationRanker(),
            modelClient ?? new FakeTravelAiModelClient(null),
            NullLogger<TravelChatService>.Instance);
    }

    private static AppUser CreateUser(Guid destinationId)
    {
        var userId = Guid.NewGuid();
        return new AppUser
        {
            Id = userId,
            Email = "demo@example.test",
            DisplayName = "Demo Traveler",
            Entitlements =
            [
                new UserEntitlement
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    AccessLevel = ContentAccessLevel.Free,
                    DestinationId = destinationId,
                    GrantedAt = DateTimeOffset.UtcNow,
                    Source = "test"
                }
            ]
        };
    }

    private static Reservation CreateReservation(string title, TimeOnly startsAt, string city)
    {
        return new Reservation
        {
            Id = Guid.NewGuid(),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 10, 6),
            StartsAt = startsAt,
            Title = title,
            City = city,
            LocationName = title,
            Address = $"{title} address",
            ConfirmationCode = "CONF",
            Notes = string.Empty
        };
    }

    private sealed class FakeTravelAiModelClient(TravelAiModelResult? result) : ITravelAiModelClient
    {
        public Task<TravelAiModelResult?> CreateStructuredResponseAsync(
            TravelAiModelRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingTravelAiModelClient : ITravelAiModelClient
    {
        public Task<TravelAiModelResult?> CreateStructuredResponseAsync(
            TravelAiModelRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Model unavailable.");
        }
    }
}

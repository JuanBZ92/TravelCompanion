using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class MobileDocsEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Docs_requires_bearer_session()
    {
        await using var factory = new TravelCompanionApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/mobile/docs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Docs_returns_flights_documents_and_lodging_for_current_user_trip()
    {
        await using var factory = new TravelCompanionApiFactory();
        var token = await factory.SeedDocsTripAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var docs = await client.GetFromJsonAsync<TravelDocsDto>("/api/mobile/docs", JsonOptions);

        Assert.NotNull(docs);
        Assert.Equal("Japan", docs.DestinationName);
        Assert.Equal("Docs Traveler", docs.TravelerName);

        Assert.NotNull(docs.Flights);
        Assert.Equal("Japan Airlines", docs.Flights.Airline);
        Assert.Equal("PNR123", docs.Flights.ConfirmationCode);
        var journey = Assert.Single(docs.Flights.Journeys);
        Assert.Equal("Ida", journey.Label);
        Assert.Equal("Buenos Aires -> Tokyo", journey.Route);
        Assert.Equal(2, journey.Legs.Count);
        Assert.Equal("EZE · Buenos Aires", journey.Legs[0].From);
        Assert.Equal("HND · Tokyo", journey.Legs[1].To);
        Assert.StartsWith("Escala en Madrid", journey.Legs[1].ConnectionNote);

        var hotelDocument = Assert.Single(docs.HotelDocuments);
        Assert.Equal(TravelDocumentCategory.Hotel, hotelDocument.Category);
        Assert.Equal("Tokyo hotel confirmation", hotelDocument.Title);
        Assert.Equal("/docs/tokyo-hotel.pdf", hotelDocument.FileUrl);

        var otherDocument = Assert.Single(docs.OtherDocuments);
        Assert.Equal("JR Pass voucher", otherDocument.Title);

        var hotel = Assert.Single(docs.Hotels);
        Assert.Equal("Tokyo", hotel.City);
        Assert.Equal("Hotel K5", hotel.Name);
        Assert.Equal("22/05 - 25/05", hotel.DateRange);
    }

    private sealed class TravelCompanionApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"mobile-docs-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TravelCompanionDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<TravelCompanionDbContext>>();
                services.AddDbContext<TravelCompanionDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName));

                services.RemoveAll<ITravelAiModelClient>();
                services.AddSingleton<ITravelAiModelClient, NullTravelAiModelClient>();
            });
        }

        public async Task<string> SeedDocsTripAsync()
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var sessionService = scope.ServiceProvider.GetRequiredService<UserSessionService>();

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "docs@example.test",
                DisplayName = "Docs Traveler",
                PasswordHash = string.Empty,
                MustChangePassword = false
            };
            var destination = new Destination
            {
                Id = Guid.NewGuid(),
                Name = "Japan",
                Slug = "japan",
                Country = "Japan",
                HeroImageUrl = "https://example.test/japan.jpg",
                ShortDescription = "Docs destination"
            };
            var trip = new Trip
            {
                Id = Guid.NewGuid(),
                AppUserId = user.Id,
                DestinationId = destination.Id,
                TravelerName = user.DisplayName,
                StartsOn = new DateOnly(2026, 5, 20),
                EndsOn = new DateOnly(2026, 5, 30),
                TimeZoneId = "Asia/Tokyo"
            };

            trip.Reservations.AddRange([
                new Reservation
                {
                    Id = Guid.NewGuid(),
                    TripId = trip.Id,
                    Type = ReservationType.Flight,
                    Date = new DateOnly(2026, 5, 20),
                    StartsAt = new TimeOnly(23, 55),
                    EndsOn = new DateOnly(2026, 5, 21),
                    EndsAt = new TimeOnly(17, 10),
                    Title = "Flight to Madrid",
                    City = "Buenos Aires",
                    LocationName = "Ezeiza",
                    Address = "EZE",
                    ConfirmationCode = "PNR123",
                    Notes = string.Empty,
                    Airline = "Japan Airlines",
                    FlightNumber = "JL100",
                    OriginName = "Buenos Aires",
                    OriginAirport = "EZE",
                    DestinationName = "Madrid",
                    DestinationAirport = "MAD"
                },
                new Reservation
                {
                    Id = Guid.NewGuid(),
                    TripId = trip.Id,
                    Type = ReservationType.Flight,
                    Date = new DateOnly(2026, 5, 21),
                    StartsAt = new TimeOnly(19, 30),
                    EndsOn = new DateOnly(2026, 5, 22),
                    EndsAt = new TimeOnly(17, 20),
                    Title = "Flight to Tokyo",
                    City = "Madrid",
                    LocationName = "Barajas",
                    Address = "MAD",
                    ConfirmationCode = "PNR123",
                    Notes = string.Empty,
                    Airline = "Japan Airlines",
                    FlightNumber = "JL101",
                    OriginName = "Madrid",
                    OriginAirport = "MAD",
                    DestinationName = "Tokyo",
                    DestinationAirport = "HND"
                },
                new Reservation
                {
                    Id = Guid.NewGuid(),
                    TripId = trip.Id,
                    Type = ReservationType.Lodging,
                    Date = new DateOnly(2026, 5, 22),
                    StartsAt = new TimeOnly(15, 0),
                    EndsOn = new DateOnly(2026, 5, 25),
                    EndsAt = new TimeOnly(11, 0),
                    Title = "Tokyo hotel",
                    City = "Tokyo",
                    LocationName = "Hotel K5",
                    Address = "Tokyo address",
                    ConfirmationCode = "HOTEL123",
                    Notes = string.Empty
                }
            ]);

            trip.Documents.AddRange([
                new TravelDocument
                {
                    Id = Guid.NewGuid(),
                    TripId = trip.Id,
                    Category = TravelDocumentCategory.Hotel,
                    Title = "Tokyo hotel confirmation",
                    Subtitle = "PDF confirmacion",
                    FileUrl = "/docs/tokyo-hotel.pdf",
                    SortOrder = 10
                },
                new TravelDocument
                {
                    Id = Guid.NewGuid(),
                    TripId = trip.Id,
                    Category = TravelDocumentCategory.Other,
                    Title = "JR Pass voucher",
                    Subtitle = "Voucher trenes",
                    FileUrl = "https://example.test/docs/jr.pdf",
                    SortOrder = 20
                }
            ]);

            dbContext.AppUsers.Add(user);
            dbContext.Destinations.Add(destination);
            dbContext.Trips.Add(trip);
            await dbContext.SaveChangesAsync();

            var (session, token) = await sessionService.CreateSessionAsync(user);
            session.LastSeenAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
            return token;
        }
    }

    private sealed class NullTravelAiModelClient : ITravelAiModelClient
    {
        public Task<TravelAiModelResult?> CreateStructuredResponseAsync(
            TravelAiModelRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<TravelAiModelResult?>(null);
        }
    }
}

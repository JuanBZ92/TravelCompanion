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
    private static readonly IReadOnlySet<Guid> PremiumPackageRecommendationIds = Enumerable
        .Range(328, 9)
        .Select(CreateRecommendationId)
        .Append(DotonboriRecommendationId)
        .ToHashSet();
    private static readonly Guid OmakaseReservationId = Guid.Parse("55555555-5555-5555-5555-555555555502");
    private static readonly Guid DemoUserId = Guid.Parse("66666666-6666-6666-6666-666666666601");
    private static readonly Guid FreeUserId = Guid.Parse("66666666-6666-6666-6666-666666666602");
    private static readonly Guid SubscriptionUserId = Guid.Parse("66666666-6666-6666-6666-666666666603");
    private static readonly Guid PaidUserId = Guid.Parse("66666666-6666-6666-6666-666666666604");
    private static readonly Guid FreeTripId = Guid.Parse("44444444-4444-4444-4444-444444444402");
    private static readonly Guid SubscriptionTripId = Guid.Parse("44444444-4444-4444-4444-444444444403");
    private static readonly Guid PaidTripId = Guid.Parse("44444444-4444-4444-4444-444444444404");
    private const string DemoUserEmail = "demo@travelcompanion.local";
    private const string FreeUserEmail = "usuariofree@travelcompanion.local";
    private const string SubscriptionUserEmail = "usuariosub@travelcompanion.local";
    private const string PaidUserEmail = "usuariopaid@travelcompanion.local";

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
        await SeedAdditionalJapanRecommendationsAsync(dbContext);
        await SeedAccessScenarioUsersAsync(dbContext, passwordHasher);
        await SeedDemoTravelPreferenceProfilesAsync(dbContext);
        await SeedDemoTravelDocumentsAsync(dbContext);

        await dbContext.SaveChangesAsync();
        await SyncSeedRecommendationPackageLinksAsync(dbContext);
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
            TimeZoneId = "Asia/Tokyo",
            HeroImageUrl = "https://images.unsplash.com/photo-1542051841857-5f90071e7989",
            ShortDescription = "Tokyo, Kyoto y Osaka con planes curados, barrios caminables y reservas organizadas."
        };

        var essentialsPackage = new TravelPackage
        {
            Id = JapanEssentialsPackageId,
            DestinationId = japan.Id,
            Name = "Japon Essentials",
            Slug = "japon-essentials",
            Description = "Guia curada con recomendaciones, mapa y tips practicos para un primer viaje.",
            Price = 19.99m,
            Currency = "USD",
            IsSubscription = false
        };

        var premiumPackage = new TravelPackage
        {
            Id = PremiumPackageId,
            DestinationId = japan.Id,
            Name = "Japon Premium Pack",
            Slug = "japon-premium-pack",
            Description = "Recomendaciones premium para experiencias, restaurantes y rutas mas curadas.",
            Price = 8.99m,
            Currency = "USD",
            IsSubscription = false
        };

        japan.Packages.AddRange([essentialsPackage, premiumPackage]);

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
                AccessLevel = ContentAccessLevel.Paid,
                Packages = [essentialsPackage]
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
                AccessLevel = ContentAccessLevel.Paid,
                Packages = [premiumPackage]
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
                    Type = ReservationType.Event,
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
                    Type = ReservationType.Event,
                    Date = new DateOnly(2026, 10, 9),
                    StartsAt = new TimeOnly(18, 0),
                    Title = "Cena omakase",
                    City = "Tokyo",
                    LocationName = "Sushi demo",
                    Address = "Shibuya City, Tokyo",
                    ConfirmationCode = "DEMO-SUSHI-1026",
                    Notes = "Avisar alergias con 48 horas de anticipacion."
                },
                new Reservation
                {
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555503"),
                    Type = ReservationType.Flight,
                    Date = new DateOnly(2026, 10, 5),
                    StartsAt = new TimeOnly(13, 30),
                    EndsOn = new DateOnly(2026, 10, 6),
                    EndsAt = new TimeOnly(9, 25),
                    Title = "Vuelo a Tokyo",
                    City = "Tokyo",
                    LocationName = "Haneda Airport",
                    Address = "Haneda Airport, Tokyo",
                    ConfirmationCode = "DEMO-FLT-1026",
                    Notes = "Llegar al aeropuerto con 3 horas de anticipacion.",
                    Airline = "Japan Airlines",
                    FlightNumber = "JL0042",
                    OriginName = "Buenos Aires",
                    DestinationName = "Tokyo",
                    OriginAirport = "EZE",
                    DestinationAirport = "HND"
                },
                new Reservation
                {
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555504"),
                    Type = ReservationType.Lodging,
                    Date = new DateOnly(2026, 10, 6),
                    StartsAt = new TimeOnly(15, 0),
                    EndsOn = new DateOnly(2026, 10, 10),
                    EndsAt = new TimeOnly(11, 0),
                    Title = "Hotel Tokyo",
                    City = "Tokyo",
                    LocationName = "Hotel demo Ginza",
                    Address = "Ginza, Chuo City, Tokyo",
                    ConfirmationCode = "DEMO-HTL-1026",
                    Notes = "Check-in desde las 15:00. Pedir habitacion alta si esta disponible."
                }
            ]
        };
        EnsureTripPin(demoTrip, "1908");

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
                    AccessLevel = ContentAccessLevel.Paid,
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
                    TravelPackageId = null,
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
        var premiumPackage = await dbContext.TravelPackages.FindAsync(PremiumPackageId);
        if (premiumPackage is not null)
        {
            premiumPackage.Name = "Japon Premium Pack";
            premiumPackage.Slug = "japon-premium-pack";
            premiumPackage.Description = "Recomendaciones premium para experiencias, restaurantes y rutas mas curadas.";
            premiumPackage.IsSubscription = false;
        }

        var recommendations = await dbContext.Recommendations
            .Include(recommendation => recommendation.Packages)
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
                && recommendation.AccessLevel != ContentAccessLevel.Paid)
            {
                recommendation.AccessLevel = ContentAccessLevel.Paid;
            }

            await ApplyRecommendationPackageLinksAsync(dbContext, recommendation);
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

        if (demoTrip is not null)
        {
            EnsureTripPin(demoTrip, "1908");

            await AddDemoReservationIfMissingAsync(
                dbContext,
                demoTrip.Id,
                Guid.Parse("55555555-5555-5555-5555-555555555503"),
                new Reservation
                {
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555503"),
                    Type = ReservationType.Flight,
                    Date = new DateOnly(2026, 10, 5),
                    StartsAt = new TimeOnly(13, 30),
                    EndsOn = new DateOnly(2026, 10, 6),
                    EndsAt = new TimeOnly(9, 25),
                    Title = "Vuelo a Tokyo",
                    City = "Tokyo",
                    LocationName = "Haneda Airport",
                    Address = "Haneda Airport, Tokyo",
                    ConfirmationCode = "DEMO-FLT-1026",
                    Notes = "Llegar al aeropuerto con 3 horas de anticipacion.",
                    Airline = "Japan Airlines",
                    FlightNumber = "JL0042",
                    OriginName = "Buenos Aires",
                    DestinationName = "Tokyo",
                    OriginAirport = "EZE",
                    DestinationAirport = "HND"
                });

            await AddDemoReservationIfMissingAsync(
                dbContext,
                demoTrip.Id,
                Guid.Parse("55555555-5555-5555-5555-555555555504"),
                new Reservation
                {
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555504"),
                    Type = ReservationType.Lodging,
                    Date = new DateOnly(2026, 10, 6),
                    StartsAt = new TimeOnly(15, 0),
                    EndsOn = new DateOnly(2026, 10, 10),
                    EndsAt = new TimeOnly(11, 0),
                    Title = "Hotel Tokyo",
                    City = "Tokyo",
                    LocationName = "Hotel demo Ginza",
                    Address = "Ginza, Chuo City, Tokyo",
                    ConfirmationCode = "DEMO-HTL-1026",
                    Notes = "Check-in desde las 15:00. Pedir habitacion alta si esta disponible."
                });
        }

        var demoUser = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Id == DemoUserId);
        if (demoUser is not null && string.IsNullOrWhiteSpace(demoUser.PasswordHash))
        {
            demoUser.MustChangePassword = true;
            demoUser.TemporaryPasswordIssuedAt = DateTimeOffset.UtcNow;
            demoUser.PasswordHash = passwordHasher.HashPassword(demoUser, "TravelDemo!2026");
        }

        var demoSubscription = await dbContext.UserEntitlements
            .FirstOrDefaultAsync(entitlement => entitlement.Id == Guid.Parse("77777777-7777-7777-7777-777777777702"));
        if (demoSubscription is not null)
        {
            demoSubscription.AccessLevel = ContentAccessLevel.Subscription;
            demoSubscription.DestinationId = JapanDestinationId;
            demoSubscription.TravelPackageId = null;
            demoSubscription.Source = "seed-subscription";
        }
    }

    private static async Task SeedAdditionalJapanRecommendationsAsync(TravelCompanionDbContext dbContext)
    {
        foreach (var recommendation in CreateAdditionalJapanRecommendations())
        {
            var existingRecommendation = await dbContext.Recommendations
                .Include(existing => existing.Packages)
                .FirstOrDefaultAsync(existing => existing.Id == recommendation.Id);

            if (existingRecommendation is null)
            {
                await ApplyRecommendationPackageLinksAsync(dbContext, recommendation);
                dbContext.Recommendations.Add(recommendation);
                continue;
            }

            ApplyRecommendation(existingRecommendation, recommendation);
            await ApplyRecommendationPackageLinksAsync(dbContext, existingRecommendation);
        }
    }

    private static async Task ApplyRecommendationPackageLinksAsync(
        TravelCompanionDbContext dbContext,
        Recommendation recommendation)
    {
        recommendation.Packages.Clear();

        var packageId = GetSeedPackageId(recommendation);

        if (!packageId.HasValue)
        {
            return;
        }

        var package = dbContext.ChangeTracker
            .Entries<TravelPackage>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(existingPackage => existingPackage.Id == packageId.Value)
            ?? await dbContext.TravelPackages.FindAsync(packageId.Value);

        if (package is not null)
        {
            recommendation.Packages.Add(package);
        }
    }

    private static void ApplyRecommendation(Recommendation target, Recommendation source)
    {
        target.DestinationId = source.DestinationId;
        target.Title = source.Title;
        target.Category = source.Category;
        target.Neighborhood = source.Neighborhood;
        target.Description = source.Description;
        target.Tags = source.Tags.ToList();
        target.PriceLevel = source.PriceLevel;
        target.Latitude = source.Latitude;
        target.Longitude = source.Longitude;
        target.SuggestedDurationMinutes = source.SuggestedDurationMinutes;
        target.Rating = source.Rating;
        target.OpeningHours = source.OpeningHours;
        target.AccessLevel = source.AccessLevel;
    }

    private static IReadOnlyCollection<Recommendation> CreateAdditionalJapanRecommendations() =>
    [
        CreateJapanRecommendation(304, "Meiji Jingu temprano", "Culture", "Harajuku, Tokyo", "Santuario tranquilo para empezar el dia antes de cruzar hacia Omotesando.", 35.676398m, 139.699326m, 75, ContentAccessLevel.Free),
        CreateJapanRecommendation(305, "Yanaka Ginza", "Food", "Yanaka, Tokyo", "Calle barrial con snacks, tiendas chicas y ritmo mas local que turistico.", 35.727609m, 139.766540m, 90, ContentAccessLevel.Free),
        CreateJapanRecommendation(306, "Kappabashi Street", "Shopping", "Asakusa, Tokyo", "Zona ideal para cuchillos, ceramica, utensilios y regalos faciles de llevar.", 35.713741m, 139.788003m, 90, ContentAccessLevel.Free),
        CreateJapanRecommendation(307, "Tokyo Metropolitan Government Building", "Viewpoint", "Shinjuku, Tokyo", "Mirador gratuito para ubicarse visualmente en la ciudad.", 35.689634m, 139.692101m, 60, ContentAccessLevel.Free),
        CreateJapanRecommendation(308, "Yoyogi Park picnic", "Nature", "Shibuya, Tokyo", "Parada verde y flexible entre Harajuku, Shibuya y Meiji Jingu.", 35.671667m, 139.694944m, 75, ContentAccessLevel.Free),
        CreateJapanRecommendation(309, "Nishiki Market", "Food", "Nakagyo, Kyoto", "Mercado compacto para probar sabores de Kyoto sin plan demasiado rigido.", 35.005025m, 135.764723m, 90, ContentAccessLevel.Free),
        CreateJapanRecommendation(310, "Kiyomizu-dera approach", "Culture", "Higashiyama, Kyoto", "Calles historicas con templos, tiendas y buenas vistas al atardecer.", 34.994856m, 135.785046m, 120, ContentAccessLevel.Free),
        CreateJapanRecommendation(311, "Arashiyama riverside", "Nature", "Arashiyama, Kyoto", "Paseo junto al rio para bajar el ritmo despues del bosque de bambu.", 35.013411m, 135.677829m, 90, ContentAccessLevel.Free),
        CreateJapanRecommendation(312, "Osaka Castle Park", "Culture", "Chuo, Osaka", "Parque amplio con historia, fotos faciles y buena pausa entre barrios.", 34.687315m, 135.526201m, 105, ContentAccessLevel.Free),
        CreateJapanRecommendation(313, "Shinsekai walk", "Food", "Naniwa, Osaka", "Zona clasica para kushikatsu, neones y una caminata nocturna informal.", 34.652499m, 135.506306m, 90, ContentAccessLevel.Free),
        CreateJapanRecommendation(314, "Hiroshima Peace Memorial Park", "Culture", "Naka, Hiroshima", "Visita sobria y necesaria para entender la ciudad con contexto.", 34.395483m, 132.453592m, 120, ContentAccessLevel.Free),
        CreateJapanRecommendation(315, "Miyajima torii view", "Nature", "Miyajima, Hiroshima", "Vista iconica del torii flotante y caminata suave por la isla.", 34.295990m, 132.319733m, 180, ContentAccessLevel.Free),

        CreateJapanRecommendation(316, "Shibuya Sky sunset slot", "Viewpoint", "Shibuya, Tokyo", "Reserva recomendada para una vista limpia de Tokyo en horario dorado.", 35.658447m, 139.702164m, 90, ContentAccessLevel.Paid),
        CreateJapanRecommendation(317, "TeamLab Planets", "Culture", "Toyosu, Tokyo", "Experiencia inmersiva que conviene reservar con horario fijo.", 35.649130m, 139.789804m, 120, ContentAccessLevel.Paid),
        CreateJapanRecommendation(318, "Ginza depachika route", "Food", "Ginza, Tokyo", "Ruta curada por subsuelos gourmet para comprar comida de alta calidad.", 35.672114m, 139.765519m, 75, ContentAccessLevel.Paid),
        CreateJapanRecommendation(319, "Shimokitazawa vintage map", "Shopping", "Setagaya, Tokyo", "Circuito corto de tiendas vintage y cafes sin perderse entre calles.", 35.661520m, 139.666863m, 120, ContentAccessLevel.Paid),
        CreateJapanRecommendation(320, "Gion evening walk", "Culture", "Gion, Kyoto", "Ruta de tarde por Hanamikoji y Shirakawa con timing cuidado.", 35.003655m, 135.775162m, 90, ContentAccessLevel.Paid),
        CreateJapanRecommendation(321, "Tea ceremony in Kyoto", "Culture", "Higashiyama, Kyoto", "Experiencia reservable para entender ritual, etiqueta y matcha.", 34.997102m, 135.776037m, 75, ContentAccessLevel.Paid),
        CreateJapanRecommendation(322, "Kurama to Kibune", "Nature", "Northern Kyoto", "Caminata de medio dia con onsen y regreso simple a Kyoto.", 35.112980m, 135.772003m, 240, ContentAccessLevel.Paid),
        CreateJapanRecommendation(323, "Kuromon Market picks", "Food", "Nipponbashi, Osaka", "Selecciones concretas para comer bien sin caer en puestos flojos.", 34.665411m, 135.506316m, 90, ContentAccessLevel.Paid),
        CreateJapanRecommendation(324, "Umeda Sky Building", "Viewpoint", "Kita, Osaka", "Mirador comodo para cerrar el dia en Osaka con buena conexion.", 34.705277m, 135.489683m, 75, ContentAccessLevel.Paid),
        CreateJapanRecommendation(325, "Himeji Castle day trip", "Culture", "Himeji", "Excursion eficiente desde Osaka o Kyoto al castillo mas fotogenico.", 34.839449m, 134.693905m, 300, ContentAccessLevel.Paid),
        CreateJapanRecommendation(326, "Naoshima art day", "Culture", "Naoshima", "Plan de arte contemporaneo con ferries y tiempos armados.", 34.459723m, 133.995620m, 420, ContentAccessLevel.Paid),
        CreateJapanRecommendation(327, "Kobe beef dinner", "Food", "Sannomiya, Kobe", "Reserva sugerida para una cena especial sin improvisar.", 34.694139m, 135.194739m, 120, ContentAccessLevel.Paid),

        CreateJapanRecommendation(328, "Tokyo first-night food crawl", "Food", "Ebisu, Tokyo", "Itinerario nocturno suave para llegar cansado y aun asi comer muy bien.", 35.646690m, 139.710106m, 150, ContentAccessLevel.Paid),
        CreateJapanRecommendation(329, "Private sake tasting", "Food", "Nihonbashi, Tokyo", "Degustacion guiada para entender estilos de sake sin hacerlo tecnico.", 35.682839m, 139.774502m, 105, ContentAccessLevel.Paid),
        CreateJapanRecommendation(330, "Kamakura coastal day", "Nature", "Kamakura", "Dia armado entre templos, tren local y costa, evitando traslados torpes.", 35.319225m, 139.546686m, 360, ContentAccessLevel.Paid),
        CreateJapanRecommendation(331, "Hakone overnight route", "Nature", "Hakone", "Ruta de una noche con ryokan, lago y vistas al Fuji si el clima acompana.", 35.232382m, 139.106935m, 480, ContentAccessLevel.Paid),
        CreateJapanRecommendation(332, "Kyoto hidden gardens", "Culture", "Kyoto", "Seleccion de jardines menos obvios para equilibrar templos famosos.", 35.026244m, 135.798047m, 180, ContentAccessLevel.Paid),
        CreateJapanRecommendation(333, "Pontocho dinner shortlist", "Food", "Pontocho, Kyoto", "Lista curada de restaurantes por presupuesto y disponibilidad tipica.", 35.006043m, 135.770013m, 120, ContentAccessLevel.Paid),
        CreateJapanRecommendation(334, "Nara with lunch timing", "Culture", "Nara", "Excursion a Nara con orden sugerido para evitar horas pico.", 34.685087m, 135.805000m, 300, ContentAccessLevel.Paid),
        CreateJapanRecommendation(335, "Osaka cocktail night", "Nightlife", "Kitashinchi, Osaka", "Bares tranquilos y elegantes para una noche adulta en Osaka.", 34.696754m, 135.497568m, 150, ContentAccessLevel.Paid),
        CreateJapanRecommendation(336, "Hiroshima okonomiyaki counter", "Food", "Hiroshima", "Counter recomendado para probar estilo Hiroshima sin hacer cola eterna.", 34.392801m, 132.461683m, 90, ContentAccessLevel.Paid),
        CreateJapanRecommendation(337, "Miyajima low tide timing", "Nature", "Miyajima", "Plan ajustado a marea para ver el torii y subir parcialmente el monte.", 34.279632m, 132.315007m, 300, ContentAccessLevel.Subscription),
        CreateJapanRecommendation(338, "Kanazawa garden and sushi", "Culture", "Kanazawa", "Dia elegante entre Kenrokuen, barrios historicos y sushi local.", 36.561325m, 136.656205m, 360, ContentAccessLevel.Subscription),
        CreateJapanRecommendation(339, "Takayama old town morning", "Culture", "Takayama", "Recorrido de manana para mercado, casco antiguo y comida regional.", 36.142849m, 137.252765m, 240, ContentAccessLevel.Subscription)
    ];

    private static Recommendation CreateJapanRecommendation(
        int idSuffix,
        string title,
        string category,
        string neighborhood,
        string description,
        decimal latitude,
        decimal longitude,
        int suggestedDurationMinutes,
        ContentAccessLevel accessLevel) =>
        new()
        {
            Id = CreateRecommendationId(idSuffix),
            DestinationId = JapanDestinationId,
            Title = title,
            Category = category,
            Neighborhood = neighborhood,
            Description = description,
            Tags = CreateRecommendationTags(category, description),
            PriceLevel = accessLevel == ContentAccessLevel.Free ? "low" : "medium",
            Latitude = latitude,
            Longitude = longitude,
            SuggestedDurationMinutes = suggestedDurationMinutes,
            Rating = accessLevel == ContentAccessLevel.Free ? 4.2 : 4.5,
            OpeningHours = CreateOpeningHours(category),
            AccessLevel = accessLevel
        };

    private static List<string> CreateRecommendationTags(string category, string description)
    {
        var tags = new List<string> { category.ToLowerInvariant() };
        if (description.Contains("snack", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("snacks");
        }

        if (description.Contains("cafe", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("cafe");
        }

        if (description.Contains("gratis", StringComparison.OrdinalIgnoreCase)
            || description.Contains("gratuito", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("free");
        }

        return tags;
    }

    private static string CreateOpeningHours(string category)
    {
        return category switch
        {
            "Nightlife" => "18:00-02:00",
            "Food" => "11:00-22:00",
            "Shopping" => "10:00-20:00",
            _ => "09:00-18:00"
        };
    }

    private static Guid? GetSeedPackageId(Recommendation recommendation)
    {
        if (recommendation.AccessLevel != ContentAccessLevel.Paid)
        {
            return null;
        }

        return PremiumPackageRecommendationIds.Contains(recommendation.Id)
            ? PremiumPackageId
            : JapanEssentialsPackageId;
    }

    private static Guid CreateRecommendationId(int idSuffix) =>
        Guid.Parse($"33333333-3333-3333-3333-333333333{idSuffix:000}");

    private static async Task SyncSeedRecommendationPackageLinksAsync(TravelCompanionDbContext dbContext)
    {
        var seedRecommendationIds = CreateAdditionalJapanRecommendations()
            .Select(recommendation => recommendation.Id)
            .Append(FushimiInariRecommendationId)
            .Append(DotonboriRecommendationId)
            .ToHashSet();

        var joinRows = dbContext.Set<Dictionary<string, object>>("RecommendationTravelPackages");
        var existingRows = await joinRows
            .Where(row => seedRecommendationIds.Contains(EF.Property<Guid>(row, "RecommendationId")))
            .ToListAsync();

        joinRows.RemoveRange(existingRows);
        await dbContext.SaveChangesAsync();

        var paidSeedRecommendations = await dbContext.Recommendations
            .AsNoTracking()
            .Where(recommendation => seedRecommendationIds.Contains(recommendation.Id))
            .Where(recommendation => recommendation.AccessLevel == ContentAccessLevel.Paid)
            .Select(recommendation => new
            {
                recommendation.Id,
                recommendation.AccessLevel
            })
            .ToListAsync();

        foreach (var recommendation in paidSeedRecommendations)
        {
            var packageId = PremiumPackageRecommendationIds.Contains(recommendation.Id)
                ? PremiumPackageId
                : JapanEssentialsPackageId;

            joinRows.Add(new Dictionary<string, object>
            {
                ["RecommendationId"] = recommendation.Id,
                ["TravelPackageId"] = packageId
            });
        }
    }

    private static async Task SeedAccessScenarioUsersAsync(TravelCompanionDbContext dbContext, IPasswordHasher<AppUser> passwordHasher)
    {
        await EnsureScenarioUserAsync(
            dbContext,
            passwordHasher,
            FreeUserId,
            FreeUserEmail,
            "UsuarioFree",
            "PasswordFree");

        await EnsureScenarioUserAsync(
            dbContext,
            passwordHasher,
            SubscriptionUserId,
            SubscriptionUserEmail,
            "UsuarioSub",
            "PasswordSub",
            ContentAccessLevel.Subscription,
            packageId: null,
            expiresAt: DateTimeOffset.UtcNow.AddYears(1));

        await EnsureScenarioUserAsync(
            dbContext,
            passwordHasher,
            PaidUserId,
            PaidUserEmail,
            "UsuarioPaid",
            "PasswordPAid",
            ContentAccessLevel.Paid,
            JapanEssentialsPackageId);

        await EnsureScenarioTripAsync(
            dbContext,
            FreeTripId,
            FreeUserId,
            "UsuarioFree",
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 16),
            "1001",
            CreateFreeUserReservations());

        await EnsureScenarioTripAsync(
            dbContext,
            SubscriptionTripId,
            SubscriptionUserId,
            "UsuarioSub",
            new DateOnly(2026, 11, 18),
            new DateOnly(2026, 12, 3),
            "2002",
            CreateSubscriptionUserReservations());

        await EnsureScenarioTripAsync(
            dbContext,
            PaidTripId,
            PaidUserId,
            "UsuarioPaid",
            new DateOnly(2026, 12, 4),
            new DateOnly(2026, 12, 24),
            "3003",
            CreatePaidUserReservations());
    }

    private static async Task EnsureScenarioUserAsync(
        TravelCompanionDbContext dbContext,
        IPasswordHasher<AppUser> passwordHasher,
        Guid userId,
        string email,
        string displayName,
        string password,
        ContentAccessLevel? accessLevel = null,
        Guid? packageId = null,
        DateTimeOffset? expiresAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        var user = await dbContext.AppUsers
            .Include(existingUser => existingUser.Entitlements)
            .FirstOrDefaultAsync(existingUser => existingUser.Email == email);

        if (user is null)
        {
            user = new AppUser
            {
                Id = userId,
                Email = email,
                DisplayName = displayName
            };

            dbContext.AppUsers.Add(user);
        }
        else
        {
            user.Id = userId;
            user.DisplayName = displayName;
        }

        user.MustChangePassword = false;
        user.TemporaryPasswordIssuedAt = null;
        user.PasswordChangedAt = now;
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        if (accessLevel is null)
        {
            dbContext.UserEntitlements.RemoveRange(user.Entitlements);
            user.Entitlements.Clear();
            return;
        }

        var entitlementId = accessLevel == ContentAccessLevel.Subscription
            ? Guid.Parse("77777777-7777-7777-7777-777777777703")
            : Guid.Parse("77777777-7777-7777-7777-777777777704");

        var obsoleteEntitlements = user.Entitlements
            .Where(entitlement => entitlement.Id != entitlementId)
            .ToList();

        dbContext.UserEntitlements.RemoveRange(obsoleteEntitlements);

        var entitlement = user.Entitlements.FirstOrDefault(existingEntitlement => existingEntitlement.Id == entitlementId);
        if (entitlement is null)
        {
            entitlement = new UserEntitlement
            {
                Id = entitlementId,
                UserId = user.Id,
                Source = "seed-access-scenario"
            };

            user.Entitlements.Add(entitlement);
        }

        entitlement.AccessLevel = accessLevel.Value;
        entitlement.DestinationId = accessLevel == ContentAccessLevel.Subscription
            ? JapanDestinationId
            : null;
        entitlement.TravelPackageId = packageId;
        entitlement.GrantedAt = now;
        entitlement.ExpiresAt = expiresAt;
        entitlement.Source = "seed-access-scenario";
    }

    private static async Task EnsureScenarioTripAsync(
        TravelCompanionDbContext dbContext,
        Guid tripId,
        Guid userId,
        string travelerName,
        DateOnly startsOn,
        DateOnly endsOn,
        string accessPin,
        IReadOnlyCollection<Reservation> reservations)
    {
        var trip = await dbContext.Trips
            .Include(existingTrip => existingTrip.Reservations)
            .FirstOrDefaultAsync(existingTrip => existingTrip.Id == tripId);

        if (trip is null)
        {
            trip = new Trip
            {
                Id = tripId,
                AppUserId = userId,
                DestinationId = JapanDestinationId,
                TravelerName = travelerName,
                StartsOn = startsOn,
                EndsOn = endsOn
            };

            dbContext.Trips.Add(trip);
        }

        trip.AppUserId = userId;
        trip.DestinationId = JapanDestinationId;
        trip.TravelerName = travelerName;
        trip.StartsOn = startsOn;
        trip.EndsOn = endsOn;
        EnsureTripPin(trip, accessPin);

        foreach (var reservation in reservations)
        {
            var existingReservation = trip.Reservations.FirstOrDefault(existing => existing.Id == reservation.Id);
            if (existingReservation is null)
            {
                reservation.TripId = trip.Id;
                trip.Reservations.Add(reservation);
                continue;
            }

            ApplyReservation(existingReservation, reservation);
        }
    }

    private static void EnsureTripPin(Trip trip, string pin)
    {
        if (!string.IsNullOrWhiteSpace(trip.AccessPinHash))
        {
            return;
        }

        var pinHasher = new PasswordHasher<Trip>();
        trip.AccessPinHash = pinHasher.HashPassword(trip, pin);
        trip.AccessPinUpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ApplyReservation(Reservation target, Reservation source)
    {
        target.Type = source.Type;
        target.Date = source.Date;
        target.StartsAt = source.StartsAt;
        target.EndsOn = source.EndsOn;
        target.EndsAt = source.EndsAt;
        target.Title = source.Title;
        target.City = source.City;
        target.LocationName = source.LocationName;
        target.Address = source.Address;
        target.ConfirmationCode = source.ConfirmationCode;
        target.Notes = source.Notes;
        target.Airline = source.Airline;
        target.FlightNumber = source.FlightNumber;
        target.OriginName = source.OriginName;
        target.DestinationName = source.DestinationName;
        target.OriginAirport = source.OriginAirport;
        target.DestinationAirport = source.DestinationAirport;
    }

    private static IReadOnlyCollection<Reservation> CreateFreeUserReservations() =>
    [
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555601"),
            Type = ReservationType.Flight,
            Date = new DateOnly(2026, 11, 2),
            StartsAt = new TimeOnly(22, 10),
            EndsOn = new DateOnly(2026, 11, 3),
            EndsAt = new TimeOnly(18, 35),
            Title = "Vuelo Madrid a Tokyo",
            City = "Tokyo",
            LocationName = "Haneda Airport",
            Address = "Haneda Airport, Tokyo",
            ConfirmationCode = "FREE-FLT-MADHND",
            Notes = "Escala incluida. Revisar equipaje facturado.",
            Airline = "Iberia / Japan Airlines",
            FlightNumber = "IB281-JL042",
            OriginName = "Madrid",
            DestinationName = "Tokyo",
            OriginAirport = "MAD",
            DestinationAirport = "HND"
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555602"),
            Type = ReservationType.Lodging,
            Date = new DateOnly(2026, 11, 3),
            StartsAt = new TimeOnly(15, 0),
            EndsOn = new DateOnly(2026, 11, 7),
            EndsAt = new TimeOnly(11, 0),
            Title = "Hotel en Shinjuku",
            City = "Tokyo",
            LocationName = "Nohga Hotel Shinjuku",
            Address = "Shinjuku City, Tokyo",
            ConfirmationCode = "FREE-TYO-1103",
            Notes = "Check-in desde las 15:00. Cerca de estacion Shinjuku."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555603"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 11, 4),
            StartsAt = new TimeOnly(9, 0),
            Title = "Desayuno y paseo por Tsukiji",
            City = "Tokyo",
            LocationName = "Tsukiji Outer Market",
            Address = "4 Chome-16-2 Tsukiji, Chuo City, Tokyo",
            ConfirmationCode = "FREE-TSUKIJI",
            Notes = "Plan libre. Ir temprano para evitar multitudes."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555604"),
            Type = ReservationType.Flight,
            Date = new DateOnly(2026, 11, 7),
            StartsAt = new TimeOnly(11, 30),
            EndsOn = new DateOnly(2026, 11, 7),
            EndsAt = new TimeOnly(12, 45),
            Title = "Vuelo Tokyo a Osaka",
            City = "Osaka",
            LocationName = "Itami Airport",
            Address = "Osaka International Airport, Osaka",
            ConfirmationCode = "FREE-FLT-TYOOSA",
            Notes = "Traslado corto hacia Namba al llegar.",
            Airline = "ANA",
            FlightNumber = "NH021",
            OriginName = "Tokyo",
            DestinationName = "Osaka",
            OriginAirport = "HND",
            DestinationAirport = "ITM"
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555605"),
            Type = ReservationType.Lodging,
            Date = new DateOnly(2026, 11, 7),
            StartsAt = new TimeOnly(15, 0),
            EndsOn = new DateOnly(2026, 11, 12),
            EndsAt = new TimeOnly(10, 30),
            Title = "Hotel en Namba",
            City = "Osaka",
            LocationName = "Hotel Vista Osaka Namba",
            Address = "Namba, Chuo Ward, Osaka",
            ConfirmationCode = "FREE-OSA-1107",
            Notes = "Guardar maletas si llegan antes del check-in."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555606"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 11, 9),
            StartsAt = new TimeOnly(19, 30),
            Title = "Cena casual en Dotonbori",
            City = "Osaka",
            LocationName = "Dotonbori",
            Address = "Dotonbori, Chuo Ward, Osaka",
            ConfirmationCode = "FREE-DOTONBORI",
            Notes = "Sin reserva formal. Probar takoyaki y okonomiyaki."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555607"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 11, 12),
            StartsAt = new TimeOnly(10, 5),
            Title = "Traslado Osaka a Kyoto",
            City = "Kyoto",
            LocationName = "Kyoto Station",
            Address = "Kyoto Station, Kyoto",
            ConfirmationCode = "FREE-TRANSFER-KYO",
            Notes = "Usar tren desde Osaka. Guardar equipaje al llegar si aun no hay check-in.",
            OriginName = "Osaka",
            DestinationName = "Kyoto",
            OriginAirport = null,
            DestinationAirport = null
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555608"),
            Type = ReservationType.Lodging,
            Date = new DateOnly(2026, 11, 12),
            StartsAt = new TimeOnly(15, 0),
            EndsOn = new DateOnly(2026, 11, 16),
            EndsAt = new TimeOnly(11, 0),
            Title = "Guesthouse en Kyoto",
            City = "Kyoto",
            LocationName = "Kyoto Granbell Hotel",
            Address = "Gion, Higashiyama Ward, Kyoto",
            ConfirmationCode = "FREE-KYO-1112",
            Notes = "Check-in simple. Buena base para caminar Higashiyama."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555609"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 11, 13),
            StartsAt = new TimeOnly(8, 45),
            Title = "Kiyomizu-dera y Ninenzaka",
            City = "Kyoto",
            LocationName = "Kiyomizu-dera",
            Address = "1 Chome-294 Kiyomizu, Higashiyama Ward, Kyoto",
            ConfirmationCode = "FREE-KIYOMIZU",
            Notes = "Ir temprano y bajar caminando por Sannenzaka."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555610"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 11, 14),
            StartsAt = new TimeOnly(10, 30),
            Title = "Nishiki Market",
            City = "Kyoto",
            LocationName = "Nishiki Market",
            Address = "Nakagyo Ward, Kyoto",
            ConfirmationCode = "FREE-NISHIKI",
            Notes = "Almuerzo informal. Ideal para probar varios snacks."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555611"),
            Type = ReservationType.Flight,
            Date = new DateOnly(2026, 11, 16),
            StartsAt = new TimeOnly(14, 40),
            EndsOn = new DateOnly(2026, 11, 16),
            EndsAt = new TimeOnly(16, 5),
            Title = "Vuelo Osaka a Madrid",
            City = "Osaka",
            LocationName = "Kansai International Airport",
            Address = "Kansai International Airport, Osaka",
            ConfirmationCode = "FREE-FLT-OSAMAD",
            Notes = "Salir desde Kyoto con margen amplio.",
            Airline = "Emirates",
            FlightNumber = "EK317-EK143",
            OriginName = "Osaka",
            DestinationName = "Madrid",
            OriginAirport = "KIX",
            DestinationAirport = "MAD"
        }
    ];

    private static IReadOnlyCollection<Reservation> CreateSubscriptionUserReservations() =>
    [
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555701"),
            Type = ReservationType.Flight,
            Date = new DateOnly(2026, 11, 18),
            StartsAt = new TimeOnly(13, 20),
            EndsOn = new DateOnly(2026, 11, 19),
            EndsAt = new TimeOnly(10, 5),
            Title = "Vuelo Buenos Aires a Tokyo",
            City = "Tokyo",
            LocationName = "Narita Airport",
            Address = "Narita International Airport, Chiba",
            ConfirmationCode = "SUB-FLT-EZENRT",
            Notes = "Llegar con 3 horas de anticipacion.",
            Airline = "LATAM / Japan Airlines",
            FlightNumber = "LA803-JL720",
            OriginName = "Buenos Aires",
            DestinationName = "Tokyo",
            OriginAirport = "EZE",
            DestinationAirport = "NRT"
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555702"),
            Type = ReservationType.Lodging,
            Date = new DateOnly(2026, 11, 19),
            StartsAt = new TimeOnly(15, 0),
            EndsOn = new DateOnly(2026, 11, 23),
            EndsAt = new TimeOnly(11, 0),
            Title = "Ryokan urbano en Asakusa",
            City = "Tokyo",
            LocationName = "Onyado Nono Asakusa",
            Address = "Asakusa, Taito City, Tokyo",
            ConfirmationCode = "SUB-TYO-1119",
            Notes = "Onsen incluido. Revisar horarios de desayuno."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555703"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 11, 21),
            StartsAt = new TimeOnly(17, 45),
            Title = "Mirador Shibuya Sky",
            City = "Tokyo",
            LocationName = "Shibuya Scramble Square",
            Address = "2 Chome-24-12 Shibuya, Tokyo",
            ConfirmationCode = "SUB-SKY-1121",
            Notes = "Llegar 20 minutos antes. Mejor horario para atardecer."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555704"),
            Type = ReservationType.Flight,
            Date = new DateOnly(2026, 11, 23),
            StartsAt = new TimeOnly(10, 15),
            EndsOn = new DateOnly(2026, 11, 23),
            EndsAt = new TimeOnly(11, 35),
            Title = "Vuelo Tokyo a Kyoto/Osaka",
            City = "Kyoto",
            LocationName = "Itami Airport",
            Address = "Osaka International Airport, Osaka",
            ConfirmationCode = "SUB-FLT-TYOKYO",
            Notes = "Traslado desde Itami a Kyoto Station.",
            Airline = "Japan Airlines",
            FlightNumber = "JL107",
            OriginName = "Tokyo",
            DestinationName = "Kyoto",
            OriginAirport = "HND",
            DestinationAirport = "ITM"
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555705"),
            Type = ReservationType.Lodging,
            Date = new DateOnly(2026, 11, 23),
            StartsAt = new TimeOnly(15, 0),
            EndsOn = new DateOnly(2026, 11, 29),
            EndsAt = new TimeOnly(11, 0),
            Title = "Machiya boutique en Kyoto",
            City = "Kyoto",
            LocationName = "Kyoto Machiya Stay",
            Address = "Gion, Higashiyama Ward, Kyoto",
            ConfirmationCode = "SUB-KYO-1123",
            Notes = "Entrada con codigo digital. Mantener bajo volumen por la noche."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555706"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 11, 25),
            StartsAt = new TimeOnly(8, 30),
            Title = "Ruta Fushimi Inari temprano",
            City = "Kyoto",
            LocationName = "Fushimi Inari Taisha",
            Address = "68 Fukakusa Yabunouchicho, Fushimi Ward, Kyoto",
            ConfirmationCode = "SUB-INARI-1125",
            Notes = "Ir temprano. Llevar calzado comodo y agua."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555707"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 11, 26),
            StartsAt = new TimeOnly(11, 0),
            Title = "Ceremonia de te",
            City = "Kyoto",
            LocationName = "Camellia Tea Ceremony",
            Address = "Higashiyama Ward, Kyoto",
            ConfirmationCode = "SUB-TEA-1126",
            Notes = "Llegar 10 minutos antes. Experiencia de 45 minutos."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555708"),
            Type = ReservationType.Lodging,
            Date = new DateOnly(2026, 11, 29),
            StartsAt = new TimeOnly(15, 0),
            EndsOn = new DateOnly(2026, 12, 3),
            EndsAt = new TimeOnly(11, 0),
            Title = "Hotel en Umeda",
            City = "Osaka",
            LocationName = "Hotel Hankyu Respire Osaka",
            Address = "Umeda, Kita Ward, Osaka",
            ConfirmationCode = "SUB-OSA-1129",
            Notes = "Base comoda para trenes y salidas nocturnas."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555709"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 11, 29),
            StartsAt = new TimeOnly(18, 30),
            Title = "Umeda Sky Building",
            City = "Osaka",
            LocationName = "Umeda Sky Building",
            Address = "1 Chome-1-88 Oyodonaka, Kita Ward, Osaka",
            ConfirmationCode = "SUB-UMEDA-1129",
            Notes = "Buen plan de llegada. Revisar clima antes de subir."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555710"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 11, 30),
            StartsAt = new TimeOnly(20, 0),
            Title = "Cocktails en Kitashinchi",
            City = "Osaka",
            LocationName = "Bar Nayuta",
            Address = "Kitashinchi, Osaka",
            ConfirmationCode = "SUB-BAR-1130",
            Notes = "Reserva para dos. Confirmar si se prefiere barra."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555711"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 12, 1),
            StartsAt = new TimeOnly(9, 30),
            Title = "Excursion a Nara",
            City = "Nara",
            LocationName = "Nara Park",
            Address = "Nara Park, Nara",
            ConfirmationCode = "SUB-NARA-1201",
            Notes = "Salir temprano desde Osaka Namba. Almuerzo cerca de Higashimuki."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555712"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 12, 2),
            StartsAt = new TimeOnly(19, 0),
            Title = "Okonomiyaki reservado",
            City = "Osaka",
            LocationName = "Mizuno",
            Address = "Dotonbori, Chuo Ward, Osaka",
            ConfirmationCode = "SUB-MIZUNO-1202",
            Notes = "Reserva de cena. Ir con algo de margen por la zona."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555713"),
            Type = ReservationType.Flight,
            Date = new DateOnly(2026, 12, 3),
            StartsAt = new TimeOnly(12, 20),
            EndsOn = new DateOnly(2026, 12, 3),
            EndsAt = new TimeOnly(14, 15),
            Title = "Vuelo Osaka a Buenos Aires",
            City = "Osaka",
            LocationName = "Kansai International Airport",
            Address = "Kansai International Airport, Osaka",
            ConfirmationCode = "SUB-FLT-OSAEZE",
            Notes = "Salida internacional. Llevar pasaporte a mano.",
            Airline = "Qatar Airways / LATAM",
            FlightNumber = "QR803-LA802",
            OriginName = "Osaka",
            DestinationName = "Buenos Aires",
            OriginAirport = "KIX",
            DestinationAirport = "EZE"
        }
    ];

    private static IReadOnlyCollection<Reservation> CreatePaidUserReservations() =>
    [
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555801"),
            Type = ReservationType.Flight,
            Date = new DateOnly(2026, 12, 4),
            StartsAt = new TimeOnly(20, 45),
            EndsOn = new DateOnly(2026, 12, 5),
            EndsAt = new TimeOnly(17, 55),
            Title = "Vuelo Barcelona a Tokyo",
            City = "Tokyo",
            LocationName = "Haneda Airport",
            Address = "Haneda Airport, Tokyo",
            ConfirmationCode = "PAID-FLT-BCNHND",
            Notes = "Verificar asiento y menu especial antes de viajar.",
            Airline = "Qatar Airways / Japan Airlines",
            FlightNumber = "QR142-JL050",
            OriginName = "Barcelona",
            DestinationName = "Tokyo",
            OriginAirport = "BCN",
            DestinationAirport = "HND"
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555802"),
            Type = ReservationType.Lodging,
            Date = new DateOnly(2026, 12, 5),
            StartsAt = new TimeOnly(15, 0),
            EndsOn = new DateOnly(2026, 12, 9),
            EndsAt = new TimeOnly(11, 0),
            Title = "Hotel boutique en Ginza",
            City = "Tokyo",
            LocationName = "Hotel The Celestine Ginza",
            Address = "8 Chome-4-22 Ginza, Chuo City, Tokyo",
            ConfirmationCode = "PAID-TYO-1205",
            Notes = "Pedir late check-out si hay disponibilidad."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555803"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 12, 7),
            StartsAt = new TimeOnly(18, 30),
            Title = "Cena omakase reservada",
            City = "Tokyo",
            LocationName = "Sushi Ginza Onodera",
            Address = "Ginza, Chuo City, Tokyo",
            ConfirmationCode = "PAID-OMAKASE",
            Notes = "Avisar alergias con 48 horas. Dress code smart casual."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555804"),
            Type = ReservationType.Flight,
            Date = new DateOnly(2026, 12, 9),
            StartsAt = new TimeOnly(9, 50),
            EndsOn = new DateOnly(2026, 12, 9),
            EndsAt = new TimeOnly(11, 15),
            Title = "Vuelo Tokyo a Osaka",
            City = "Osaka",
            LocationName = "Kansai International Airport",
            Address = "Kansai International Airport, Osaka",
            ConfirmationCode = "PAID-FLT-TYOOSA",
            Notes = "Reservar traslado privado desde KIX.",
            Airline = "ANA",
            FlightNumber = "NH093",
            OriginName = "Tokyo",
            DestinationName = "Osaka",
            OriginAirport = "HND",
            DestinationAirport = "KIX"
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555805"),
            Type = ReservationType.Lodging,
            Date = new DateOnly(2026, 12, 9),
            StartsAt = new TimeOnly(15, 0),
            EndsOn = new DateOnly(2026, 12, 15),
            EndsAt = new TimeOnly(11, 0),
            Title = "Hotel con vista en Osaka",
            City = "Osaka",
            LocationName = "Conrad Osaka",
            Address = "3 Chome-2-4 Nakanoshima, Kita Ward, Osaka",
            ConfirmationCode = "PAID-OSA-1209",
            Notes = "Check-in ejecutivo incluido. Confirmar preferencia de cama."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555806"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 12, 11),
            StartsAt = new TimeOnly(10, 0),
            Title = "Universal Studios Japan",
            City = "Osaka",
            LocationName = "Universal Studios Japan",
            Address = "2 Chome-1-33 Sakurajima, Konohana Ward, Osaka",
            ConfirmationCode = "PAID-USJ-1211",
            Notes = "Tickets Express Pass incluidos. Llegar antes de apertura."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555807"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 12, 12),
            StartsAt = new TimeOnly(19, 30),
            Title = "Cena Kobe beef",
            City = "Kobe",
            LocationName = "Kobe Steak Ishida",
            Address = "Sannomiya, Kobe",
            ConfirmationCode = "PAID-KOBE-1212",
            Notes = "Reserva premium. Avisar si se prefiere maridaje."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555808"),
            Type = ReservationType.Flight,
            Date = new DateOnly(2026, 12, 15),
            StartsAt = new TimeOnly(10, 30),
            EndsOn = new DateOnly(2026, 12, 15),
            EndsAt = new TimeOnly(11, 50),
            Title = "Vuelo Osaka a Hiroshima",
            City = "Hiroshima",
            LocationName = "Hiroshima Airport",
            Address = "Hiroshima Airport, Hiroshima",
            ConfirmationCode = "PAID-FLT-OSAHIJ",
            Notes = "Traslado privado coordinado al hotel.",
            Airline = "ANA",
            FlightNumber = "NH673",
            OriginName = "Osaka",
            DestinationName = "Hiroshima",
            OriginAirport = "ITM",
            DestinationAirport = "HIJ"
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555809"),
            Type = ReservationType.Lodging,
            Date = new DateOnly(2026, 12, 15),
            StartsAt = new TimeOnly(15, 0),
            EndsOn = new DateOnly(2026, 12, 19),
            EndsAt = new TimeOnly(11, 0),
            Title = "Hotel frente al parque",
            City = "Hiroshima",
            LocationName = "The Knot Hiroshima",
            Address = "Naka Ward, Hiroshima",
            ConfirmationCode = "PAID-HIJ-1215",
            Notes = "Habitacion con vista solicitada."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555810"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 12, 16),
            StartsAt = new TimeOnly(10, 0),
            Title = "Peace Memorial Museum",
            City = "Hiroshima",
            LocationName = "Hiroshima Peace Memorial Museum",
            Address = "1-2 Nakajimacho, Naka Ward, Hiroshima",
            ConfirmationCode = "PAID-PEACE-1216",
            Notes = "Visita sobria. Dejar margen despues para caminar el parque."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555811"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 12, 17),
            StartsAt = new TimeOnly(8, 30),
            Title = "Miyajima full day",
            City = "Miyajima",
            LocationName = "Itsukushima Shrine",
            Address = "Miyajima, Hiroshima",
            ConfirmationCode = "PAID-MIYAJIMA",
            Notes = "Horario ajustado a marea. Ferry incluido en el plan."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555812"),
            Type = ReservationType.Flight,
            Date = new DateOnly(2026, 12, 19),
            StartsAt = new TimeOnly(9, 35),
            EndsOn = new DateOnly(2026, 12, 19),
            EndsAt = new TimeOnly(11, 10),
            Title = "Vuelo Hiroshima a Sapporo",
            City = "Sapporo",
            LocationName = "New Chitose Airport",
            Address = "New Chitose Airport, Hokkaido",
            ConfirmationCode = "PAID-FLT-HIJCTS",
            Notes = "Revisar equipaje de invierno.",
            Airline = "Japan Airlines",
            FlightNumber = "JL3403",
            OriginName = "Hiroshima",
            DestinationName = "Sapporo",
            OriginAirport = "HIJ",
            DestinationAirport = "CTS"
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555813"),
            Type = ReservationType.Lodging,
            Date = new DateOnly(2026, 12, 19),
            StartsAt = new TimeOnly(15, 0),
            EndsOn = new DateOnly(2026, 12, 24),
            EndsAt = new TimeOnly(11, 0),
            Title = "Hotel en Odori Park",
            City = "Sapporo",
            LocationName = "Sapporo Grand Hotel",
            Address = "Odori, Chuo Ward, Sapporo",
            ConfirmationCode = "PAID-CTS-1219",
            Notes = "Pedir habitacion alejada del ascensor."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555814"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 12, 20),
            StartsAt = new TimeOnly(18, 0),
            Title = "Ramen alley dinner",
            City = "Sapporo",
            LocationName = "Ganso Ramen Yokocho",
            Address = "Susukino, Sapporo",
            ConfirmationCode = "PAID-RAMEN-1220",
            Notes = "Cena casual. Ideal despues de caminar Odori."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555815"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 12, 21),
            StartsAt = new TimeOnly(9, 0),
            Title = "Otaru canal day trip",
            City = "Otaru",
            LocationName = "Otaru Canal",
            Address = "Otaru, Hokkaido",
            ConfirmationCode = "PAID-OTARU-1221",
            Notes = "Dia de excursion desde Sapporo. Llevar abrigo."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555816"),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 12, 22),
            StartsAt = new TimeOnly(11, 30),
            Title = "Nijo Market lunch",
            City = "Sapporo",
            LocationName = "Nijo Market",
            Address = "Minami 3 Jonishi, Chuo Ward, Sapporo",
            ConfirmationCode = "PAID-NIJO-1222",
            Notes = "Almuerzo de seafood bowl. Evitar hora pico."
        },
        new Reservation
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555817"),
            Type = ReservationType.Flight,
            Date = new DateOnly(2026, 12, 24),
            StartsAt = new TimeOnly(13, 10),
            EndsOn = new DateOnly(2026, 12, 24),
            EndsAt = new TimeOnly(22, 45),
            Title = "Vuelo Sapporo a Barcelona",
            City = "Sapporo",
            LocationName = "New Chitose Airport",
            Address = "New Chitose Airport, Hokkaido",
            ConfirmationCode = "PAID-FLT-CTSBCN",
            Notes = "Vuelo de regreso con conexion internacional.",
            Airline = "Japan Airlines / Qatar Airways",
            FlightNumber = "JL512-QR141",
            OriginName = "Sapporo",
            DestinationName = "Barcelona",
            OriginAirport = "CTS",
            DestinationAirport = "BCN"
        }
    ];

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

    private static async Task AddDemoReservationIfMissingAsync(
        TravelCompanionDbContext dbContext,
        Guid tripId,
        Guid reservationId,
        Reservation reservation)
    {
        var exists = await dbContext.Reservations.AnyAsync(existingReservation => existingReservation.Id == reservationId);
        if (exists)
        {
            return;
        }

        reservation.TripId = tripId;
        dbContext.Reservations.Add(reservation);
    }

    private static async Task SeedDemoTravelPreferenceProfilesAsync(TravelCompanionDbContext dbContext)
    {
        var demoUserIds = new[] { DemoUserId, FreeUserId, SubscriptionUserId, PaidUserId };
        var existingProfileUserIds = await dbContext.TravelPreferenceProfiles
            .Where(profile => demoUserIds.Contains(profile.UserId))
            .Select(profile => profile.UserId)
            .ToListAsync();

        foreach (var userId in demoUserIds.Except(existingProfileUserIds))
        {
            dbContext.TravelPreferenceProfiles.Add(new TravelPreferenceProfile
            {
                UserId = userId,
                Interests = ["Food", "Culture", "Neighborhood"],
                FoodPreferences = ["local food", "coffee"],
                BudgetLevel = "medium",
                TravelPace = "balanced",
                AvoidTouristTraps = true,
                MaxWalkingMinutes = 25
            });
        }
    }

    private static async Task SeedDemoTravelDocumentsAsync(TravelCompanionDbContext dbContext)
    {
        var documents = new[]
        {
            CreateTravelDocument(1, Guid.Parse("44444444-4444-4444-4444-444444444401"), "demo-hotel-tokyo", TravelDocumentCategory.Hotel, "Hotel Tokyo", "Hotel demo Ginza", "/docs/hotel-tokyo.pdf", 10),
            CreateTravelDocument(2, Guid.Parse("44444444-4444-4444-4444-444444444401"), "demo-trenes", TravelDocumentCategory.Other, "Trenes", "Tickets y pases", "/docs/trenes.pdf", 20),
            CreateTravelDocument(3, SubscriptionTripId, "sub-hotel-tokyo-ida", TravelDocumentCategory.Hotel, "Hotel Tokio (ida)", "Hotel The Celestine Ginza", "/docs/hotel-tokyo-ida.pdf", 10),
            CreateTravelDocument(4, SubscriptionTripId, "sub-hotel-kyoto", TravelDocumentCategory.Hotel, "Hotel Kioto", "Kyoto Machiya Stay", "/docs/hotel-kyoto.pdf", 20),
            CreateTravelDocument(5, SubscriptionTripId, "sub-hotel-osaka", TravelDocumentCategory.Hotel, "Hotel Osaka", "Hotel Hankyu Respire Osaka", "/docs/hotel-osaka.pdf", 30),
            CreateTravelDocument(6, SubscriptionTripId, "sub-trenes", TravelDocumentCategory.Other, "Trenes", "Shinkansen y tickets", "/docs/trenes.pdf", 10),
            CreateTravelDocument(7, SubscriptionTripId, "sub-disney", TravelDocumentCategory.Other, "Tokyo Disney", "Guia del dia", "/docs/tokyo-disney.pdf", 20),
            CreateTravelDocument(8, PaidTripId, "paid-hotel-tokyo", TravelDocumentCategory.Hotel, "Hotel Tokio", "Hotel The Celestine Ginza", "/docs/hotel-tokyo.pdf", 10),
            CreateTravelDocument(9, PaidTripId, "paid-hotel-osaka", TravelDocumentCategory.Hotel, "Hotel Osaka", "Conrad Osaka", "/docs/hotel-osaka.pdf", 20),
            CreateTravelDocument(10, PaidTripId, "paid-trenes", TravelDocumentCategory.Other, "Trenes", "Tickets intercity", "/docs/trenes.pdf", 10)
        };

        foreach (var document in documents)
        {
            var existing = await dbContext.TravelDocuments
                .FirstOrDefaultAsync(current => current.TripId == document.TripId && current.ExternalId == document.ExternalId);

            if (existing is null)
            {
                dbContext.TravelDocuments.Add(document);
                continue;
            }

            existing.Category = document.Category;
            existing.Title = document.Title;
            existing.Subtitle = document.Subtitle;
            existing.FileUrl = document.FileUrl;
            existing.SortOrder = document.SortOrder;
        }
    }

    private static TravelDocument CreateTravelDocument(
        int seedNumber,
        Guid tripId,
        string externalId,
        TravelDocumentCategory category,
        string title,
        string subtitle,
        string fileUrl,
        int sortOrder) =>
        new()
        {
            Id = Guid.Parse($"88888888-8888-8888-8888-{seedNumber:000000000000}"),
            TripId = tripId,
            ExternalId = externalId,
            Category = category,
            Title = title,
            Subtitle = subtitle,
            FileUrl = fileUrl,
            SortOrder = sortOrder
        };
}

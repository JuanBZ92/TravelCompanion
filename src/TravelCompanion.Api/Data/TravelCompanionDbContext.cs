using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Data;

public sealed class TravelCompanionDbContext(DbContextOptions<TravelCompanionDbContext> options)
    : DbContext(options)
{
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<TravelPackage> TravelPackages => Set<TravelPackage>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserEntitlement> UserEntitlements => Set<UserEntitlement>();
    public DbSet<AppUserSession> AppUserSessions => Set<AppUserSession>();
    public DbSet<TravelPreferenceProfile> TravelPreferenceProfiles => Set<TravelPreferenceProfile>();
    public DbSet<TravelChatConversation> TravelChatConversations => Set<TravelChatConversation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Destination>(entity =>
        {
            entity.HasIndex(destination => destination.Slug).IsUnique();
            entity.Property(destination => destination.Name).HasMaxLength(120);
            entity.Property(destination => destination.Slug).HasMaxLength(80);
            entity.Property(destination => destination.Country).HasMaxLength(80);
        });

        modelBuilder.Entity<TravelPackage>(entity =>
        {
            entity.HasIndex(package => package.Slug).IsUnique();
            entity.HasIndex(package => new { package.DestinationId, package.Price });
            entity.Property(package => package.Name).HasMaxLength(140);
            entity.Property(package => package.Slug).HasMaxLength(100);
            entity.Property(package => package.Currency).HasMaxLength(3);
            entity.Property(package => package.Price).HasPrecision(10, 2);
        });

        modelBuilder.Entity<Recommendation>(entity =>
        {
            entity.HasIndex(recommendation => new { recommendation.DestinationId, recommendation.Title });
            entity.HasIndex(recommendation => new { recommendation.DestinationId, recommendation.Category, recommendation.Title });
            entity.Property(recommendation => recommendation.Title).HasMaxLength(160);
            entity.Property(recommendation => recommendation.Category).HasMaxLength(80);
            entity.Property(recommendation => recommendation.Neighborhood).HasMaxLength(120);
            entity.Property(recommendation => recommendation.PriceLevel).HasMaxLength(32);
            entity.Property(recommendation => recommendation.OpeningHours).HasMaxLength(256);
            entity.Property(recommendation => recommendation.Latitude).HasPrecision(9, 6);
            entity.Property(recommendation => recommendation.Longitude).HasPrecision(9, 6);
            entity.Property(recommendation => recommendation.AccessLevel)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.HasMany(recommendation => recommendation.Packages)
                .WithMany(package => package.Recommendations)
                .UsingEntity<Dictionary<string, object>>(
                    "RecommendationTravelPackages",
                    right => right
                        .HasOne<TravelPackage>()
                        .WithMany()
                        .HasForeignKey("TravelPackageId")
                        .OnDelete(DeleteBehavior.Cascade),
                    left => left
                        .HasOne<Recommendation>()
                        .WithMany()
                        .HasForeignKey("RecommendationId")
                        .OnDelete(DeleteBehavior.Cascade),
                    join =>
                    {
                        join.ToTable("RecommendationTravelPackages");
                        join.HasKey("RecommendationId", "TravelPackageId");
                        join.HasIndex("TravelPackageId");
                    });
        });

        modelBuilder.Entity<Trip>(entity =>
        {
            entity.HasIndex(trip => new { trip.AppUserId, trip.StartsOn });
            entity.HasIndex(trip => new { trip.DestinationId, trip.StartsOn });
            entity.Property(trip => trip.TravelerName).HasMaxLength(140);
            entity.HasOne(trip => trip.AppUser)
                .WithMany(user => user.Trips)
                .HasForeignKey(trip => trip.AppUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasIndex(reservation => new { reservation.TripId, reservation.Date, reservation.StartsAt });
            entity.HasIndex(reservation => new { reservation.TripId, reservation.Type, reservation.Date, reservation.StartsAt });
            entity.Property(reservation => reservation.Type)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(TravelCompanion.Shared.ReservationType.Event);
            entity.Property(reservation => reservation.Title).HasMaxLength(160);
            entity.Property(reservation => reservation.City).HasMaxLength(120);
            entity.Property(reservation => reservation.LocationName).HasMaxLength(160);
            entity.Property(reservation => reservation.ConfirmationCode).HasMaxLength(80);
            entity.Property(reservation => reservation.Airline).HasMaxLength(120);
            entity.Property(reservation => reservation.FlightNumber).HasMaxLength(40);
            entity.Property(reservation => reservation.OriginName).HasMaxLength(160);
            entity.Property(reservation => reservation.DestinationName).HasMaxLength(160);
            entity.Property(reservation => reservation.OriginAirport).HasMaxLength(80);
            entity.Property(reservation => reservation.DestinationAirport).HasMaxLength(80);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.Email).HasMaxLength(180);
            entity.Property(user => user.DisplayName).HasMaxLength(140);
            entity.Property(user => user.PasswordHash).HasMaxLength(512);
        });

        modelBuilder.Entity<AppUserSession>(entity =>
        {
            entity.HasIndex(session => session.TokenHash).IsUnique();
            entity.HasIndex(session => new { session.UserId, session.RevokedAt });
            entity.Property(session => session.TokenHash).HasMaxLength(128);
            entity.HasOne(session => session.User)
                .WithMany(user => user.Sessions)
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TravelPreferenceProfile>(entity =>
        {
            entity.HasKey(profile => profile.UserId);
            entity.Property(profile => profile.BudgetLevel).HasMaxLength(32);
            entity.Property(profile => profile.TravelPace).HasMaxLength(32);
            entity.HasOne(profile => profile.User)
                .WithOne(user => user.TravelPreferenceProfile)
                .HasForeignKey<TravelPreferenceProfile>(profile => profile.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TravelChatConversation>(entity =>
        {
            entity.Property(conversation => conversation.Id).HasMaxLength(64);
            entity.Property(conversation => conversation.LastCity).HasMaxLength(120);
            entity.Property(conversation => conversation.LastResponseMode).HasMaxLength(40);
            entity.Property(conversation => conversation.LastRecommendationIds).HasMaxLength(512);
            entity.Property(conversation => conversation.PendingPreferenceOriginalMessage).HasMaxLength(512);
            entity.HasKey(conversation => conversation.Id);
            entity.HasIndex(conversation => new { conversation.UserId, conversation.UpdatedAt });
            entity.HasOne(conversation => conversation.User)
                .WithMany(user => user.TravelChatConversations)
                .HasForeignKey(conversation => conversation.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserEntitlement>(entity =>
        {
            entity.HasIndex(entitlement => new { entitlement.UserId, entitlement.ExpiresAt });
            entity.HasIndex(entitlement => new { entitlement.TravelPackageId, entitlement.ExpiresAt });
            entity.HasIndex(entitlement => new { entitlement.DestinationId, entitlement.ExpiresAt });
            entity.Property(entitlement => entitlement.AccessLevel)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(entitlement => entitlement.Source).HasMaxLength(80);
        });
    }
}

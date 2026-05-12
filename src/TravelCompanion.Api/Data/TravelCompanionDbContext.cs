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
            entity.Property(package => package.Name).HasMaxLength(140);
            entity.Property(package => package.Slug).HasMaxLength(100);
            entity.Property(package => package.Currency).HasMaxLength(3);
            entity.Property(package => package.Price).HasPrecision(10, 2);
        });

        modelBuilder.Entity<Recommendation>(entity =>
        {
            entity.Property(recommendation => recommendation.Title).HasMaxLength(160);
            entity.Property(recommendation => recommendation.Category).HasMaxLength(80);
            entity.Property(recommendation => recommendation.Neighborhood).HasMaxLength(120);
            entity.Property(recommendation => recommendation.Latitude).HasPrecision(9, 6);
            entity.Property(recommendation => recommendation.Longitude).HasPrecision(9, 6);
            entity.Property(recommendation => recommendation.AccessLevel)
                .HasConversion<string>()
                .HasMaxLength(32);
        });

        modelBuilder.Entity<Trip>(entity =>
        {
            entity.Property(trip => trip.TravelerName).HasMaxLength(140);
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.Property(reservation => reservation.Title).HasMaxLength(160);
            entity.Property(reservation => reservation.LocationName).HasMaxLength(160);
            entity.Property(reservation => reservation.ConfirmationCode).HasMaxLength(80);
            entity.Property(reservation => reservation.AccessLevel)
                .HasConversion<string>()
                .HasMaxLength(32);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.Email).HasMaxLength(180);
            entity.Property(user => user.DisplayName).HasMaxLength(140);
        });

        modelBuilder.Entity<UserEntitlement>(entity =>
        {
            entity.Property(entitlement => entitlement.AccessLevel)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(entitlement => entitlement.Source).HasMaxLength(80);
        });
    }
}

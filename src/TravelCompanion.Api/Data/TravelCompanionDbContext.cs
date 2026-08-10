using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Data;

public sealed class TravelCompanionDbContext(DbContextOptions<TravelCompanionDbContext> options)
    : DbContext(options)
{
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<TravelPackage> TravelPackages => Set<TravelPackage>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<FreeMapCity> FreeMapCities => Set<FreeMapCity>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripDayPlan> TripDayPlans => Set<TripDayPlan>();
    public DbSet<TripDayBlock> TripDayBlocks => Set<TripDayBlock>();
    public DbSet<TripPlanDraft> TripPlanDrafts => Set<TripPlanDraft>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<TravelDocument> TravelDocuments => Set<TravelDocument>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserEntitlement> UserEntitlements => Set<UserEntitlement>();
    public DbSet<AppUserSession> AppUserSessions => Set<AppUserSession>();
    public DbSet<TravelPreferenceProfile> TravelPreferenceProfiles => Set<TravelPreferenceProfile>();
    public DbSet<TravelChatConversation> TravelChatConversations => Set<TravelChatConversation>();
    public DbSet<TravelAssistantFeedback> TravelAssistantFeedbackItems => Set<TravelAssistantFeedback>();
    public DbSet<RecommendationInteractionSignal> RecommendationInteractionSignals => Set<RecommendationInteractionSignal>();
    public DbSet<NotificationDeviceRegistration> NotificationDeviceRegistrations => Set<NotificationDeviceRegistration>();
    public DbSet<NotificationOutboxItem> NotificationOutboxItems => Set<NotificationOutboxItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Destination>(entity =>
        {
            entity.HasIndex(destination => destination.Slug).IsUnique();
            entity.Property(destination => destination.Name).HasMaxLength(120);
            entity.Property(destination => destination.Slug).HasMaxLength(80);
            entity.Property(destination => destination.Country).HasMaxLength(80);
            entity.Property(destination => destination.TimeZoneId).HasMaxLength(120).HasDefaultValue("UTC");
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
            entity.HasIndex(recommendation => new { recommendation.DestinationId, recommendation.ExternalId }).IsUnique();
            entity.HasIndex(recommendation => new { recommendation.DestinationId, recommendation.Title });
            entity.HasIndex(recommendation => new { recommendation.DestinationId, recommendation.Category, recommendation.Title });
            entity.HasIndex(recommendation => new { recommendation.DestinationId, recommendation.CitySlug });
            entity.Property(recommendation => recommendation.ExternalId).HasMaxLength(160);
            entity.Property(recommendation => recommendation.Title).HasMaxLength(160);
            entity.Property(recommendation => recommendation.Category).HasMaxLength(80);
            entity.Property(recommendation => recommendation.Neighborhood).HasMaxLength(120);
            entity.Property(recommendation => recommendation.CitySlug).HasMaxLength(80);
            entity.Property(recommendation => recommendation.PriceLevel).HasMaxLength(32);
            entity.Property(recommendation => recommendation.OpeningHours).HasMaxLength(256);
            entity.Property(recommendation => recommendation.SourceName).HasMaxLength(160);
            entity.Property(recommendation => recommendation.SourceUrl).HasMaxLength(512);
            entity.Property(recommendation => recommendation.CurationNotes).HasMaxLength(1000);
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
            entity.HasIndex(trip => new { trip.AppUserId, trip.ExternalId }).IsUnique();
            entity.HasIndex(trip => new { trip.AppUserId, trip.StartsOn });
            entity.HasIndex(trip => new { trip.DestinationId, trip.StartsOn });
            entity.Property(trip => trip.ExternalId).HasMaxLength(160);
            entity.Property(trip => trip.AccessPinHash).HasMaxLength(512);
            entity.Property(trip => trip.TravelerName).HasMaxLength(140);
            entity.Property(trip => trip.TimeZoneId).HasMaxLength(120).HasDefaultValue("UTC");
            entity.Property(trip => trip.PublicationStatus)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(TripPublicationStatus.Published)
                .HasSentinel((TripPublicationStatus)(-1));
            entity.HasIndex(trip => new { trip.PublicationStatus, trip.StartsOn });
            entity.HasOne(trip => trip.AppUser)
                .WithMany(user => user.Trips)
                .HasForeignKey(trip => trip.AppUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TripDayPlan>(entity =>
        {
            entity.HasIndex(day => new { day.TripId, day.Date }).IsUnique();
            entity.HasIndex(day => new { day.TripId, day.DayNumber }).IsUnique();
            entity.Property(day => day.City).HasMaxLength(120);
            entity.Property(day => day.HotelBase).HasMaxLength(180);
            entity.Property(day => day.BaseLatitude).HasPrecision(9, 6);
            entity.Property(day => day.BaseLongitude).HasPrecision(9, 6);
            entity.Property(day => day.Introduction).HasMaxLength(2000);
            entity.HasOne(day => day.Trip)
                .WithMany(trip => trip.DayPlans)
                .HasForeignKey(day => day.TripId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TripDayBlock>(entity =>
        {
            entity.HasIndex(block => new { block.TripDayPlanId, block.PeriodKey }).IsUnique();
            entity.Property(block => block.PeriodKey).HasMaxLength(32);
            entity.Property(block => block.CuratedDescription).HasMaxLength(2000);
            entity.HasOne(block => block.TripDayPlan)
                .WithMany(day => day.Blocks)
                .HasForeignKey(block => block.TripDayPlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TripPlanDraft>(entity =>
        {
            entity.HasKey(draft => draft.TripId);
            entity.Property(draft => draft.PayloadJson).HasColumnType("jsonb");
            entity.Property(draft => draft.PendingAccessPinHash).HasMaxLength(512);
            entity.HasOne(draft => draft.Trip)
                .WithOne(trip => trip.PlanDraft)
                .HasForeignKey<TripPlanDraft>(draft => draft.TripId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasIndex(reservation => new { reservation.TripId, reservation.ExternalId }).IsUnique();
            entity.HasIndex(reservation => new { reservation.TripId, reservation.Date, reservation.StartsAt });
            entity.HasIndex(reservation => new { reservation.TripId, reservation.Type, reservation.Date, reservation.StartsAt });
            entity.HasIndex(reservation => new { reservation.TripId, reservation.RecommendationId });
            entity.HasIndex(reservation => reservation.TripDayBlockId);
            entity.Property(reservation => reservation.ExternalId).HasMaxLength(160);
            entity.Property(reservation => reservation.Type)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(TravelCompanion.Shared.ReservationType.Event);
            entity.Property(reservation => reservation.PlanningKind)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(TravelCompanion.Shared.ScheduleItemKind.ManualEvent);
            entity.Property(reservation => reservation.Title).HasMaxLength(160);
            entity.Property(reservation => reservation.TimeZoneId).HasMaxLength(120);
            entity.Property(reservation => reservation.City).HasMaxLength(120);
            entity.Property(reservation => reservation.LocationName).HasMaxLength(160);
            entity.Property(reservation => reservation.ConfirmationCode).HasMaxLength(80);
            entity.Property(reservation => reservation.Airline).HasMaxLength(120);
            entity.Property(reservation => reservation.FlightNumber).HasMaxLength(40);
            entity.Property(reservation => reservation.OriginName).HasMaxLength(160);
            entity.Property(reservation => reservation.DestinationName).HasMaxLength(160);
            entity.Property(reservation => reservation.OriginAirport).HasMaxLength(80);
            entity.Property(reservation => reservation.DestinationAirport).HasMaxLength(80);
            entity.Property(reservation => reservation.SourceName).HasMaxLength(160);
            entity.Property(reservation => reservation.SourceUrl).HasMaxLength(512);
            entity.Property(reservation => reservation.Latitude).HasPrecision(9, 6);
            entity.Property(reservation => reservation.Longitude).HasPrecision(9, 6);
            entity.HasOne(reservation => reservation.Recommendation)
                .WithMany()
                .HasForeignKey(reservation => reservation.RecommendationId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(reservation => reservation.TripDayBlock)
                .WithMany(block => block.Reservations)
                .HasForeignKey(reservation => reservation.TripDayBlockId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<FreeMapCity>(entity =>
        {
            entity.HasIndex(city => new { city.DestinationId, city.CitySlug }).IsUnique();
            entity.HasIndex(city => new { city.IsEnabled, city.SortOrder });
            entity.Property(city => city.CitySlug).HasMaxLength(80);
            entity.Property(city => city.DisplayName).HasMaxLength(120);
            entity.Property(city => city.CenterLatitude).HasPrecision(9, 6);
            entity.Property(city => city.CenterLongitude).HasPrecision(9, 6);
            entity.Property(city => city.FreeRadiusKm).HasPrecision(6, 2);
            entity.Property(city => city.CoverageRadiusKm).HasPrecision(6, 2);
            entity.Property(city => city.ContactUrl).HasMaxLength(512);
            entity.HasOne(city => city.Destination)
                .WithMany()
                .HasForeignKey(city => city.DestinationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TravelDocument>(entity =>
        {
            entity.HasIndex(document => new { document.TripId, document.Category, document.SortOrder });
            entity.HasIndex(document => new { document.TripId, document.ExternalId }).IsUnique();
            entity.Property(document => document.ExternalId).HasMaxLength(160);
            entity.Property(document => document.Category)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(document => document.Title).HasMaxLength(160);
            entity.Property(document => document.Subtitle).HasMaxLength(220);
            entity.Property(document => document.FileUrl).HasMaxLength(512);
            entity.HasOne(document => document.Trip)
                .WithMany(trip => trip.Documents)
                .HasForeignKey(document => document.TripId)
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.HasIndex(session => new { session.TripId, session.RevokedAt });
            entity.Property(session => session.TokenHash).HasMaxLength(128);
            entity.Property(session => session.AccessMode)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(TravelCompanion.Shared.SessionAccessMode.Trip);
            entity.HasOne(session => session.User)
                .WithMany(user => user.Sessions)
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(session => session.Trip)
                .WithMany()
                .HasForeignKey(session => session.TripId)
                .OnDelete(DeleteBehavior.SetNull);
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
            entity.Property(conversation => conversation.LastIntent).HasMaxLength(80);
            entity.Property(conversation => conversation.LastLocale).HasMaxLength(32);
            entity.Property(conversation => conversation.LastPromptVersion).HasMaxLength(80);
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

        modelBuilder.Entity<TravelAssistantFeedback>(entity =>
        {
            entity.HasKey(feedback => feedback.Id);
            entity.HasIndex(feedback => new { feedback.UserId, feedback.CreatedAtUtc });
            entity.HasIndex(feedback => new { feedback.RecommendationId, feedback.Signal });
            entity.HasIndex(feedback => new { feedback.ConversationId, feedback.CreatedAtUtc });
            entity.Property(feedback => feedback.ConversationId).HasMaxLength(64);
            entity.Property(feedback => feedback.Signal)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(feedback => feedback.Locale).HasMaxLength(32);
            entity.Property(feedback => feedback.Intent).HasMaxLength(80);
            entity.Property(feedback => feedback.ResponseMode).HasMaxLength(40);
            entity.HasOne(feedback => feedback.User)
                .WithMany(user => user.TravelAssistantFeedbackItems)
                .HasForeignKey(feedback => feedback.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(feedback => feedback.Conversation)
                .WithMany(conversation => conversation.FeedbackItems)
                .HasForeignKey(feedback => feedback.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(feedback => feedback.Recommendation)
                .WithMany()
                .HasForeignKey(feedback => feedback.RecommendationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecommendationInteractionSignal>(entity =>
        {
            entity.HasKey(signal => signal.Id);
            entity.HasIndex(signal => new { signal.UserId, signal.CreatedAtUtc });
            entity.HasIndex(signal => new { signal.UserId, signal.TripId, signal.RecommendationId, signal.Signal });
            entity.HasIndex(signal => new { signal.RecommendationId, signal.Signal });
            entity.Property(signal => signal.Signal)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(signal => signal.Source).HasMaxLength(80);
            entity.Property(signal => signal.Latitude).HasPrecision(9, 6);
            entity.Property(signal => signal.Longitude).HasPrecision(9, 6);
            entity.Property(signal => signal.DistanceMeters).HasPrecision(10, 2);
            entity.Property(signal => signal.Confidence).HasPrecision(5, 4);
            entity.HasOne(signal => signal.User)
                .WithMany()
                .HasForeignKey(signal => signal.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(signal => signal.Trip)
                .WithMany()
                .HasForeignKey(signal => signal.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(signal => signal.Recommendation)
                .WithMany()
                .HasForeignKey(signal => signal.RecommendationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationDeviceRegistration>(entity =>
        {
            entity.HasIndex(device => new { device.UserId, device.InstallationId }).IsUnique();
            entity.HasIndex(device => new { device.UserId, device.DisabledAtUtc });
            entity.Property(device => device.InstallationId).HasMaxLength(160);
            entity.Property(device => device.Platform).HasMaxLength(32);
            entity.Property(device => device.PushToken).HasMaxLength(1024);
            entity.Property(device => device.Locale).HasMaxLength(32);
            entity.Property(device => device.TimeZoneId).HasMaxLength(120);
            entity.HasOne(device => device.User)
                .WithMany(user => user.NotificationDevices)
                .HasForeignKey(device => device.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationOutboxItem>(entity =>
        {
            entity.HasIndex(notification => notification.DeduplicationKey).IsUnique();
            entity.HasIndex(notification => new { notification.Status, notification.ScheduledForUtc });
            entity.HasIndex(notification => new { notification.UserId, notification.Status });
            entity.Property(notification => notification.DeduplicationKey).HasMaxLength(240);
            entity.Property(notification => notification.Kind).HasMaxLength(80);
            entity.Property(notification => notification.Title).HasMaxLength(160);
            entity.Property(notification => notification.Body).HasMaxLength(500);
            entity.Property(notification => notification.DeepLink).HasMaxLength(512);
            entity.Property(notification => notification.Status).HasMaxLength(32);
            entity.Property(notification => notification.LastError).HasMaxLength(1000);
            entity.HasOne(notification => notification.User)
                .WithMany(user => user.NotificationOutboxItems)
                .HasForeignKey(notification => notification.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(notification => notification.Reservation)
                .WithMany()
                .HasForeignKey(notification => notification.ReservationId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(notification => notification.Recommendation)
                .WithMany()
                .HasForeignKey(notification => notification.RecommendationId)
                .OnDelete(DeleteBehavior.SetNull);
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

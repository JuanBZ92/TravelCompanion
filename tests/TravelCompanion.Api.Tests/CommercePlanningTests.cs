using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class CommercePlanningTests
{
    [Fact]
    public async Task Free_day_improvements_and_chat_have_independent_three_result_limits()
    {
        await using var db = CreateDb();
        var (user, trip, _, _) = await SeedPurchaseTripAsync(db);
        var grant = new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, TripId = trip.Id, DestinationId = trip.DestinationId,
            IsTrial = true, Status = BuilderAccessStatus.Active, TrialEditingStartedAtUtc = DateTimeOffset.UtcNow,
            TrialEditingExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30), TrialDraftExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30)
        };
        db.Add(grant);
        await db.SaveChangesAsync();
        var usage = new AssistantUsageService(db, Microsoft.Extensions.Options.Options.Create(new FreePreviewOptions()),
            Microsoft.Extensions.Options.Options.Create(new StorePurchaseOptions()));
        var cancelled = await usage.ReserveAsync(user.Id, trip.Id, "full-day:failed", default);
        await usage.CancelAsync(cancelled.LeaseId, default);
        var cancelledChat = await usage.ReserveAsync(user.Id, trip.Id, "chat:failed", default);
        await usage.CancelAsync(cancelledChat.LeaseId, default);
        for (var i = 0; i < 3; i++)
        {
            var lease = await usage.ReserveAsync(user.Id, trip.Id, $"full-day:{i}", default);
            Assert.Equal(lease.LeaseId, (await usage.ReserveAsync(user.Id, trip.Id, $"full-day:{i}", default)).LeaseId);
            await usage.CompleteAsync(lease.LeaseId, default);
            await usage.CompleteAsync(lease.LeaseId, default);
            Assert.Equal(lease.LeaseId, (await usage.ReserveAsync(user.Id, trip.Id, $"full-day:{i}", default)).LeaseId);
        }
        Assert.Equal(0, grant.TrialAssistantRequestsUsed);
        await Assert.ThrowsAsync<TrialUpgradeRequiredException>(() => usage.ReserveAsync(user.Id, trip.Id, "full-day:fourth", default));
        for (var i = 0; i < 3; i++)
        {
            var lease = await usage.ReserveAsync(user.Id, trip.Id, $"chat:{i}", default);
            Assert.Equal(lease.LeaseId, (await usage.ReserveAsync(user.Id, trip.Id, $"chat:{i}", default)).LeaseId);
            await usage.CompleteAsync(lease.LeaseId, default);
            Assert.Equal(lease.LeaseId, (await usage.ReserveAsync(user.Id, trip.Id, $"chat:{i}", default)).LeaseId);
        }
        Assert.Equal(3, grant.TrialAssistantRequestsUsed);
        await Assert.ThrowsAsync<TrialUpgradeRequiredException>(() => usage.ReserveAsync(user.Id, trip.Id, "chat:fourth", default));
        Assert.Equal(6, await db.AssistantUsageLeases.CountAsync(l => l.CompletedAtUtc != null));
    }

    [Fact]
    public async Task Free_planning_rejects_day_four_and_expired_editing()
    {
        await using var db = CreateDb();
        var (user, trip, _, _) = await SeedPurchaseTripAsync(db);
        db.Add(new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, TripId = trip.Id, DestinationId = trip.DestinationId,
            IsTrial = true, Status = BuilderAccessStatus.Active, TrialEditingStartedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
            TrialEditingExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30), TrialDraftExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();
        var free = new FreeTrialAccessService(db, Microsoft.Extensions.Options.Options.Create(new FreePreviewOptions()), NullLogger<FreeTrialAccessService>.Instance);
        await free.RequirePlanningDateAsync(user.Id, trip.Id, trip.StartsOn.AddDays(2), default);
        await Assert.ThrowsAsync<TrialUpgradeRequiredException>(() => free.RequirePlanningDateAsync(user.Id, trip.Id, trip.StartsOn.AddDays(3), default));
        await Assert.ThrowsAsync<TrialUpgradeRequiredException>(() => free.RequireEditingAsync(user.Id, false, default));
        Assert.False(FreePlanningPolicy.CanPlanDate(trip.StartsOn, trip.StartsOn.AddDays(-1)));
    }

    [Fact]
    public void Pass_expiry_uses_trip_timezone_and_one_year_cap()
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(), DestinationId = Guid.NewGuid(), TravelerName = "Traveler",
            StartsOn = new DateOnly(2027, 1, 5), EndsOn = new DateOnly(2027, 1, 10), TimeZoneId = "Asia/Tokyo"
        };
        var expiry = StorePurchaseService.CalculateExpiry(trip, new DateTimeOffset(2027, 12, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2027, 1, 17, 15, 0, 0, TimeSpan.Zero), expiry);
        var cap = new DateTimeOffset(2027, 1, 12, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(cap, StorePurchaseService.CalculateExpiry(trip, cap));
    }

    [Fact]
    public void Google_order_money_uses_units_and_nanos()
    {
        using var document = JsonDocument.Parse("""
            { "total": { "currencyCode": "EUR", "units": "24", "nanos": 990000000 } }
            """);

        var amount = GooglePlayReceiptVerifier.ParseOrderAmount(document.RootElement);

        Assert.NotNull(amount);
        Assert.Equal("EUR", amount.Currency);
        Assert.Equal(24.99m, amount.Amount);
    }

    [Fact]
    public async Task Verified_email_recovers_existing_account_without_an_active_session()
    {
        await using var db = CreateDb();
        const string email = "traveler@example.com";
        const string code = "123456";
        const string secret = "test-secret";
        var user = new AppUser { Id = Guid.NewGuid(), Email = email, DisplayName = "Traveler", EmailVerified = true };
        db.AppUsers.Add(user);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        db.EmailVerificationChallenges.Add(new EmailVerificationChallenge
        {
            Id = Guid.NewGuid(), Email = email,
            CodeHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{email}:{code}"))),
            RequestIpHash = "test", CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10), ResendAvailableAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var sessions = new UserSessionService(db);
        var service = new EmailAccountService(db, sessions, new TestEmailSender(),
            Microsoft.Extensions.Options.Options.Create(new EmailVerificationOptions { HashSecret = secret }));

        var recovered = await service.VerifyCodeAsync(new DefaultHttpContext(), new(email, code), default);

        Assert.Equal(user.Id, recovered.UserId);
        Assert.False(string.IsNullOrWhiteSpace(recovered.Token));
        Assert.True((await db.EmailVerificationChallenges.SingleAsync()).ConsumedAtUtc.HasValue);
    }

    [Fact]
    public async Task Verifying_existing_account_moves_the_complete_draft_without_changing_trip_id()
    {
        await using var db = CreateDb();
        const string email = "existing@example.com";
        const string code = "654321";
        const string secret = "test-secret";
        var destination = new Destination { Id = Guid.NewGuid(), Name = "Japan", Slug = "japan", Country = "Japan", ShortDescription = "", HeroImageUrl = "", TimeZoneId = "Asia/Tokyo" };
        var target = new AppUser { Id = Guid.NewGuid(), Email = email, DisplayName = "Existing", EmailVerified = true };
        var source = new AppUser { Id = Guid.NewGuid(), Email = $"{FreePreviewAccountService.AccountEmailPrefix}{Guid.NewGuid():N}@travelcompanion.system", DisplayName = "Preview" };
        var trip = new Trip
        {
            Id = Guid.NewGuid(), AppUserId = source.Id, DestinationId = destination.Id, TravelerName = "Preview",
            StartsOn = new DateOnly(2027, 4, 1), EndsOn = new DateOnly(2027, 4, 5), TimeZoneId = "Asia/Tokyo",
            ExperienceMode = ExperienceMode.SelfServiceBuilder
        };
        var grant = new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = source.Id, DestinationId = destination.Id, TripId = trip.Id,
            IsTrial = true, Status = BuilderAccessStatus.Active, ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30)
        };
        var intent = new StorePurchaseIntent
        {
            Id = Guid.NewGuid(), AppUserId = source.Id, TripId = trip.Id, Provider = StoreProvider.Google,
            ProductId = "japan_pass", OpaqueAccountId = Guid.NewGuid().ToString("N"), State = PurchaseIntentState.Preparing,
            EntryPoint = PaywallEntryPoint.ExplicitUpgrade, PaywallVariant = "continuity", CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var route = new ThematicRoute
        {
            Id = Guid.NewGuid(), AppUserId = source.Id, TripId = trip.Id, DestinationId = destination.Id,
            Name = "Tokyo", Theme = RouteTheme.Food, City = "Tokyo", Origin = RouteOrigin.Personal,
            Status = RoutePublicationStatus.Published, WarningsJson = "[]", CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var proposal = new ItineraryProposal
        {
            Id = Guid.NewGuid(), AppUserId = source.Id, TripId = trip.Id, SourceRouteId = route.Id,
            Date = trip.StartsOn, Goal = DayPlanningGoal.Reorganize, ChangesJson = "[]", WarningsJson = "[]",
            IdempotencyKey = "merge-proposal", CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        var operation = new ItineraryOperation
        {
            Id = Guid.NewGuid(), AppUserId = source.Id, TripId = trip.Id, IdempotencyKey = "merge-operation",
            PreviousStateJson = "[]", AppliedAtUtc = DateTimeOffset.UtcNow, UndoAvailableUntilUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        var sourceAssignment = new ProductExperimentAssignment
        {
            Id = Guid.NewGuid(), AppUserId = source.Id, Experiment = "paywall-copy-v1", Variant = "continuity",
            AssignedAtUtc = DateTimeOffset.UtcNow
        };
        var targetAssignment = new ProductExperimentAssignment
        {
            Id = Guid.NewGuid(), AppUserId = target.Id, Experiment = "paywall-copy-v1", Variant = "catalog-tools",
            AssignedAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
        };
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var challenge = new EmailVerificationChallenge
        {
            Id = Guid.NewGuid(), Email = email,
            CodeHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{email}:{code}"))),
            RequestIpHash = "test", CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10), ResendAvailableAtUtc = DateTimeOffset.UtcNow
        };
        db.AddRange(destination, target, source, trip, grant, intent, route, proposal, operation,
            sourceAssignment, targetAssignment, challenge);
        await db.SaveChangesAsync();
        var sessions = new UserSessionService(db);
        var (_, token) = await sessions.CreateSessionAsync(source, tripId: trip.Id, accessMode: SessionAccessMode.FreeMapPreview);
        var http = new DefaultHttpContext(); http.Request.Headers.Authorization = $"Bearer {token}";
        var service = new EmailAccountService(db, sessions, new TestEmailSender(),
            Microsoft.Extensions.Options.Options.Create(new EmailVerificationOptions { HashSecret = secret }));

        var linked = await service.VerifyCodeAsync(http, new(email, code), default);

        Assert.Equal(target.Id, linked.UserId);
        Assert.Equal(trip.Id, linked.TripId);
        Assert.Equal(target.Id, (await db.Trips.SingleAsync(item => item.Id == trip.Id)).AppUserId);
        Assert.Equal(target.Id, (await db.BuilderAccessGrants.SingleAsync(item => item.Id == grant.Id)).AppUserId);
        Assert.Equal(target.Id, (await db.StorePurchaseIntents.SingleAsync(item => item.Id == intent.Id)).AppUserId);
        Assert.Equal(target.Id, (await db.ThematicRoutes.SingleAsync(item => item.Id == route.Id)).AppUserId);
        Assert.Equal(target.Id, (await db.ItineraryProposals.SingleAsync(item => item.Id == proposal.Id)).AppUserId);
        Assert.Equal(target.Id, (await db.ItineraryOperations.SingleAsync(item => item.Id == operation.Id)).AppUserId);
        var assignment = await db.ProductExperimentAssignments.SingleAsync(item => item.Experiment == "paywall-copy-v1");
        Assert.Equal(target.Id, assignment.AppUserId);
        Assert.Equal("continuity", assignment.Variant);
    }

    [Fact]
    public async Task Client_behavior_events_are_only_persisted_with_consent()
    {
        await using var db = CreateDb();
        var user = new AppUser
        {
            Id = Guid.NewGuid(), Email = $"free-preview+{Guid.NewGuid():N}@travelcompanion.system", DisplayName = "Preview",
            BehaviorAnalyticsConsent = true
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var sessions = new UserSessionService(db);
        var (_, token) = await sessions.CreateSessionAsync(user, accessMode: SessionAccessMode.FreeMapPreview);
        var http = new DefaultHttpContext(); http.Request.Headers.Authorization = $"Bearer {token}";
        var service = new ProductAnalyticsService(db);
        var now = DateTimeOffset.UtcNow;

        var accepted = await service.IngestAsync(http, new ProductAnalyticsBatchDto([
            new(Guid.NewGuid(), "paywall_shown", now, "map", "1.0", "android", "continuity", null, false),
            new(Guid.NewGuid(), "paywall_shown", now, "map", "1.0", "android", "continuity", null, true)
        ]), sessions, default);

        Assert.Equal(1, accepted);
        var stored = await db.ProductAnalyticsEvents.SingleAsync();
        Assert.True(stored.BehaviorConsent);
        Assert.Equal(user.Id, stored.AnonymousUserId);
        Assert.Null(stored.AppUserId);
        Assert.Equal(SessionAccessMode.FreeMapPreview.ToString(), stored.AccessState);
    }

    [Fact]
    public async Task Current_trip_can_be_archived_and_is_unbound_from_the_session()
    {
        await using var db = CreateDb();
        var (user, trip, sessions, http) = await SeedPurchaseTripAsync(db);
        var service = new EmailAccountService(db, sessions, new TestEmailSender(),
            Microsoft.Extensions.Options.Options.Create(new EmailVerificationOptions { HashSecret = "test-secret" }));

        await service.ArchiveTripAsync(http, trip.Id, default);

        Assert.True((await db.Trips.SingleAsync(item => item.Id == trip.Id)).IsArchived);
        var storedSession = await db.AppUserSessions.SingleAsync(item => item.UserId == user.Id && item.RevokedAt == null);
        Assert.Null(storedSession.TripId);
        Assert.Equal(SessionAccessMode.FreeMapPreview, storedSession.AccessMode);
        Assert.Single(await db.AppUserSessions.Where(item => item.UserId == user.Id && item.RevokedAt != null).ToListAsync());
    }

    [Fact]
    public async Task Expired_purchased_pass_keeps_the_trip_readable_without_premium_capabilities()
    {
        await using var db = CreateDb();
        var destination = new Destination
        {
            Id = Guid.NewGuid(), Name = "Japan", Slug = "japan", Country = "Japan",
            ShortDescription = "", HeroImageUrl = "", TimeZoneId = "Asia/Tokyo"
        };
        var user = new AppUser
        {
            Id = Guid.NewGuid(), Email = "expired@example.com", DisplayName = "Traveler", EmailVerified = true
        };
        var trip = new Trip
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, DestinationId = destination.Id,
            TravelerName = user.DisplayName, StartsOn = new DateOnly(2026, 4, 1),
            EndsOn = new DateOnly(2026, 4, 10), TimeZoneId = "Asia/Tokyo",
            ExperienceMode = ExperienceMode.SelfServiceBuilder
        };
        db.AddRange(destination, user, trip, new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, DestinationId = destination.Id, TripId = trip.Id,
            IsTrial = false, Status = BuilderAccessStatus.Expired,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var sessions = new UserSessionService(db);
        var (storedSession, token) = await sessions.CreateSessionAsync(
            user, tripId: trip.Id, accessMode: SessionAccessMode.Builder);
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = $"Bearer {token}";
        var free = new FreeTrialAccessService(
            db,
            Microsoft.Extensions.Options.Options.Create(new FreePreviewOptions()),
            NullLogger<FreeTrialAccessService>.Instance);

        var access = await new TravelerAccessService(sessions, free).GetAsync(http, default);

        Assert.NotNull(access);
        Assert.Equal(trip.Id, access.TripId);
        Assert.Equal(SessionAccessMode.BuilderReadOnly, access.Session.AccessMode);
        Assert.False(access.Capabilities.CanEditItinerary);
        Assert.False(access.Capabilities.CanSearchGooglePlaces);
        Assert.False(access.Capabilities.CanCalculateRoutes);
        Assert.Equal(
            SessionAccessMode.BuilderReadOnly,
            (await db.AppUserSessions.SingleAsync(item => item.Id == storedSession.Id)).AccessMode);
    }

    [Fact]
    public async Task Pending_purchase_intents_are_scoped_to_the_requested_store()
    {
        await using var db = CreateDb();
        var (user, trip, sessions, http) = await SeedPurchaseTripAsync(db);
        var service = CreatePurchaseService(db, sessions, []);

        var apple = await service.CreateIntentAsync(http,
            new(trip.Id, StoreProvider.Apple, "apple.pass", PaywallEntryPoint.Map, "continuity"), default);
        var google = await service.CreateIntentAsync(http,
            new(trip.Id, StoreProvider.Google, "google.pass", PaywallEntryPoint.Map, "continuity"), default);

        Assert.NotEqual(apple.Id, google.Id);
        Assert.Equal(StoreProvider.Apple, apple.Provider);
        Assert.Equal(StoreProvider.Google, google.Provider);
        Assert.Equal(2, await db.StorePurchaseIntents.CountAsync(item => item.AppUserId == user.Id));
    }

    [Fact]
    public async Task Purchase_evidence_must_match_the_intent_account_and_active_retry_is_idempotent()
    {
        await using var db = CreateDb();
        var (_, trip, sessions, http) = await SeedPurchaseTripAsync(db);
        var verifier = new TestStoreVerifier(StoreProvider.Apple);
        var service = CreatePurchaseService(db, sessions, [verifier]);
        var mismatchIntent = await service.CreateIntentAsync(http,
            new(trip.Id, StoreProvider.Apple, "apple.pass", PaywallEntryPoint.Assistant, "catalog-tools"), default);
        verifier.Result = new(true, false, "apple-mismatch", null, "apple.pass", DateTimeOffset.UtcNow,
            OpaqueAccountId: Guid.NewGuid().ToString("D"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerifyAsync(http,
            new(mismatchIntent.Id, "signed-evidence", StoreEnvironment.Sandbox, "verify-mismatch"), default));
        Assert.Equal(PurchaseIntentState.Failed,
            (await db.StorePurchaseIntents.SingleAsync(item => item.Id == mismatchIntent.Id)).State);

        var validIntent = await service.CreateIntentAsync(http,
            new(trip.Id, StoreProvider.Apple, "apple.pass", PaywallEntryPoint.Assistant, "catalog-tools"), default);
        verifier.Result = new(true, false, "apple-valid", null, "apple.pass", DateTimeOffset.UtcNow,
            OpaqueAccountId: validIntent.OpaqueAccountId);
        var first = await service.VerifyAsync(http,
            new(validIntent.Id, "signed-evidence-2", StoreEnvironment.Sandbox, "verify-valid"), default);
        var second = await service.VerifyAsync(http,
            new(validIntent.Id, "signed-evidence-2", StoreEnvironment.Sandbox, "verify-valid-retry"), default);

        Assert.Equal(TrialAccessState.Paid, first.State);
        Assert.Equal(first, second);
        Assert.Equal(2, verifier.CallCount);
        Assert.Single(await db.StorePurchaseTransactions.ToListAsync());
        Assert.Equal(1, await db.ProductAnalyticsEvents.CountAsync(item => item.Name == "purchase_verified"));
        Assert.Equal(1, await db.ProductAnalyticsEvents.CountAsync(item => item.Name == "pass_activated"));
    }

    [Fact]
    public async Task Late_store_confirmation_restores_the_snapshotted_draft_with_the_same_trip_id()
    {
        await using var db = CreateDb();
        var (_, trip, sessions, http) = await SeedPurchaseTripAsync(db);
        var day = new TripDayPlan
        {
            Id = Guid.NewGuid(), TripId = trip.Id, Date = trip.StartsOn, DayNumber = 1, City = "Tokyo",
            HotelBase = "Ueno", BaseAddress = "Tokyo",
            Blocks = TripPlanPeriods.All.Select(period => new TripDayBlock
            {
                Id = Guid.NewGuid(), PeriodKey = period.Key, SortOrder = period.SortOrder
            }).ToList()
        };
        foreach (var block in day.Blocks) block.TripDayPlanId = day.Id;
        var item = Reservation(trip.Id, trip.StartsOn, "Night walk", new TimeOnly(20, 0),
            ItineraryFlexibility.Flexible, day.Blocks[2].Id);
        db.AddRange(day, item);
        await db.SaveChangesAsync();
        var verifier = new TestStoreVerifier(StoreProvider.Apple);
        var service = CreatePurchaseService(db, sessions, [verifier]);
        var intentDto = await service.CreateIntentAsync(http,
            new(trip.Id, StoreProvider.Apple, "apple.pass", PaywallEntryPoint.Today, "continuity"), default);
        var intent = await db.StorePurchaseIntents.SingleAsync(item => item.Id == intentDto.Id);
        intent.CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2);
        intent.ExpiresAtUtc = intent.CreatedAtUtc.AddDays(1);
        db.Reservations.RemoveRange(await db.Reservations.Where(x => x.TripId == trip.Id).ToListAsync());
        db.TripDayBlocks.RemoveRange(await db.TripDayBlocks.Where(x => x.TripDayPlanId == day.Id).ToListAsync());
        db.TripDayPlans.RemoveRange(await db.TripDayPlans.Where(x => x.TripId == trip.Id).ToListAsync());
        trip.IsArchived = true;
        await db.SaveChangesAsync();
        verifier.Result = new(true, false, "apple-late", null, "apple.pass", intent.CreatedAtUtc.AddHours(1),
            OpaqueAccountId: intent.OpaqueAccountId);

        var pass = await service.VerifyAsync(http,
            new(intent.Id, "late-signed-evidence", StoreEnvironment.Sandbox, "late-verify"), default);

        Assert.Equal(TrialAccessState.Paid, pass.State);
        var restoredTrip = await db.Trips.AsNoTracking().SingleAsync(x => x.Id == trip.Id);
        Assert.False(restoredTrip.IsArchived);
        Assert.Single(await db.TripDayPlans.Where(x => x.TripId == trip.Id).ToListAsync());
        var restoredItem = await db.Reservations.SingleAsync(x => x.TripId == trip.Id);
        Assert.Equal(item.Id, restoredItem.Id);
        Assert.Equal("Night walk", restoredItem.Title);
    }

    private static Reservation Reservation(Guid tripId, DateOnly date, string title, TimeOnly start,
        ItineraryFlexibility flexibility, Guid? blockId) => new()
    {
        Id = Guid.NewGuid(), TripId = tripId, TripDayBlockId = blockId, Type = ReservationType.Event,
        PlanningKind = flexibility == ItineraryFlexibility.ConfirmedReservation ? ScheduleItemKind.ConfirmedReservation : ScheduleItemKind.ManualEvent,
        Owner = ItineraryItemOwner.Traveler, ItemSource = ItineraryItemSource.Manual, TimePrecision = ItineraryTimePrecision.Exact,
        Flexibility = flexibility, DurationMinutes = 60, Date = date, StartsAt = start, EndsAt = start.AddMinutes(60),
        TimeZoneId = "Asia/Tokyo", Title = title, City = "Tokyo", LocationName = title, Address = "", ConfirmationCode = "", Notes = ""
    };
    private static TravelCompanionDbContext CreateDb() => new(new DbContextOptionsBuilder<TravelCompanionDbContext>()
        .UseInMemoryDatabase($"commerce-planning-{Guid.NewGuid():N}").Options);

    private static async Task<(AppUser User, Trip Trip, UserSessionService Sessions, DefaultHttpContext Http)>
        SeedPurchaseTripAsync(TravelCompanionDbContext db)
    {
        var destination = new Destination
        {
            Id = Guid.NewGuid(), Name = "Japan", Slug = $"japan-{Guid.NewGuid():N}", Country = "Japan",
            ShortDescription = "", HeroImageUrl = "", TimeZoneId = "Asia/Tokyo"
        };
        var user = new AppUser
        {
            Id = Guid.NewGuid(), Email = $"buyer-{Guid.NewGuid():N}@example.com", DisplayName = "Buyer", EmailVerified = true
        };
        var trip = new Trip
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, DestinationId = destination.Id, TravelerName = user.DisplayName,
            StartsOn = new DateOnly(2027, 6, 1), EndsOn = new DateOnly(2027, 6, 8), TimeZoneId = "Asia/Tokyo",
            ExperienceMode = ExperienceMode.SelfServiceBuilder
        };
        db.AddRange(destination, user, trip);
        await db.SaveChangesAsync();
        var sessions = new UserSessionService(db);
        var (_, token) = await sessions.CreateSessionAsync(user, tripId: trip.Id, accessMode: SessionAccessMode.FreeMapPreview);
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = $"Bearer {token}";
        return (user, trip, sessions, http);
    }

    private static StorePurchaseService CreatePurchaseService(TravelCompanionDbContext db,
        UserSessionService sessions, IEnumerable<IStoreReceiptVerifier> verifiers) => new(
        db, sessions, verifiers, [], new EphemeralDataProtectionProvider(), new ProductAnalyticsService(db),
        Microsoft.Extensions.Options.Options.Create(new StorePurchaseOptions
        {
            NewPurchasesEnabled = true,
            AppleProductId = "apple.pass",
            GoogleProductId = "google.pass"
        }));

    private sealed class TestEmailSender : ITransactionalEmailSender
    {
        public Task SendVerificationCodeAsync(string email, string code, string locale, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestStoreVerifier(StoreProvider provider) : IStoreReceiptVerifier
    {
        public StoreProvider Provider { get; } = provider;
        public VerifiedStorePurchase Result { get; set; } = new(false, false, null, null, string.Empty, default);
        public int CallCount { get; private set; }
        public Task<VerifiedStorePurchase> VerifyAsync(string evidence, StoreEnvironment environment, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }
}

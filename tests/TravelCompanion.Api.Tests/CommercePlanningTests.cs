using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
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
    [Theory]
    [InlineData("GET", "/api/mobile/conversion/paywall/00000000-0000-0000-0000-000000000001", true)]
    [InlineData("POST", "/api/mobile/conversion/events", true)]
    [InlineData("POST", "/api/mobile/account/email/code", true)]
    [InlineData("POST", "/api/mobile/account/email/verify", true)]
    [InlineData("GET", "/api/mobile/account", true)]
    [InlineData("POST", "/api/mobile/purchases/intents", true)]
    [InlineData("POST", "/api/mobile/purchases/verify", true)]
    [InlineData("POST", "/api/mobile/purchases/restore", true)]
    [InlineData("GET", "/api/mobile/purchases-extra", false)]
    [InlineData("GET", "/api/admin/users", false)]
    public async Task Free_session_can_reach_commerce_but_not_unrelated_routes(string method, string path, bool allowed)
    {
        await using var db = CreateDb();
        var (_, _, sessions, http) = await SeedPurchaseTripAsync(db);
        http.Request.Method = method;
        http.Request.Path = path;
        http.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        var reached = false;
        var guard = new TravelCompanion.Api.Middleware.FreePreviewSessionGuardMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        });
        await guard.InvokeAsync(http, sessions);
        Assert.Equal(allowed, reached);
        Assert.Equal(allowed ? 200 : 403, http.Response.StatusCode);
    }

    [Fact]
    public async Task Free_session_offer_pipeline_retains_trip_ownership_check()
    {
        await using var db = CreateDb();
        var (_, trip, sessions, http) = await SeedPurchaseTripAsync(db);
        var (_, otherTrip, _, _) = await SeedPurchaseTripAsync(db);
        var service = new PaywallOfferService(db, sessions,
            Microsoft.Extensions.Options.Options.Create(new StorePurchaseOptions()),
            Microsoft.Extensions.Options.Options.Create(new ProductFeatureOptions()));
        http.Request.Method = "GET";
        http.Request.Path = $"/api/mobile/conversion/paywall/{trip.Id}";
        PaywallOfferDto? offer = null;
        var guard = new TravelCompanion.Api.Middleware.FreePreviewSessionGuardMiddleware(async context =>
        {
            offer = await service.GetAsync(context, trip.Id, PaywallEntryPoint.Today, "android", default);
        });
        await guard.InvokeAsync(http, sessions);
        Assert.NotNull(offer);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(http, otherTrip.Id, PaywallEntryPoint.Today, "android", default));
    }

    [Fact]
    public async Task Conversion_attributes_server_events_using_matching_client_events()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        foreach (var platform in new[] { "android", "ios" })
        {
            var user = Guid.NewGuid();
            var names = new[] { "trial_started", "first_item_saved", "paywall_shown", "checkout_started", "purchase_verified", "pass_activated" };
            for (var i = 0; i < names.Length; i++) db.Add(new ProductAnalyticsEvent
            {
                Id = Guid.NewGuid(), EventId = Guid.NewGuid(), AppUserId = user, Name = names[i],
                OccurredAtUtc = now.AddDays(-40).AddHours(i), ReceivedAtUtc = now, BehaviorConsent = true,
                Platform = names[i] == "paywall_shown" ? platform : null,
                PaywallVariant = names[i] == "paywall_shown" ? "contextual-v2" : null
            });
        }
        await db.SaveChangesAsync();
        var page = new TravelCompanion.Api.Pages.Admin.ConversionModel(db);
        await page.OnGetAsync(DateOnly.FromDateTime(now.AddDays(-41).UtcDateTime),
            DateOnly.FromDateTime(now.UtcDateTime), "android", null, null, "contextual-v2");
        Assert.All(page.Stages, stage => Assert.Equal(1, stage.Users));
        Assert.Equal(1, page.ThirtyDayCohortSize);
        Assert.Equal(100d, page.ThirtyDayConversionPercent);
    }

    [Fact]
    public async Task Conversion_uses_later_ordered_paywall_and_does_not_reactivate_existing_users()
    {
        await using var db = CreateDb();
        var current = Guid.NewGuid();
        var existing = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        void Add(Guid user, string name, int days) => db.Add(new ProductAnalyticsEvent
        {
            Id = Guid.NewGuid(), EventId = Guid.NewGuid(), AppUserId = user, Name = name,
            OccurredAtUtc = now.AddDays(days), ReceivedAtUtc = now, BehaviorConsent = true
        });
        Add(current, "trial_started", -39);
        Add(current, "paywall_shown", -37);
        Add(current, "first_item_saved", -35);
        Add(current, "paywall_shown", -34);
        Add(current, "checkout_started", -33);
        Add(current, "purchase_verified", -32);
        Add(current, "pass_activated", -31);
        Add(existing, "first_item_saved", -50);
        Add(existing, "first_item_saved", -30);
        await db.SaveChangesAsync();
        var page = new TravelCompanion.Api.Pages.Admin.ConversionModel(db);
        await page.OnGetAsync(DateOnly.FromDateTime(now.AddDays(-40).UtcDateTime),
            DateOnly.FromDateTime(now.UtcDateTime), null, null, null, null);
        Assert.All(page.Stages, stage => Assert.Equal(1, stage.Users));
        Assert.Equal(1, page.ThirtyDayCohortSize);
        Assert.Equal(100d, page.ThirtyDayConversionPercent);
        Assert.Equal(100d, page.SevenDayConversionPercent);
    }

    [Fact]
    public async Task Persistent_draft_is_never_selected_by_timed_trial_cleanup()
    {
        await using var db = CreateDb();
        var (user, trip, _, _) = await SeedPurchaseTripAsync(db);
        db.Add(new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, TripId = trip.Id, DestinationId = trip.DestinationId,
            IsTrial = true, FreePolicy = FreeAccessPolicy.PersistentFree,
            TrialEditingStartedAtUtc = DateTimeOffset.UtcNow.AddDays(-40),
            TrialDraftExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        using var provider = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var worker = new ExpiredTrialCleanupWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ExpiredTrialCleanupWorker>.Instance);
        Assert.Equal(0, await worker.PurgeAsync(default));
        Assert.True(await db.Trips.AnyAsync(item => item.Id == trip.Id));
    }

    [Fact]
    public async Task Contextual_paywall_uses_actual_quota_and_never_threatens_persistent_draft_deletion()
    {
        await using var db = CreateDb();
        var (user, trip, sessions, http) = await SeedPurchaseTripAsync(db);
        db.Add(new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, TripId = trip.Id, DestinationId = trip.DestinationId,
            IsTrial = true, FreePolicy = FreeAccessPolicy.PersistentFree, TrialDraftExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
        });
        await db.SaveChangesAsync();
        var settings = new StorePurchaseOptions { DailyAssistantLimit = 17, NewPurchasesEnabled = false };
        var service = new PaywallOfferService(db, sessions, Microsoft.Extensions.Options.Options.Create(settings),
            Microsoft.Extensions.Options.Options.Create(new ProductFeatureOptions()));
        var map = await service.GetAsync(http, trip.Id, PaywallEntryPoint.Map, "android", default);
        var assistant = await service.GetAsync(http, trip.Id, PaywallEntryPoint.Assistant, "android", default);
        Assert.Null(map.DraftDeletionAtUtc);
        Assert.Equal("contextual-v2", map.Variant);
        Assert.NotEqual(map.Benefits[0], assistant.Benefits[0]);
        var offline = await service.GetAsync(http, trip.Id, PaywallEntryPoint.Offline, "android", default);
        Assert.NotEqual(map.Benefits[0], offline.Benefits[0]);
        Assert.Contains(offline.Benefits, benefit => benefit.Contains("PDF"));
        Assert.Contains(map.Benefits, item => item.Contains("17"));
        Assert.False(map.CanPurchase);
        settings.NewPurchasesEnabled = true;
        Assert.True((await service.GetAsync(http, trip.Id, PaywallEntryPoint.Today, "ios", default)).CanPurchase);
        Assert.False((await service.GetAsync(http, trip.Id, PaywallEntryPoint.Today, "desktop", default)).CanPurchase);
    }

    [Fact]
    public async Task Analytics_keeps_free_policy_separate_from_paywall_variant_and_respects_consent()
    {
        await using var db = CreateDb();
        var (user, trip, _, _) = await SeedPurchaseTripAsync(db);
        db.Add(new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, TripId = trip.Id, DestinationId = trip.DestinationId,
            IsTrial = true, FreePolicy = FreeAccessPolicy.PersistentFree
        });
        await db.SaveChangesAsync();
        var analytics = new ProductAnalyticsService(db);
        await analytics.RecordServerEventAsync(user.Id, trip.Id, "first_item_saved", "itinerary", "contextual-v2", default);
        Assert.Empty(db.ProductAnalyticsEvents);
        user.BehaviorAnalyticsConsent = true;
        await db.SaveChangesAsync();
        await analytics.RecordServerEventAsync(user.Id, trip.Id, "first_item_saved", "itinerary", "contextual-v2", default);
        var item = await db.ProductAnalyticsEvents.SingleAsync();
        Assert.Equal("PersistentFree", item.FreePolicyVariant);
        Assert.Equal("contextual-v2", item.PaywallVariant);
        var grant = await db.BuilderAccessGrants.SingleAsync();
        grant.IsTrial = false;
        await db.SaveChangesAsync();
        await analytics.RecordServerEventAsync(user.Id, trip.Id, "purchase_verified", "checkout", "contextual-v2", default);
        Assert.Equal("PersistentFree", (await db.ProductAnalyticsEvents.SingleAsync(value => value.Name == "purchase_verified")).FreePolicyVariant);
        Assert.True(item.BehaviorConsent);
    }

    [Fact]
    public async Task Persistent_free_preserves_editing_and_quotas_without_expiry()
    {
        await using var db = CreateDb();
        var (user, trip, _, _) = await SeedPurchaseTripAsync(db);
        var grant = new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, TripId = trip.Id, DestinationId = trip.DestinationId,
            IsTrial = true, FreePolicy = FreeAccessPolicy.PersistentFree, Status = BuilderAccessStatus.Active
        };
        db.Add(grant);
        await db.SaveChangesAsync();
        var service = new FreeTrialAccessService(db, Microsoft.Extensions.Options.Options.Create(new FreePreviewOptions()), NullLogger<FreeTrialAccessService>.Instance);
        var started = await service.StartEditingAsync(user.Id);
        Assert.Null(started.EditingExpiresAtUtc);
        Assert.Null(started.DraftExpiresAtUtc);
        Assert.Equal(TrialAccessState.Editing, service.ToStatus(grant, DateTimeOffset.UtcNow.AddDays(31)).State);
        var usage = new AssistantUsageService(db, Microsoft.Extensions.Options.Options.Create(new FreePreviewOptions()), Microsoft.Extensions.Options.Options.Create(new StorePurchaseOptions()));
        for (var i = 0; i < 3; i++)
        {
            var lease = await usage.ReserveAsync(user.Id, trip.Id, $"chat:{i}", default);
            await usage.CompleteAsync(lease.LeaseId, default);
        }
        var blocked = await Assert.ThrowsAsync<TrialUpgradeRequiredException>(() => usage.ReserveAsync(user.Id, trip.Id, "chat:4", default));
        Assert.Equal(FreeAccessPolicy.PersistentFree, blocked.Status.FreePolicy);
        Assert.Equal(TrialAccessState.Editing, blocked.Status.State);
        var day = await usage.ReserveAsync(user.Id, trip.Id, "full-day:1", default);
        await usage.CompleteAsync(day.LeaseId, default);
        Assert.Equal(2, (await service.GetStatusAsync(user.Id)).DayImprovementsRemaining);
        await Assert.ThrowsAsync<TrialUpgradeRequiredException>(() => service.RequirePlanningDateAsync(user.Id, trip.Id, trip.StartsOn.AddDays(3), default));
        grant.RevokedAtUtc = DateTimeOffset.UtcNow;
        Assert.Equal(TrialAccessState.Revoked, service.ToStatus(grant).State);
    }

    [Fact]
    public async Task Free_policy_is_assigned_only_to_new_compatible_accounts_and_survives_rollout_changes()
    {
        await using var db = CreateDb();
        await SeedPurchaseTripAsync(db);
        var options = new FreePreviewOptions { PersistentFreePercent = 100 };
        var service = new FreePreviewAccountService(db, Microsoft.Extensions.Options.Options.Create(options));
        var legacy = await service.GetOrCreateAsync("legacy-client");
        var compatible = await service.GetOrCreateAsync("new-client", default, true);
        Assert.Equal(FreeAccessPolicy.TimedTrial, (await db.BuilderAccessGrants.SingleAsync(g => g.AppUserId == legacy.Id)).FreePolicy);
        Assert.Equal(FreeAccessPolicy.PersistentFree, (await db.BuilderAccessGrants.SingleAsync(g => g.AppUserId == compatible.Id)).FreePolicy);
        options.PersistentFreePercent = 0;
        await service.GetOrCreateAsync("new-client", default, true);
        await service.GetOrCreateAsync("legacy-client", default, true);
        Assert.Equal(FreeAccessPolicy.PersistentFree, (await db.BuilderAccessGrants.SingleAsync(g => g.AppUserId == compatible.Id)).FreePolicy);
        Assert.Equal(FreeAccessPolicy.TimedTrial, (await db.BuilderAccessGrants.SingleAsync(g => g.AppUserId == legacy.Id)).FreePolicy);
    }

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
            IsTrial = true, Status = BuilderAccessStatus.Active, FreePolicy = FreeAccessPolicy.PersistentFree,
            TrialEditingStartedAtUtc = DateTimeOffset.UtcNow.AddDays(-40)
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
            Microsoft.Extensions.Options.Options.Create(new EmailVerificationOptions { HashSecret = secret }),
            freeTrialAccessService: new FreeTrialAccessService(db,
                Microsoft.Extensions.Options.Options.Create(new FreePreviewOptions()), NullLogger<FreeTrialAccessService>.Instance));

        var linked = await service.VerifyCodeAsync(http, new(email, code), default);

        Assert.Equal(FreeAccessPolicy.PersistentFree, linked.TrialAccess?.FreePolicy);
        Assert.Equal(TrialAccessState.Editing, linked.TrialAccess?.State);
        Assert.True(linked.Capabilities?.CanEditItinerary);
        Assert.Equal(3, linked.TrialAccess?.DayImprovementsRemaining);
        var selectedContext = new DefaultHttpContext();
        selectedContext.Request.Headers.Authorization = $"Bearer {linked.Token}";
        var selected = await service.SelectTripAsync(selectedContext, trip.Id, default);
        Assert.Equal(linked.TrialAccess, selected.TrialAccess);
        Assert.True(selected.Capabilities?.CanEditItinerary);
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

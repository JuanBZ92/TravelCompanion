using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class PostgresDayPlanTests
{
    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Concurrent_generation_retries_return_one_result_and_consume_one_use()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed, trial: true);
        world.User.BehaviorAnalyticsConsent = true;
        await seed.SaveChangesAsync();
        var request = world.Request(3);
        var warnings = new System.Collections.Concurrent.ConcurrentBag<string>();
        async Task<DayPlanResponse> Generate()
        {
            await using var db = Open(database);
            var response = await DayPlanTestWorld.Service(db, analytics: true, logger: new CaptureWarnings(warnings)).GenerateAsync(world.Access(), request, default);
            Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
            return response;
        }

        var responses = await Task.WhenAll(Generate(), Generate(), Generate());
        Assert.All(responses, result => Assert.Equal(JsonSerializer.Serialize(responses[0]), JsonSerializer.Serialize(result)));
        Assert.All(responses, result => Assert.NotEmpty(result.Days.SelectMany(day => day.Stops)));
        await using var verify = database.Open();
        Assert.Single(await verify.AssistantUsageLeases.Where(lease => lease.CompletedAtUtc != null).ToListAsync());
        var events = await verify.ProductAnalyticsEvents.Where(item => item.Name == "first_useful_response").ToListAsync();
        Assert.True(events.Count == 1, $"Expected one signal, got {events.Count}. Diagnostics: {string.Join("; ", warnings)}");
        Assert.Empty(await verify.Reservations.ToListAsync());
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Generation_telemetry_preserves_a_connection_opened_by_the_caller()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var db = Open(database);
        var world = await DayPlanTestWorld.SeedAsync(db, trial: true);
        world.User.BehaviorAnalyticsConsent = true;
        await db.SaveChangesAsync();
        await db.Database.OpenConnectionAsync();
        try
        {
            var response = await DayPlanTestWorld.Service(db, analytics: true).GenerateAsync(world.Access(), world.Request(3), default);

            Assert.NotEmpty(response.Days.SelectMany(day => day.Stops));
            Assert.Equal(ConnectionState.Open, db.Database.GetDbConnection().State);
            Assert.Equal(1, await db.ProductAnalyticsEvents.CountAsync(item => item.Name == "first_useful_response"));
            Assert.Equal(1, await db.AssistantUsageLeases.CountAsync(item => item.CompletedAtUtc != null));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Concurrent_distinct_generations_cannot_exceed_three_trial_uses()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed, trial: true);
        async Task<bool> Generate()
        {
            await using var db = Open(database);
            try
            {
                await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request(3), default);
                return true;
            }
            catch (DayPlanException error) when (error.StatusCode == 403)
            {
                return false;
            }
        }
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Generate()));
        Assert.Equal(3, responses.Count(success => success));
        await using var verify = database.Open();
        Assert.Equal(3, await verify.AssistantUsageLeases.CountAsync(lease => lease.CompletedAtUtc != null));
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Concurrent_apply_retries_commit_one_batch_and_deleted_items_do_not_resurrect()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed);
        var proposal = await DayPlanTestWorld.Service(seed).GenerateAsync(world.Access(), world.Request(3), default);
        var request = Selection(world, proposal);
        async Task<DayPlanApplyResponse> Apply()
        {
            await using var db = Open(database);
            return await DayPlanTestWorld.Service(db).ApplyAsync(world.Access(), request, default);
        }
        var responses = await Task.WhenAll(Apply(), Apply());
        Assert.All(responses, response => Assert.True(response.Applied));
        Assert.Equal(JsonSerializer.Serialize(responses[0]), JsonSerializer.Serialize(responses[1]));
        await using (var verify = database.Open())
        {
            Assert.Equal(3, await verify.Reservations.CountAsync());
            Assert.Equal(1, (await verify.Trips.SingleAsync()).PlanRevision);
            verify.Reservations.Remove(await verify.Reservations.FirstAsync());
            await verify.SaveChangesAsync();
        }
        var replay = await Apply();
        Assert.Equal(JsonSerializer.Serialize(responses[0]), JsonSerializer.Serialize(replay));
        await using var afterReplay = database.Open();
        Assert.Equal(2, await afterReplay.Reservations.CountAsync());
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Concurrent_different_mutations_allow_only_one_expected_revision()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed);
        var proposal = await DayPlanTestWorld.Service(seed).GenerateAsync(world.Access(), world.Request(3), default);
        var request = Selection(world, proposal);
        async Task<bool> Apply()
        {
            await using var db = Open(database);
            try
            {
                return (await DayPlanTestWorld.Service(db).ApplyAsync(world.Access(), request with { MutationId = Guid.NewGuid() }, default)).Applied;
            }
            catch (DayPlanException error) when (error.StatusCode == 409)
            {
                return false;
            }
        }
        var responses = await Task.WhenAll(Apply(), Apply());
        Assert.Single(responses, applied => applied);
        await using var verify = database.Open();
        Assert.Equal(3, await verify.Reservations.CountAsync());
        Assert.Equal(1, (await verify.Trips.SingleAsync()).PlanRevision);
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Failed_insert_rolls_back_every_item_revision_and_receipt()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        DayPlanTestWorld world;
        DayPlanResponse proposal;
        await using (var seed = database.Open())
        {
            world = await DayPlanTestWorld.SeedAsync(seed);
            proposal = await DayPlanTestWorld.Service(seed).GenerateAsync(world.Access(), world.Request(3), default);
        }
        var rejectedRecommendation = proposal.Days[1].Stops[0].RecommendationId;
        await using (var triggerDb = database.Open())
        {
            // The injected value is a server-generated Guid, never untrusted SQL.
            var triggerSql = $"""
                CREATE FUNCTION reject_planner_test_insert() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."RecommendationId" = '{rejectedRecommendation}'::uuid THEN
                        RAISE EXCEPTION 'Synthetic batch failure';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER reject_planner_test_insert BEFORE INSERT ON "Reservations"
                FOR EACH ROW EXECUTE FUNCTION reject_planner_test_insert();
                """;
            await triggerDb.Database.ExecuteSqlRawAsync(triggerSql);
        }
        var request = Selection(world, proposal);
        await using (var failing = Open(database))
            await Assert.ThrowsAnyAsync<Exception>(() => DayPlanTestWorld.Service(failing).ApplyAsync(world.Access(), request, default));

        await using (var verify = database.Open())
        {
            Assert.Empty(await verify.Reservations.ToListAsync());
            Assert.Equal(0, (await verify.Trips.SingleAsync()).PlanRevision);
            await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_planner_test_insert ON \"Reservations\"; DROP FUNCTION reject_planner_test_insert();");
        }
        await using var retry = Open(database);
        var saved = await DayPlanTestWorld.Service(retry).ApplyAsync(world.Access(), request, default);
        Assert.True(saved.Applied);
        Assert.Equal(3, saved.Items.Count);
        Assert.Equal(3, await retry.Reservations.CountAsync());
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Unknown_selected_stop_rejects_whole_batch_and_different_payload_cannot_reuse_receipt()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var db = Open(database);
        var world = await DayPlanTestWorld.SeedAsync(db);
        var proposal = await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request(3), default);
        var request = Selection(world, proposal);
        var service = DayPlanTestWorld.Service(db);
        var error = await Assert.ThrowsAsync<DayPlanException>(() => service.ApplyAsync(world.Access(),
            request with { SelectedStopIds = [request.SelectedStopIds[0], Guid.NewGuid()] }, default));
        Assert.Equal(400, error.StatusCode);
        Assert.Empty(await db.Reservations.ToListAsync());
        Assert.Equal(0, (await db.Trips.SingleAsync()).PlanRevision);
        await service.ApplyAsync(world.Access(), request, default);
        var conflict = await Assert.ThrowsAsync<DayPlanException>(() => service.ApplyAsync(world.Access(),
            request with { SelectedStopIds = [request.SelectedStopIds[0]] }, default));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(3, await db.Reservations.CountAsync());
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Migration_indexes_and_rollback_preserve_confirmed_itinerary_content()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var db = Open(database);
        var world = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(3), default);
        await service.ApplyAsync(world.Access(), Selection(world, proposal), default);
        var indexes = await db.Database.SqlQueryRaw<string>("""
            SELECT indexname AS "Value" FROM pg_indexes
            WHERE schemaname = current_schema() AND tablename = 'PlanningApplicationReceipts'
            """).ToListAsync();
        Assert.Contains("IX_PlanningApplicationReceipts_AppUserId_TripId", indexes);
        Assert.Contains("IX_PlanningApplicationReceipts_TripId_MutationId", indexes);
        var jsonType = await db.Database.SqlQueryRaw<string>("""
            SELECT data_type AS "Value" FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'AssistantUsageLeases' AND column_name = 'ResponseJson'
            """).SingleAsync();
        Assert.Equal("jsonb", jsonType);
        var previous = db.Database.GetMigrations().TakeWhile(name => !name.EndsWith("AddDayPlanReplayReceipts", StringComparison.Ordinal)).Last();
        await db.GetService<IMigrator>().MigrateAsync(previous);
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.Reservations.CountAsync());
        Assert.Equal(1, (await db.Trips.SingleAsync()).PlanRevision);
        await db.Database.MigrateAsync();
        Assert.Equal(3, await db.Reservations.CountAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Analytics_failure_does_not_undo_a_committed_proposal_or_applied_batch()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var db = Open(database);
        var world = await DayPlanTestWorld.SeedAsync(db);
        world.User.BehaviorAnalyticsConsent = true;
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_planner_analytics() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic analytics failure'; END $$;
            CREATE TRIGGER reject_planner_analytics BEFORE INSERT ON "ProductAnalyticsEvents"
            FOR EACH ROW EXECUTE FUNCTION reject_planner_analytics();
            """);
        var service = DayPlanTestWorld.Service(db, analytics: true);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(3), default);
        var applied = await service.ApplyAsync(world.Access(), Selection(world, proposal), default);
        Assert.True(applied.Applied);
        Assert.Equal(3, await db.Reservations.CountAsync());
        Assert.Equal(1, (await db.Trips.SingleAsync()).PlanRevision);
        Assert.Single(await db.AssistantUsageLeases.Where(lease => lease.CompletedAtUtc != null).ToListAsync());
        Assert.Single(await db.PlanningApplicationReceipts.ToListAsync());
        Assert.Empty(await db.ProductAnalyticsEvents.ToListAsync());
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Concurrent_paid_generations_create_one_daily_usage_row_with_exact_count()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed);
        async Task<DayPlanResponse> Generate()
        {
            await using var db = Open(database);
            return await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request(), default);
        }
        var results = await Task.WhenAll(Generate(), Generate());
        Assert.All(results, result => Assert.NotEmpty(result.Days.SelectMany(day => day.Stops)));
        await using var verify = database.Open();
        Assert.Equal(2, (await verify.AssistantDailyUsages.SingleAsync()).SuccessfulRequests);
        Assert.Equal(2, await verify.AssistantUsageLeases.CountAsync(item => item.CompletedAtUtc != null));
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Concurrent_receipt_retries_when_every_selected_place_already_exists_write_no_extra_items()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        DayPlanTestWorld world;
        DayPlanResponse proposal;
        await using (var seed = database.Open())
        {
            world = await DayPlanTestWorld.SeedAsync(seed);
            proposal = await DayPlanTestWorld.Service(seed).GenerateAsync(world.Access(), world.Request(3), default);
            foreach (var day in proposal.Days)
            {
                var recommendation = await seed.Recommendations.SingleAsync(item => item.Id == day.Stops[0].RecommendationId);
                seed.Reservations.Add(new Reservation
                {
                    Id = Guid.NewGuid(), TripId = world.Trip.Id, RecommendationId = recommendation.Id,
                    Date = day.Date, StartsAt = new(9, 0), Title = recommendation.Title, City = "Tokyo",
                    LocationName = recommendation.Title, Address = "", ConfirmationCode = "", Notes = "",
                    Owner = ItineraryItemOwner.Traveler, TimePrecision = ItineraryTimePrecision.PeriodOnly
                });
            }
            world.Trip.PlanRevision = 1;
            await seed.SaveChangesAsync();
        }
        var request = Selection(world, proposal) with { ExpectedRevision = 1 };
        async Task<DayPlanApplyResponse> Apply()
        {
            await using var db = Open(database);
            return await DayPlanTestWorld.Service(db).ApplyAsync(world.Access(), request, default);
        }
        var results = await Task.WhenAll(Apply(), Apply());
        Assert.All(results, result => Assert.True(result.Applied));
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(results[1]));
        await using var verify = database.Open();
        Assert.Equal(3, await verify.Reservations.CountAsync());
        Assert.Equal(1, (await verify.Trips.SingleAsync()).PlanRevision);
        Assert.Single(await verify.PlanningApplicationReceipts.ToListAsync());
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Generation_completion_rechecks_trip_and_account_access_before_persisting_or_replaying()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        foreach (var completed in new[] { false, true })
        foreach (var change in new[] { "unpublished", "deleted-account", "revoked-grant" })
        {
            await using var db = database.Open();
            var world = await DayPlanTestWorld.SeedAsync(db, trial: true, perCity: 0);
            var usage = new AssistantUsageService(db,
                Microsoft.Extensions.Options.Options.Create(new TravelCompanion.Api.Options.FreePreviewOptions()),
                Microsoft.Extensions.Options.Options.Create(new TravelCompanion.Api.Options.StorePurchaseOptions()));
            var lease = await usage.ReserveAsync(world.User.Id, world.Trip.Id,
                AssistantUsageService.PlannerPrefix + Guid.NewGuid().ToString("N"), default);
            var json = JsonSerializer.Serialize(new DayPlanResponse(Guid.NewGuid(), world.Trip.Id, 0, [], "Synthetic result"));
            if (completed) await usage.CompletePlanAsync(lease.LeaseId, world.Trip.Id, 0, json, default);

            // A different request changes access while generation is in flight.
            await using (var changed = database.Open())
            {
                if (change == "unpublished")
                    await changed.Trips.Where(item => item.Id == world.Trip.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(item => item.PublicationStatus, TripPublicationStatus.Draft));
                else if (change == "deleted-account")
                    await changed.AppUsers.Where(item => item.Id == world.User.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(item => item.DeletedAtUtc, DateTimeOffset.UtcNow));
                else
                    await changed.BuilderAccessGrants.Where(item => item.Id == world.Grant.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, BuilderAccessStatus.Revoked));
            }

            await Assert.ThrowsAsync<DayPlanException>(() =>
                usage.CompletePlanAsync(lease.LeaseId, world.Trip.Id, 0, json, default));
            await using var verify = database.Open();
            var persisted = await verify.AssistantUsageLeases.SingleAsync(item => item.Id == lease.LeaseId);
            Assert.Equal(completed, persisted.CompletedAtUtc.HasValue);
            if (completed)
                Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<DayPlanResponse>(persisted.ResponseJson!)));
            else
                Assert.Null(persisted.ResponseJson);
            Assert.Empty(await verify.Reservations.Where(item => item.TripId == world.Trip.Id).ToListAsync());
        }
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Completion_replay_keeps_canonical_result_after_itinerary_revision_changes()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var db = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(db, trial: true, perCity: 0);
        var usage = new AssistantUsageService(db,
            Microsoft.Extensions.Options.Options.Create(new TravelCompanion.Api.Options.FreePreviewOptions()),
            Microsoft.Extensions.Options.Options.Create(new TravelCompanion.Api.Options.StorePurchaseOptions()));
        var lease = await usage.ReserveAsync(world.User.Id, world.Trip.Id,
            AssistantUsageService.PlannerPrefix + Guid.NewGuid().ToString("N"), default);
        var json = JsonSerializer.Serialize(new DayPlanResponse(Guid.NewGuid(), world.Trip.Id, 0, [], "Canonical result"));
        await usage.CompletePlanAsync(lease.LeaseId, world.Trip.Id, 0, json, default);
        await using (var changed = database.Open())
            await changed.Trips.Where(item => item.Id == world.Trip.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.PlanRevision, 1));

        var replay = await usage.CompletePlanAsync(lease.LeaseId, world.Trip.Id, 0, "Different result", default);

        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<DayPlanResponse>(replay)));
        Assert.Single(await db.AssistantUsageLeases.Where(item => item.BuilderAccessGrantId == world.Grant.Id
            && item.CompletedAtUtc != null).ToListAsync());
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Options_load_city_context_without_materializing_reservation_content()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var db = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(db, existing: 3);
        world.Trip.BuilderSegmentsJson = null;
        var reservation = await db.Reservations.FirstAsync();
        reservation.Notes = new string('x', 2000);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        database.Counter.Reset();

        var options = await DayPlanTestWorld.Service(db).OptionsAsync(world.Access(), default);

        Assert.Equal(7, options.CityDays.Count);
        Assert.Equal(["Tokyo"], options.CityDays[0].Cities);
        Assert.Equal(["Japan"], options.CityDays[1].Cities);
        var cityQuery = Assert.Single(database.Counter.Commands, command => command.Contains("FROM \"Reservations\""));
        Assert.DoesNotContain("\"Notes\"", cityQuery);
        Assert.DoesNotContain("\"ConfirmationCode\"", cityQuery);
        Assert.Empty(db.ChangeTracker.Entries<Reservation>());
    }

    private static DayPlanApplyRequest Selection(DayPlanTestWorld world, DayPlanResponse proposal) =>
        new(proposal.OperationId, world.Trip.Id, 0, Guid.NewGuid(), proposal.Days.Select(day => day.Stops[0].Id).ToList());

    private static TravelCompanionDbContext Open(PerformanceDatabase database) => new(
        new DbContextOptionsBuilder<TravelCompanionDbContext>().UseNpgsql(database.ConnectionString,
            provider => provider.EnableRetryOnFailure()).Options);

    private sealed class CaptureWarnings(System.Collections.Concurrent.ConcurrentBag<string> messages) : ILogger<DayPlanService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (level >= LogLevel.Warning) messages.Add(formatter(state, exception)); }
    }
}

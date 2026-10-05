using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class PostgresDayPlanReplacementTests
{
    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Concurrent_replacement_retries_commit_one_card_and_replay_one_snapshot_without_extra_usage()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed);
        var generation = world.Request(3);
        var original = await DayPlanTestWorld.Service(seed).GenerateAsync(world.Access(), generation, default);
        var request = Request(original, original.Days[0].Stops[0]);
        async Task<DayPlanReplaceResponse> Replace()
        {
            await using var db = Open(database);
            return await DayPlanTestWorld.Service(db).ReplaceAsync(world.Access(), request, default);
        }

        var results = await Task.WhenAll(Replace(), Replace(), Replace());

        Assert.All(results, result => Assert.True(result.Replaced));
        Assert.All(results, result => Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(result)));
        await using var verify = database.Open();
        var lease = await verify.AssistantUsageLeases.SingleAsync();
        using var state = JsonDocument.Parse(lease.ResponseJson!);
        Assert.Equal(1, state.RootElement.GetProperty("_planningState").GetProperty("Replacements").GetArrayLength());
        Assert.Equal(1, (await verify.AssistantDailyUsages.SingleAsync()).SuccessfulRequests);
        Assert.Equal(0, (await verify.Trips.SingleAsync()).PlanRevision);
        Assert.Empty(await verify.Reservations.ToListAsync());
        var current = await DayPlanTestWorld.Service(verify).GenerateAsync(world.Access(), generation, default);
        Assert.Equal(JsonSerializer.Serialize(results[0].Proposal), JsonSerializer.Serialize(current));
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Concurrent_distinct_replacements_cannot_overwrite_an_advanced_proposal_revision()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed);
        var original = await DayPlanTestWorld.Service(seed).GenerateAsync(world.Access(), world.Request(3), default);
        async Task<bool> Replace(DayPlanStopDto target)
        {
            await using var db = Open(database);
            try { return (await DayPlanTestWorld.Service(db).ReplaceAsync(world.Access(), Request(original, target), default)).Replaced; }
            catch (DayPlanException error) when (error.StatusCode == 409 && error.Code == "stale") { return false; }
        }

        var results = await Task.WhenAll(Replace(original.Days[0].Stops[0]), Replace(original.Days[1].Stops[0]));

        Assert.Single(results, result => result);
        await using var verify = database.Open();
        var proposal = JsonSerializer.Deserialize<DayPlanResponse>((await verify.AssistantUsageLeases.SingleAsync()).ResponseJson!)!;
        Assert.Equal(1, proposal.ProposalRevision);
        var stops = proposal.Days.SelectMany(day => day.Stops).ToList();
        Assert.Equal(stops.Count, stops.Select(stop => stop.RecommendationId).Distinct().Count());
        Assert.Equal(1, (await verify.AssistantDailyUsages.SingleAsync()).SuccessfulRequests);
        Assert.Empty(await verify.Reservations.ToListAsync());
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Replacement_and_application_race_never_saves_a_different_card_under_an_old_stop_id()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed);
        var proposal = await DayPlanTestWorld.Service(seed).GenerateAsync(world.Access(), world.Request(), default);
        var target = proposal.Days[0].Stops[0];
        async Task<bool> Replace()
        {
            await using var db = Open(database);
            try { return (await DayPlanTestWorld.Service(db).ReplaceAsync(world.Access(), Request(proposal, target), default)).Replaced; }
            catch (DayPlanException error) when (error.Code == "stale") { return false; }
        }
        async Task<bool> Apply()
        {
            await using var db = Open(database);
            try { return (await DayPlanTestWorld.Service(db).ApplyAsync(world.Access(),
                new(proposal.OperationId, world.Trip.Id, 0, Guid.NewGuid(), [target.Id]), default)).Applied; }
            catch (DayPlanException error) when (error.Code == "selection") { return false; }
        }

        var results = await Task.WhenAll(Replace(), Apply());

        Assert.Single(results, result => result);
        await using var verify = database.Open();
        var reservations = await verify.Reservations.ToListAsync();
        if (results[1])
        {
            Assert.Equal(target.RecommendationId, Assert.Single(reservations).RecommendationId);
            Assert.Equal(target.Id, reservations[0].ClientMutationId);
        }
        else Assert.Empty(reservations);
        Assert.Equal(1, (await verify.AssistantDailyUsages.SingleAsync()).SuccessfulRequests);
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Failed_proposal_write_rolls_back_the_card_history_and_can_retry_the_same_mutation()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed);
        var proposal = await DayPlanTestWorld.Service(seed).GenerateAsync(world.Access(), world.Request(), default);
        var request = Request(proposal, proposal.Days[0].Stops[0]);
        var before = (await seed.AssistantUsageLeases.AsNoTracking().SingleAsync()).ResponseJson;
        await seed.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_replacement_test_update() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic proposal failure'; END $$;
            CREATE TRIGGER reject_replacement_test_update BEFORE UPDATE ON "AssistantUsageLeases"
            FOR EACH ROW EXECUTE FUNCTION reject_replacement_test_update();
            """);
        await using (var failing = Open(database))
            await Assert.ThrowsAnyAsync<Exception>(() => DayPlanTestWorld.Service(failing).ReplaceAsync(world.Access(), request, default));
        await using (var verify = database.Open())
        {
            Assert.Equal(before, (await verify.AssistantUsageLeases.SingleAsync()).ResponseJson);
            Assert.Empty(await verify.Reservations.ToListAsync());
            Assert.Equal(0, (await verify.Trips.SingleAsync()).PlanRevision);
            Assert.Equal(1, (await verify.AssistantDailyUsages.SingleAsync()).SuccessfulRequests);
            await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_replacement_test_update ON \"AssistantUsageLeases\"; DROP FUNCTION reject_replacement_test_update();");
        }
        await using var retry = Open(database);
        var result = await DayPlanTestWorld.Service(retry).ReplaceAsync(world.Access(), request, default);
        Assert.True(result.Replaced);
        Assert.Equal(1, result.Proposal.ProposalRevision);
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Revoking_access_during_catalog_io_prevents_persisting_the_replacement()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed);
        var proposal = await DayPlanTestWorld.Service(seed).GenerateAsync(world.Access(), world.Request(), default);
        var before = (await seed.AssistantUsageLeases.AsNoTracking().SingleAsync()).ResponseJson;
        var interceptor = new DuringCatalog(async () =>
        {
            await using var revoke = database.Open();
            await revoke.BuilderAccessGrants.Where(item => item.Id == world.Grant.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.RevokedAtUtc, DateTimeOffset.UtcNow));
        });
        await using var db = Open(database, interceptor);

        var error = await Assert.ThrowsAsync<DayPlanException>(() => DayPlanTestWorld.Service(db)
            .ReplaceAsync(world.Access(), Request(proposal, proposal.Days[0].Stops[0]), default));

        Assert.Equal("upgrade", error.Code);
        Assert.True(interceptor.Executed);
        await using var verify = database.Open();
        Assert.Equal(before, (await verify.AssistantUsageLeases.SingleAsync()).ResponseJson);
        Assert.Empty(await verify.Reservations.ToListAsync());
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Cancellation_during_catalog_io_preserves_the_original_proposal_and_usage()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed);
        var proposal = await DayPlanTestWorld.Service(seed).GenerateAsync(world.Access(), world.Request(), default);
        var before = (await seed.AssistantUsageLeases.AsNoTracking().SingleAsync()).ResponseJson;
        using var cancelled = new CancellationTokenSource();
        var interceptor = new DuringCatalog(() => { cancelled.Cancel(); return Task.CompletedTask; });
        await using var db = Open(database, interceptor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DayPlanTestWorld.Service(db)
            .ReplaceAsync(world.Access(), Request(proposal, proposal.Days[0].Stops[0]), cancelled.Token));

        await using var verify = database.Open();
        Assert.Equal(before, (await verify.AssistantUsageLeases.SingleAsync()).ResponseJson);
        Assert.Equal(1, (await verify.AssistantDailyUsages.SingleAsync()).SuccessfulRequests);
        Assert.Empty(await verify.Reservations.ToListAsync());
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task A_saved_then_deleted_stop_and_a_deleted_account_cannot_replace_or_replay_cards()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var seed = database.Open();
        var world = await DayPlanTestWorld.SeedAsync(seed);
        var service = DayPlanTestWorld.Service(seed);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(), default);
        var target = proposal.Days[0].Stops[0];
        var saved = await service.ApplyAsync(world.Access(), new(proposal.OperationId, world.Trip.Id, 0,
            Guid.NewGuid(), [target.Id]), default);
        await using (var remove = database.Open()) await remove.Reservations.ExecuteDeleteAsync();
        await using (var db = Open(database))
        {
            var error = await Assert.ThrowsAsync<DayPlanException>(() => DayPlanTestWorld.Service(db)
                .ReplaceAsync(world.Access(), Request(proposal, target, saved.Revision), default));
            Assert.Equal("selection", error.Code);
        }
        var remaining = proposal.Days[0].Stops[1];
        var request = Request(proposal, remaining, saved.Revision);
        await using (var db = Open(database)) Assert.True((await DayPlanTestWorld.Service(db)
            .ReplaceAsync(world.Access(), request, default)).Replaced);
        await using (var deleted = database.Open()) await deleted.AppUsers.Where(item => item.Id == world.User.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.DeletedAtUtc, DateTimeOffset.UtcNow));
        await using var replay = Open(database);
        await Assert.ThrowsAsync<DayPlanException>(() => DayPlanTestWorld.Service(replay).ReplaceAsync(world.Access(), request, default));
    }

    private static DayPlanReplaceRequest Request(DayPlanResponse proposal, DayPlanStopDto stop, int revision = 0) =>
        new(proposal.OperationId, proposal.TripId, revision, stop.Id, Guid.NewGuid(), "en")
        { ExpectedProposalRevision = proposal.ProposalRevision };
    private static TravelCompanionDbContext Open(PerformanceDatabase database, params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<TravelCompanionDbContext>().UseNpgsql(database.ConnectionString,
            options => options.EnableRetryOnFailure()).AddInterceptors(interceptors).Options);
    private sealed class DuringCatalog(Func<Task> action) : DbCommandInterceptor
    {
        private int executed;
        public bool Executed => executed != 0;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Recommendations\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref executed, 1) == 0) await action();
            return result;
        }
    }
}

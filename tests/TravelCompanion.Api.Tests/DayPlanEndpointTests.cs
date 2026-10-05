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
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class DayPlanEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    [Theory]
    [InlineData("options")]
    [InlineData("generate")]
    [InlineData("apply")]
    [InlineData("replace")]
    public async Task Every_planner_endpoint_requires_a_bearer_session(string endpoint)
    {
        await using var factory = new PlannerFactory();
        using var client = factory.CreateClient();
        var result = endpoint switch
        {
            "options" => await client.GetAsync("/api/ai/day-plans/options"),
            "generate" => await client.PostAsJsonAsync("/api/ai/day-plans",
                new DayPlanRequest(Guid.NewGuid(), 0, DayPlanTestWorld.Start, 1, Guid.NewGuid())),
            "apply" => await client.PostAsJsonAsync("/api/ai/day-plans/apply",
                new DayPlanApplyRequest(Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), [Guid.NewGuid()])),
            _ => await client.PostAsJsonAsync("/api/ai/day-plans/replace",
                new DayPlanReplaceRequest(Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid()))
        };
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task Free_session_replaces_a_card_and_replays_the_canonical_proposal_without_extra_quota_or_private_metadata()
    {
        await using var factory = new PlannerFactory();
        var (world, token) = await factory.SeedAsync(trial: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var generation = world.Request(3);
        var generated = await client.PostAsJsonAsync("/api/ai/day-plans", generation);
        generated.EnsureSuccessStatusCode();
        var proposal = (await generated.Content.ReadFromJsonAsync<DayPlanResponse>(Json))!;
        var request = new DayPlanReplaceRequest(proposal.OperationId, proposal.TripId, 0,
            proposal.Days[0].Stops[0].Id, Guid.NewGuid(), "en") { OriginalRequest = generation };

        var response = await client.PostAsJsonAsync("/api/ai/day-plans/replace", request);
        response.EnsureSuccessStatusCode();
        var replacement = (await response.Content.ReadFromJsonAsync<DayPlanReplaceResponse>(Json))!;
        Assert.True(replacement.Replaced);
        Assert.Equal(1, replacement.Proposal.ProposalRevision);
        Assert.Equal(2, replacement.Proposal.TrialAccess!.DayImprovementsRemaining);
        Assert.DoesNotContain("_planningState", await response.Content.ReadAsStringAsync());
        var replay = await client.PostAsJsonAsync("/api/ai/day-plans/replace", request);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(await response.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var canonical = await client.PostAsJsonAsync("/api/ai/day-plans", generation);
        canonical.EnsureSuccessStatusCode();
        var current = (await canonical.Content.ReadFromJsonAsync<DayPlanResponse>(Json))!;
        Assert.Equal(replacement.Proposal.Days[0].Stops[0].Id, current.Days[0].Stops[0].Id);
        Assert.Equal(1, current.ProposalRevision);
        Assert.DoesNotContain("_planningState", await canonical.Content.ReadAsStringAsync());
        var saved = await client.PostAsJsonAsync("/api/ai/day-plans/apply", new DayPlanApplyRequest(current.OperationId,
            current.TripId, 0, Guid.NewGuid(), [current.Days[0].Stops[0].Id]));
        saved.EnsureSuccessStatusCode();
        Assert.Equal(current.Days[0].Stops[0].RecommendationId,
            (await saved.Content.ReadFromJsonAsync<DayPlanApplyResponse>(Json))!.Items[0].RecommendationId);
    }

    [Fact]
    public async Task Free_session_can_view_options_generate_with_preferences_and_apply_three_days()
    {
        await using var factory = new PlannerFactory();
        var (world, token) = await factory.SeedAsync(trial: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var optionsResult = await client.GetAsync("/api/ai/day-plans/options");
        optionsResult.EnsureSuccessStatusCode();
        var options = await optionsResult.Content.ReadFromJsonAsync<DayPlanOptionsDto>(Json);
        Assert.True(options!.Enabled);
        Assert.Equal([1, 3], options.DayCounts);
        Assert.Equal(world.Trip.EndsOn, options.EndsOn);
        Assert.Equal(7, options.CityDays.Count);
        Assert.Equal(["Tokyo"], options.CityDays[0].Cities);
        Assert.Equal(["Kyoto"], options.CityDays[^1].Cities);
        var response = await client.PostAsJsonAsync("/api/ai/day-plans", world.Request(3, "efficient"));
        response.EnsureSuccessStatusCode();
        var proposal = await response.Content.ReadFromJsonAsync<DayPlanResponse>(Json);
        Assert.Equal(3, proposal!.Days.Count);
        Assert.All(proposal.Days.SelectMany(day => day.Stops), stop => Assert.Equal("Tokyo, Japan", stop.Place));
        Assert.Equal(2, proposal.TrialAccess!.DayImprovementsRemaining);
        var save = await client.PostAsJsonAsync("/api/ai/day-plans/apply",
            new DayPlanApplyRequest(proposal.OperationId, world.Trip.Id, 0, Guid.NewGuid(),
                proposal.Days.Select(day => day.Stops[0].Id).ToList()));
        save.EnsureSuccessStatusCode();
        var result = await save.Content.ReadFromJsonAsync<DayPlanApplyResponse>(Json);
        Assert.True(result!.Applied);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task Paid_session_accepts_seven_days_and_returns_structured_stale_errors()
    {
        await using var factory = new PlannerFactory();
        var (world, token) = await factory.SeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/ai/day-plans", world.Request(7));
        response.EnsureSuccessStatusCode();
        var proposal = await response.Content.ReadFromJsonAsync<DayPlanResponse>(Json);
        Assert.Equal(7, proposal!.Days.Count);
        var stale = await client.PostAsJsonAsync("/api/ai/day-plans", world.Request() with { ExpectedRevision = 99 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("stale", (await stale.Content.ReadFromJsonAsync<DayPlanErrorDto>())!.Code);
    }

    [Fact]
    public async Task Trial_paid_range_is_a_structured_upgrade_and_unrelated_routes_remain_blocked()
    {
        await using var factory = new PlannerFactory();
        var (world, token) = await factory.SeedAsync(trial: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/ai/day-plans", world.Request(5));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("upgrade", (await response.Content.ReadFromJsonAsync<DayPlanErrorDto>())!.Code);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai/day-plans-extra/options")).StatusCode);
    }

    private sealed class PlannerFactory : WebApplicationFactory<Program>
    {
        private readonly string name = $"planner-endpoints-{Guid.NewGuid():N}";
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TravelCompanionDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<TravelCompanionDbContext>>();
                services.AddDbContext<TravelCompanionDbContext>(options => options.UseInMemoryDatabase(name));
            });
        }
        internal async Task<(DayPlanTestWorld World, string Token)> SeedAsync(bool trial = false)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var world = await DayPlanTestWorld.SeedAsync(db, trial);
            var (_, token) = await scope.ServiceProvider.GetRequiredService<UserSessionService>().CreateSessionAsync(world.User,
                tripId: world.Trip.Id, accessMode: trial ? SessionAccessMode.FreeMapPreview : SessionAccessMode.Builder);
            return (world, token);
        }
    }
}

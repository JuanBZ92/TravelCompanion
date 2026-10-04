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
    public async Task Every_planner_endpoint_requires_a_bearer_session(string endpoint)
    {
        await using var factory = new PlannerFactory();
        using var client = factory.CreateClient();
        var result = endpoint switch
        {
            "options" => await client.GetAsync("/api/ai/day-plans/options"),
            "generate" => await client.PostAsJsonAsync("/api/ai/day-plans",
                new DayPlanRequest(Guid.NewGuid(), 0, DayPlanTestWorld.Start, 1, Guid.NewGuid())),
            _ => await client.PostAsJsonAsync("/api/ai/day-plans/apply",
                new DayPlanApplyRequest(Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), [Guid.NewGuid()]))
        };
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
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
        var response = await client.PostAsJsonAsync("/api/ai/day-plans", world.Request(3, "efficient"));
        response.EnsureSuccessStatusCode();
        var proposal = await response.Content.ReadFromJsonAsync<DayPlanResponse>(Json);
        Assert.Equal(3, proposal!.Days.Count);
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

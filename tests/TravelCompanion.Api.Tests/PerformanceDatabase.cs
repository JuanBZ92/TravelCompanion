using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using TravelCompanion.Api.Data;

namespace TravelCompanion.Api.Tests;

// Also linked by the standalone benchmark. It never uses the application's connection string.
public sealed class PerformanceDatabase : IAsyncDisposable
{
    private readonly string schema = "tc_perf_" + Guid.NewGuid().ToString("N");
    private readonly NpgsqlConnection admin;
    public string ConnectionString { get; }
    public CommandCounter Counter { get; } = new();
    public PerformanceDatabase()
    {
        var value = Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES")
            ?? throw new InvalidOperationException("TRAVELCOMPANION_TEST_POSTGRES is required; PostgreSQL checks cannot be skipped.");
        var builder = new NpgsqlConnectionStringBuilder(value);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("Performance data is restricted to local PostgreSQL.");
        admin = new(value);
        builder.SearchPath = schema;
        builder.Options = "-c timezone=UTC";
        ConnectionString = builder.ConnectionString;
    }
    public TravelCompanionDbContext Open() => new(new DbContextOptionsBuilder<TravelCompanionDbContext>()
        .UseNpgsql(ConnectionString).AddInterceptors(Counter).Options);
    public async Task InitializeAsync()
    {
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin);
        await command.ExecuteNonQueryAsync();
        await using var db = Open();
        await db.Database.MigrateAsync();
        Counter.Reset();
    }
    public async Task SeedEventsAsync(int count)
    {
        await using var db = Open();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ProductAnalyticsEvents"
                ("Id", "EventId", "Name", "OccurredAtUtc", "ReceivedAtUtc", "BehaviorConsent", "IsBusinessEvent", "SchemaVersion", "IsSandbox", "Source")
            SELECT md5('event-' || i)::uuid, md5('event-id-' || i)::uuid, 'paywall_shown',
                TIMESTAMPTZ '2025-01-01 00:00:00Z' + (i % 100) * interval '1 day',
                TIMESTAMPTZ '2025-01-01 00:00:00Z', true, false, 1, false,
                CASE WHEN i % 2 = 0 THEN NULL ELSE '' END
            FROM generate_series(1, {count}) AS i
            """);
    }
    public async ValueTask DisposeAsync()
    {
        if (admin.State == System.Data.ConnectionState.Open)
        {
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            await command.ExecuteNonQueryAsync();
        }
        await admin.DisposeAsync();
    }
}

public sealed class CommandCounter : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];
    public List<DbParameter[]> Parameters { get; } = [];
    public void Reset() { Commands.Clear(); Parameters.Clear(); }
    private void Record(DbCommand command)
    {
        lock (Commands)
        {
            Commands.Add(command.CommandText);
            Parameters.Add(command.Parameters.Cast<NpgsqlParameter>().Select(p => (DbParameter)p.Clone()).ToArray());
        }
    }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Record(command); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Record(command); return ValueTask.FromResult(result); }
}

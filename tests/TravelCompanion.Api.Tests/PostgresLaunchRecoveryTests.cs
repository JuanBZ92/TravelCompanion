using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class PostgresLaunchRecoveryTests
{
    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Backup_restores_account_session_and_encrypted_pending_purchase_after_certificate_rotation()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES"));
        var schema = $"tc_recovery_{Guid.NewGuid():N}";
        var container = Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES_CONTAINER");
        var archive = string.IsNullOrEmpty(container) ? Path.Combine(Path.GetTempPath(), schema + ".dump") : $"/tmp/{schema}.dump";
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, admin);
            await command.ExecuteNonQueryAsync();
        }
        await Execute($"CREATE SCHEMA \"{schema}\"");
        connection.SearchPath = schema;
        using var firstCertificate = Certificate("first");
        using var rotatedCertificate = Certificate("rotated");
        ServiceProvider Create(X509Certificate2 certificate, bool includePrevious = false)
        {
            var values = new Dictionary<string, string?>
            {
                ["DataProtection:UseDatabase"] = "true",
                ["DataProtection:CertificateBase64"] = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx))
            };
            if (includePrevious) values["DataProtection:PreviousCertificates:0"] = Convert.ToBase64String(firstCertificate.Export(X509ContentType.Pfx));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<TravelCompanionDbContext>(o => o.UseNpgsql(connection.ConnectionString));
            services.AddLaunchInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            return services.BuildServiceProvider();
        }
        try
        {
            string token;
            Guid ownerId;
            await using (var first = Create(firstCertificate))
            {
                await using var scope = first.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
                await db.Database.MigrateAsync();
                var destination = new Destination { Id = Guid.NewGuid(), Name = "Japan", Slug = schema, Country = "Japan", HeroImageUrl = "", ShortDescription = "" };
                var owner = new AppUser { Id = Guid.NewGuid(), Email = "recovery@example.test", DisplayName = "Recovery" };
                ownerId = owner.Id;
                var trip = new Trip { Id = Guid.NewGuid(), AppUserId = owner.Id, DestinationId = destination.Id, TravelerName = "Recovery", StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 2) };
                db.AddRange(destination, owner, trip);
                db.StorePurchaseIntents.Add(new StorePurchaseIntent
                {
                    Id = Guid.NewGuid(), AppUserId = owner.Id, TripId = trip.Id,
                    ProductId = "test-pass", OpaqueAccountId = "test-account", PaywallVariant = "test",
                    ProtectedEvidence = first.GetRequiredService<IDataProtectionProvider>()
                        .CreateProtector("TravelCompanion.StorePurchaseEvidence.v1").Protect("pending-test-receipt")
                });
                await db.SaveChangesAsync();
                (_, token) = await new UserSessionService(db).CreateSessionAsync(owner, tripId: trip.Id);
                Assert.All(await db.DataProtectionKeys.ToListAsync(), key => Assert.Contains("encryptedSecret", key.Xml));
            }

            // Only the randomly generated test schema is exported, dropped and restored.
            await RunTool("pg_dump", ["--format=custom", "--no-owner", "--schema=" + schema, "--file=" + archive]);
            await Execute($"DROP SCHEMA \"{schema}\" CASCADE");
            await RunTool("pg_restore", ["--exit-on-error", "--no-owner", "--dbname=" + connection.Database, archive]);

            await using var restored = Create(rotatedCertificate, includePrevious: true);
            await using var restoredScope = restored.CreateAsyncScope();
            var restoredDb = restoredScope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var intent = await restoredDb.StorePurchaseIntents.SingleAsync();
            Assert.Equal(ownerId, intent.AppUserId);
            Assert.Equal("pending-test-receipt", restored.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("TravelCompanion.StorePurchaseEvidence.v1").Unprotect(intent.ProtectedEvidence!));
            var http = new DefaultHttpContext();
            http.Request.Headers.Authorization = "Bearer " + token;
            Assert.Equal(ownerId, (await new UserSessionService(restoredDb).GetUserAsync(http))?.Id);
            Assert.Empty(await restoredDb.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            await Execute($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
            if (string.IsNullOrEmpty(container)) File.Delete(archive);
            else await RunTool("rm", ["--", archive]);
        }

        async Task RunTool(string tool, string[] arguments)
        {
            var start = new ProcessStartInfo(string.IsNullOrEmpty(container) ? tool : "docker")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var (key, value) in new Dictionary<string, string?>
            {
                ["PGHOST"] = connection.Host, ["PGPORT"] = connection.Port.ToString(),
                ["PGUSER"] = connection.Username, ["PGPASSWORD"] = connection.Password, ["PGDATABASE"] = connection.Database
            }) start.Environment[key] = value ?? "";
            if (!string.IsNullOrEmpty(container))
            {
                start.ArgumentList.Add("exec");
                foreach (var key in new[] { "PGHOST", "PGPORT", "PGUSER", "PGPASSWORD", "PGDATABASE" })
                { start.ArgumentList.Add("--env"); start.ArgumentList.Add(key); }
                start.ArgumentList.Add(container);
                start.ArgumentList.Add(tool);
            }
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            await output;
            Assert.True(process.ExitCode == 0, $"{tool} failed: {await error}");
        }
    }

    private static X509Certificate2 Certificate(string name)
    {
        using var rsa = RSA.Create(2048);
        return new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(5));
    }
}

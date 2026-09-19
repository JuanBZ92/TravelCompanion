using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;

var refreshPremiumDemo = args.Length == 1 && args[0] == "--refresh-premium-demo";
if (args.Length == 0 || (args[0].StartsWith("--") && !refreshPremiumDemo))
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  CatalogAdmin --refresh-premium-demo");
    Console.Error.WriteLine("  CatalogAdmin <workbook.xlsx> [--reset --backup <new-archive.dump> --confirm-delete-test-data]");
    return 1;
}
var connection = Environment.GetEnvironmentVariable("CATALOG_DATABASE_URL")
    ?? throw new InvalidOperationException("Set CATALOG_DATABASE_URL for the intended database; no default database is used.");
var settings = ConnectionSettings(connection);
await using var db = new TravelCompanionDbContext(new DbContextOptionsBuilder<TravelCompanionDbContext>().UseNpgsql(settings.ConnectionString).Options);
await db.Database.MigrateAsync();
await DatabaseSeeder.SeedAsync(db, new PasswordHasher<AppUser>());
if (refreshPremiumDemo)
{
    var refreshed = await new PremiumDemoTripRefreshService(db).RefreshAsync();
    Console.WriteLine($"PIN 2222 trip {(refreshed.Created ? "created" : "updated")}: {refreshed.TripId}");
    Console.WriteLine($"{refreshed.StartsOn:yyyy-MM-dd} to {refreshed.EndsOn:yyyy-MM-dd}; {refreshed.DayCount} days, {refreshed.CityCount} cities, {refreshed.BlockCount} periods, {refreshed.ReservationCount} items.");
    return 0;
}

var bytes = await File.ReadAllBytesAsync(Path.GetFullPath(args[0]));
var importer = new YukuJapanRecommendationImportService(db, NullLogger<YukuJapanRecommendationImportService>.Instance);
using var previewStream = new MemoryStream(bytes, writable: false);
var preview = await importer.PreviewAsync(previewStream);
Console.WriteLine($"Database: {settings.Host}/{settings.Database}. {preview.StatusMessage}");
foreach (var error in preview.Errors) Console.Error.WriteLine(error);
foreach (var row in preview.Rows.Where(r => r.Warnings.Count > 0)) Console.WriteLine($"Row {row.RowNumber}: {string.Join("; ", row.Warnings)}");
if (!preview.CanImport) return 2;
if (!args.Contains("--reset")) return 0;
if (!args.Contains("--confirm-delete-test-data")) throw new InvalidOperationException("Explicit --confirm-delete-test-data is required.");
var backupIndex = Array.IndexOf(args, "--backup");
if (backupIndex < 0 || backupIndex + 1 >= args.Length) throw new InvalidOperationException("--backup path is required.");
var archive = Path.GetFullPath(args[backupIndex + 1]);
if (File.Exists(archive)) throw new InvalidOperationException("Backup must be a new file.");
Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
await RunPostgresTool("pg_dump", ["--format=custom", "--file", archive, "--no-password"]);
if (new FileInfo(archive).Length == 0) throw new InvalidOperationException("Empty backup; reset refused.");
await RunPostgresTool("pg_restore", ["--list", archive]);
Console.WriteLine($"Backup validated: {archive}");
var result = await new YukuCatalogResetService(db, importer).ResetAsync(bytes);
Console.WriteLine(result.StatusMessage);
return result.Imported ? 0 : 3;

async Task RunPostgresTool(string tool, string[] arguments)
{
    var processInfo = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) processInfo.ArgumentList.Add(argument);
    processInfo.Environment["PGHOST"] = settings.Host;
    processInfo.Environment["PGPORT"] = settings.Port.ToString();
    processInfo.Environment["PGDATABASE"] = settings.Database;
    processInfo.Environment["PGUSER"] = settings.Username;
    processInfo.Environment["PGPASSWORD"] = settings.Password;
    processInfo.Environment["PGSSLMODE"] = settings.SslMode == SslMode.Disable ? "disable" : "require";
    using var process = Process.Start(processInfo) ?? throw new InvalidOperationException($"Could not start {tool}.");
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    await output;
    // PostgreSQL errors can contain connection details; don't echo them into shared logs.
    await error;
    if (process.ExitCode != 0) throw new InvalidOperationException($"{tool} failed ({process.ExitCode}); reset refused.");
}

static NpgsqlConnectionStringBuilder ConnectionSettings(string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("postgres" or "postgresql")) return new(value);
    var credentials = uri.UserInfo.Split(':', 2);
    return new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host, Port = uri.Port > 0 ? uri.Port : 5432,
        Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        Username = Uri.UnescapeDataString(credentials[0]), Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty,
        SslMode = SslMode.Require
    };
}

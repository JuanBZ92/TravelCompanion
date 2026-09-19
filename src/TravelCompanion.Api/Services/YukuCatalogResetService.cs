using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

// Only invoked by the explicit maintenance CLI after validating a PostgreSQL backup.
public sealed class YukuCatalogResetService(TravelCompanionDbContext db, YukuJapanRecommendationImportService importer)
{
    public async Task<YukuJapanRecommendationImportResult> ResetAsync(byte[] workbook, CancellationToken cancellationToken = default)
    {
        using var previewStream = new MemoryStream(workbook, writable: false);
        var preview = await importer.PreviewAsync(previewStream, cancellationToken);
        if (!preview.CanImport) return preview;
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Catalog reset requires PostgreSQL.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var japan = await db.Destinations.SingleAsync(d => d.Slug == "japon", cancellationToken);
        var preservedEmails = new[] { FreePreviewAccountService.AccountEmail, "builder-demo@travelcompanion.local", "premium-demo@travelcompanion.local" };
        var users = await db.AppUsers.Where(u => preservedEmails.Contains(u.Email)).ToListAsync(cancellationToken);

        // Keep only technical configuration. No CASCADE: an unexpected foreign key must fail the reset.
        var preservedTables = new HashSet<string>(StringComparer.Ordinal) { "Destinations", "FreeMapCities", "AppUsers" };
        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>().Distinct()
            .Where(t => !preservedTables.Contains(t)).OrderBy(t => t).ToArray();
        var identifiers = string.Join(", ", tables.Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));
        var truncateSql = "TRUNCATE TABLE " + identifiers;
        await db.Database.ExecuteSqlRawAsync(truncateSql, cancellationToken);
        await db.AppUsers.Where(u => !preservedEmails.Contains(u.Email)).ExecuteDeleteAsync(cancellationToken);
        using var importStream = new MemoryStream(workbook, writable: false);
        var result = await importer.ImportAsync(importStream, cancellationToken);
        if (!result.Imported) throw new InvalidOperationException("Catalog import failed; reset rolled back.");

        AppUser Account(string email, string name)
        {
            var user = users.SingleOrDefault(u => u.Email == email);
            if (user is null)
            {
                user = new AppUser { Id = Guid.NewGuid(), Email = email, DisplayName = name, PasswordHash = string.Empty, MustChangePassword = false };
                db.AppUsers.Add(user);
            }
            user.DisplayName = name;
            user.PasswordHash = string.Empty;
            user.MustChangePassword = false;
            return user;
        }
        Account(FreePreviewAccountService.AccountEmail, "YUKU Free");
        var paid = Account("builder-demo@travelcompanion.local", "YUKU Pago");
        var premium = Account("premium-demo@travelcompanion.local", "YUKU Premium");
        var grant = new BuilderAccessGrant { Id = Guid.NewGuid(), AppUserId = paid.Id, DestinationId = japan.Id, PinHash = string.Empty, OrderReference = "catalog-v2-demo" };
        grant.PinHash = new PasswordHasher<BuilderAccessGrant>().HashPassword(grant, "1111");
        db.BuilderAccessGrants.Add(grant);
        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Tokyo").DateTime).AddDays(7);
        var citySlugs = PremiumDemoTripFactory.CitySlugs;
        var recommendations = await db.Recommendations
            .Where(recommendation => recommendation.DestinationId == japan.Id
                && recommendation.CitySlug != null
                && citySlugs.Contains(recommendation.CitySlug))
            .OrderBy(recommendation => recommendation.CitySlug)
            .ThenBy(recommendation => recommendation.Title)
            .ToListAsync(cancellationToken);
        var trip = PremiumDemoTripFactory.Create(premium.Id, premium.DisplayName, japan.Id, firstDate, recommendations);
        trip.AccessPinHash = new PasswordHasher<Trip>().HashPassword(trip, "2222");
        trip.AccessPinUpdatedAt = DateTimeOffset.UtcNow;
        db.Trips.Add(trip);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}

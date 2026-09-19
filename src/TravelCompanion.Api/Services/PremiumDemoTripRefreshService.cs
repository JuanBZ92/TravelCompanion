using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class PremiumDemoTripRefreshService(TravelCompanionDbContext db)
{
    private const string PremiumEmail = "premium-demo@travelcompanion.local";
    private const string PremiumPin = "2222";

    public async Task<PremiumDemoTripRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational())
        {
            transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        }

        await using (transaction)
        {
            var destination = await db.Destinations.SingleAsync(item => item.Slug == "japon", cancellationToken);
            var pinHasher = new PasswordHasher<Trip>();
            var matchingTripIds = (await db.Trips.AsNoTracking()
                    .Where(item => item.AccessPinHash != null)
                    .ToListAsync(cancellationToken))
                .Where(item => pinHasher.VerifyHashedPassword(item, item.AccessPinHash!, PremiumPin)
                    != PasswordVerificationResult.Failed)
                .Select(item => item.Id)
                .ToList();
            if (matchingTripIds.Count > 1)
            {
                throw new InvalidOperationException("More than one trip uses PIN 2222; no data was changed.");
            }

            var target = matchingTripIds.Count == 1
                ? await LoadTripAsync(matchingTripIds[0], cancellationToken)
                : null;
            var premiumUser = target?.AppUserId is Guid targetUserId
                ? await db.AppUsers.SingleAsync(item => item.Id == targetUserId, cancellationToken)
                : await db.AppUsers.SingleOrDefaultAsync(item => item.Email == PremiumEmail, cancellationToken);
            if (premiumUser is null)
            {
                premiumUser = new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = PremiumEmail,
                    DisplayName = "YUKU Premium",
                    PasswordHash = string.Empty,
                    MustChangePassword = false
                };
                db.AppUsers.Add(premiumUser);
            }

            if (target is null)
            {
                var premiumTrips = await db.Trips
                    .Where(item => item.AppUserId == premiumUser.Id && item.ExperienceMode == ExperienceMode.CuratedPremium)
                    .Select(item => item.Id)
                    .ToListAsync(cancellationToken);
                if (premiumTrips.Count > 1)
                {
                    throw new InvalidOperationException("The premium demo account has multiple trips; no data was changed.");
                }
                if (premiumTrips.Count == 1)
                {
                    target = await LoadTripAsync(premiumTrips[0], cancellationToken);
                }
            }

            var recommendations = await db.Recommendations
                .Where(item => item.DestinationId == destination.Id
                    && item.CitySlug != null
                    && PremiumDemoTripFactory.CitySlugs.Contains(item.CitySlug))
                .OrderBy(item => item.CitySlug)
                .ThenBy(item => item.Title)
                .ToListAsync(cancellationToken);
            var firstDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Tokyo").DateTime).AddDays(7);
            var generated = PremiumDemoTripFactory.Create(
                premiumUser.Id,
                premiumUser.DisplayName,
                destination.Id,
                firstDate,
                recommendations);

            var created = target is null;
            if (created)
            {
                target = generated;
                db.Trips.Add(target);
            }
            else
            {
                ReplaceTripContents(target!, generated);
            }

            var refreshedTrip = target ?? throw new InvalidOperationException("The premium trip could not be created.");
            refreshedTrip.AccessPinHash = pinHasher.HashPassword(refreshedTrip, PremiumPin);
            refreshedTrip.AccessPinUpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                var entries = string.Join(", ", exception.Entries.Select(entry => $"{entry.Metadata.ClrType.Name}:{entry.State}"));
                throw new InvalidOperationException($"Could not replace the PIN 2222 trip ({entries}).", exception);
            }
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new PremiumDemoTripRefreshResult(
                refreshedTrip.Id,
                refreshedTrip.StartsOn,
                refreshedTrip.EndsOn,
                refreshedTrip.DayPlans.Count,
                refreshedTrip.DayPlans.Select(item => item.City).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                refreshedTrip.DayPlans.SelectMany(item => item.Blocks).Count(),
                refreshedTrip.Reservations.Count,
                created);
        }
    }

    private async Task<Trip> LoadTripAsync(Guid tripId, CancellationToken cancellationToken) =>
        await db.Trips
            .Include(item => item.PlanDraft)
            .Include(item => item.DayPlans)
                .ThenInclude(day => day.Blocks)
                    .ThenInclude(block => block.Reservations)
            .Include(item => item.Reservations)
            .SingleAsync(item => item.Id == tripId, cancellationToken);

    private void ReplaceTripContents(Trip target, Trip generated)
    {
        var nextRevision = target.PlanRevision + 1;
        db.Reservations.RemoveRange(target.Reservations);
        db.TripDayPlans.RemoveRange(target.DayPlans);
        if (target.PlanDraft is not null)
        {
            db.TripPlanDrafts.Remove(target.PlanDraft);
            target.PlanDraft = null;
        }

        target.Reservations.Clear();
        target.DayPlans.Clear();
        target.DestinationId = generated.DestinationId;
        target.TravelerName = generated.TravelerName;
        target.StartsOn = generated.StartsOn;
        target.EndsOn = generated.EndsOn;
        target.TimeZoneId = generated.TimeZoneId;
        target.ExperienceMode = generated.ExperienceMode;
        target.PublicationStatus = generated.PublicationStatus;
        target.PublishedAtUtc = generated.PublishedAtUtc;
        target.UpdatedAtUtc = DateTimeOffset.UtcNow;
        target.PlanRevision = nextRevision;

        foreach (var day in generated.DayPlans)
        {
            day.TripId = target.Id;
            day.Trip = target;
            target.DayPlans.Add(day);
        }
        foreach (var reservation in generated.Reservations)
        {
            reservation.TripId = target.Id;
            reservation.Trip = target;
            target.Reservations.Add(reservation);
        }

        db.TripDayPlans.AddRange(target.DayPlans);
        db.TripDayBlocks.AddRange(target.DayPlans.SelectMany(day => day.Blocks));
        db.Reservations.AddRange(target.Reservations);
    }
}

public sealed record PremiumDemoTripRefreshResult(
    Guid TripId,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int DayCount,
    int CityCount,
    int BlockCount,
    int ReservationCount,
    bool Created);

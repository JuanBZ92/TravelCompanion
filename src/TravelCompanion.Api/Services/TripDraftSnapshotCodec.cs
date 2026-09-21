using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Services;

public static class TripDraftSnapshotCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Capture(Trip trip)
    {
        var snapshot = new TripDraftSnapshot(
            2,
            trip.Id,
            trip.TravelerName,
            trip.StartsOn,
            trip.EndsOn,
            trip.TimeZoneId,
            trip.PlanRevision,
            trip.DayPlans.Select(day => new DaySnapshot(
                day.Id, day.Date, day.DayNumber, day.City, day.HotelBase, day.BaseAddress,
                day.BaseProviderPlaceId, day.BaseLatitude, day.BaseLongitude, day.Introduction,
                day.Blocks.Select(block => new BlockSnapshot(block.Id, block.PeriodKey, block.SortOrder,
                    block.CuratedDescription, block.AutofillEnabled)).ToList())).ToList(),
            trip.Reservations.Select(item => new ReservationSnapshot(
                item.Id, item.ClientMutationId, item.ExternalId, item.TripDayBlockId, item.RecommendationId,
                item.Type, item.PlanningKind, item.Owner, item.ItemSource, item.TimePrecision, item.Flexibility,
                item.DurationMinutes, item.SortOrder, item.ProviderPlaceId, item.Date, item.StartsAt, item.EndsOn,
                item.EndsAt, item.TimeZoneId, item.Title, item.City, item.LocationName, item.Address,
                item.ConfirmationCode, item.Notes, item.Latitude, item.Longitude, item.Airline, item.FlightNumber,
                item.OriginName, item.DestinationName, item.OriginAirport, item.DestinationAirport,
                item.SourceName, item.SourceUrl, item.ReminderEnabled)).ToList(),
            trip.Documents.Select(item => new DocumentSnapshot(item.Id, item.ExternalId, item.Category,
                item.Title, item.Subtitle, item.FileUrl, item.SortOrder)).ToList(),
            trip.PlanDraft is null ? null : new PlanDraftSnapshot(trip.PlanDraft.BasePlanRevision,
                trip.PlanDraft.PayloadJson, trip.PlanDraft.PendingAccessPinHash, trip.PlanDraft.UpdatedAtUtc),
            trip.ThematicRoutes.SelectMany(route => route.Stops)
                .Where(stop => stop.ItineraryItemId.HasValue)
                .Select(stop => new RouteLinkSnapshot(stop.Id, stop.ItineraryItemId!.Value)).ToList(),
            trip.BuilderSegmentsJson);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public static async Task<bool> RestoreAsync(TravelCompanionDbContext dbContext, Trip trip, string? json,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        TripDraftSnapshot? snapshot;
        try { snapshot = JsonSerializer.Deserialize<TripDraftSnapshot>(json, JsonOptions); }
        catch (JsonException) { return false; }
        if (snapshot is null || snapshot.Version is < 1 or > 2 || snapshot.TripId != trip.Id) return false;
        if (await dbContext.Reservations.AnyAsync(item => item.TripId == trip.Id, cancellationToken)
            || await dbContext.TripDayPlans.AnyAsync(item => item.TripId == trip.Id, cancellationToken))
            return false;

        trip.TravelerName = snapshot.TravelerName;
        trip.StartsOn = snapshot.StartsOn;
        trip.EndsOn = snapshot.EndsOn;
        trip.TimeZoneId = snapshot.TimeZoneId;
        trip.BuilderSegmentsJson = snapshot.BuilderSegmentsJson;
        trip.PlanRevision = snapshot.PlanRevision;
        trip.IsArchived = false;
        trip.DraftPurgedAtUtc = null;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;

        foreach (var day in snapshot.Days)
        {
            var entity = new TripDayPlan
            {
                Id = day.Id, TripId = trip.Id, Date = day.Date, DayNumber = day.DayNumber, City = day.City,
                HotelBase = day.HotelBase, BaseAddress = day.BaseAddress, BaseProviderPlaceId = day.BaseProviderPlaceId,
                BaseLatitude = day.BaseLatitude, BaseLongitude = day.BaseLongitude, Introduction = day.Introduction,
                Blocks = day.Blocks.Select(block => new TripDayBlock
                {
                    Id = block.Id, TripDayPlanId = day.Id, PeriodKey = block.PeriodKey, SortOrder = block.SortOrder,
                    CuratedDescription = block.CuratedDescription, AutofillEnabled = block.AutofillEnabled
                }).ToList()
            };
            dbContext.TripDayPlans.Add(entity);
        }
        foreach (var item in snapshot.Reservations)
            dbContext.Reservations.Add(new Reservation
            {
                Id = item.Id, ClientMutationId = item.ClientMutationId, ExternalId = item.ExternalId, TripId = trip.Id,
                TripDayBlockId = item.TripDayBlockId, RecommendationId = item.RecommendationId, Type = item.Type,
                PlanningKind = item.PlanningKind, Owner = item.Owner, ItemSource = item.ItemSource,
                ReminderEnabled = item.ReminderEnabled, TimePrecision = item.TimePrecision, Flexibility = item.Flexibility, DurationMinutes = item.DurationMinutes,
                SortOrder = item.SortOrder, ProviderPlaceId = item.ProviderPlaceId, Date = item.Date,
                StartsAt = item.StartsAt, EndsOn = item.EndsOn, EndsAt = item.EndsAt, TimeZoneId = item.TimeZoneId,
                Title = item.Title, City = item.City, LocationName = item.LocationName, Address = item.Address,
                ConfirmationCode = item.ConfirmationCode, Notes = item.Notes, Latitude = item.Latitude,
                Longitude = item.Longitude, Airline = item.Airline, FlightNumber = item.FlightNumber,
                OriginName = item.OriginName, DestinationName = item.DestinationName,
                OriginAirport = item.OriginAirport, DestinationAirport = item.DestinationAirport,
                SourceName = item.SourceName, SourceUrl = item.SourceUrl
            });
        foreach (var item in snapshot.Documents)
            dbContext.TravelDocuments.Add(new TravelDocument
            {
                Id = item.Id, TripId = trip.Id, ExternalId = item.ExternalId, Category = item.Category,
                Title = item.Title, Subtitle = item.Subtitle, FileUrl = item.FileUrl, SortOrder = item.SortOrder
            });
        if (snapshot.PlanDraft is { } draft)
            dbContext.TripPlanDrafts.Add(new TripPlanDraft
            {
                TripId = trip.Id, BasePlanRevision = draft.BasePlanRevision, PayloadJson = draft.PayloadJson,
                PendingAccessPinHash = draft.PendingAccessPinHash, UpdatedAtUtc = draft.UpdatedAtUtc
            });
        if (snapshot.RouteLinks is { Count: > 0 })
        {
            var stopIds = snapshot.RouteLinks.Select(item => item.StopId).ToList();
            var stops = await dbContext.ThematicRouteStops
                .Where(item => stopIds.Contains(item.Id)).ToListAsync(cancellationToken);
            foreach (var stop in stops)
            {
                var link = snapshot.RouteLinks.First(item => item.StopId == stop.Id);
                stop.ItineraryItemId = link.ItineraryItemId;
            }
        }
        return true;
    }

    private sealed record TripDraftSnapshot(int Version, Guid TripId, string TravelerName, DateOnly StartsOn,
        DateOnly EndsOn, string TimeZoneId, int PlanRevision, IReadOnlyList<DaySnapshot> Days,
        IReadOnlyList<ReservationSnapshot> Reservations, IReadOnlyList<DocumentSnapshot> Documents,
        PlanDraftSnapshot? PlanDraft, IReadOnlyList<RouteLinkSnapshot>? RouteLinks = null,
        string? BuilderSegmentsJson = null);
    private sealed record RouteLinkSnapshot(Guid StopId, Guid ItineraryItemId);
    private sealed record DaySnapshot(Guid Id, DateOnly Date, int DayNumber, string City, string HotelBase,
        string BaseAddress, string? BaseProviderPlaceId, decimal? BaseLatitude, decimal? BaseLongitude,
        string Introduction, IReadOnlyList<BlockSnapshot> Blocks);
    private sealed record BlockSnapshot(Guid Id, string PeriodKey, int SortOrder, string CuratedDescription,
        bool AutofillEnabled);
    private sealed record DocumentSnapshot(Guid Id, string? ExternalId, TravelDocumentCategory Category,
        string Title, string Subtitle, string FileUrl, int SortOrder);
    private sealed record PlanDraftSnapshot(int BasePlanRevision, string PayloadJson, string? PendingAccessPinHash,
        DateTimeOffset UpdatedAtUtc);
    private sealed record ReservationSnapshot(Guid Id, Guid? ClientMutationId, string? ExternalId,
        Guid? TripDayBlockId, Guid? RecommendationId, ReservationType Type, ScheduleItemKind PlanningKind,
        ItineraryItemOwner Owner, ItineraryItemSource ItemSource, ItineraryTimePrecision TimePrecision,
        ItineraryFlexibility Flexibility, int? DurationMinutes, int SortOrder, string? ProviderPlaceId,
        DateOnly Date, TimeOnly StartsAt, DateOnly? EndsOn, TimeOnly? EndsAt, string? TimeZoneId,
        string Title, string City, string LocationName, string Address, string ConfirmationCode, string Notes,
        decimal? Latitude, decimal? Longitude, string? Airline, string? FlightNumber, string? OriginName,
        string? DestinationName, string? OriginAirport, string? DestinationAirport, string? SourceName,
        string? SourceUrl, bool? ReminderEnabled = null);
}

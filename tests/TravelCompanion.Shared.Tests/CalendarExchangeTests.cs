using System.Text;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared.Tests;

public sealed class CalendarExchangeTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static CalendarPreview Parse(params string[] events) => CalendarExchange.Parse(File(events), "Asia/Tokyo", Start, Start.AddDays(30));
    private static string File(params string[] events) => "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("\r\n", events.Select(value => "BEGIN:VEVENT\r\n" + value + "\r\nEND:VEVENT")) + "\r\nEND:VCALENDAR\r\n";

    [Fact]
    public void Imports_utc_and_source_zones_into_trip_zone_including_midnight()
    {
        var result = Parse("UID:madrid\r\nSUMMARY:Salida\r\nDTSTART;TZID=Europe/Madrid:20261008T230000\r\nDTEND;TZID=Europe/Madrid:20261009T000000");
        var item = Assert.Single(result.Events);
        Assert.Equal(new DateOnly(2026, 10, 9), item.Date);
        Assert.Equal(new TimeOnly(6, 0), item.StartsAt);
        Assert.Equal(new TimeOnly(7, 0), item.EndsAt);
        Assert.Equal(60, item.DurationMinutes);
        Assert.Equal("Asia/Tokyo", item.TimeZoneId); Assert.Equal("Europe/Madrid", item.SourceTimeZoneId);
        var utc = Assert.Single(Parse("UID:utc\r\nSUMMARY:UTC\r\nDTSTART:20261008T210000Z").Events);
        Assert.Equal(item.StartsAt, utc.StartsAt); Assert.Equal(item.Date, utc.Date);
    }

    [Fact]
    public void Floating_time_uses_trip_zone_explicitly_and_overnight_duration_survives_mutation()
    {
        var item = Assert.Single(Parse("UID:night\r\nSUMMARY:Noche\r\nDTSTART:20261008T233000\r\nDTEND:20261009T013000").Events);
        Assert.True(item.UsesTripTimeZone); Assert.Equal(120, item.DurationMinutes);
        var mutation = CalendarExchange.Mutation(item, 7);
        Assert.Equal("night", mutation.PeriodKey); Assert.Equal(new TimeOnly(1, 30), mutation.EndsAt);
        Assert.Equal(7, mutation.ExpectedRevision); Assert.Equal("Asia/Tokyo", mutation.TimeZoneId);
        Assert.False(mutation.ReminderEnabled); Assert.Equal(item.ImportKey, mutation.IdempotencyKey);
    }

    [Fact]
    public void Single_all_day_event_is_imported_as_unscheduled_plan()
    {
        var item = Assert.Single(Parse("UID:day\r\nSUMMARY:Día\r\nDTSTART;VALUE=DATE:20261008\r\nDTEND;VALUE=DATE:20261009").Events);
        Assert.Null(item.StartsAt); Assert.False(CalendarExchange.Mutation(item, 0).UseExactTime);
        var tooLong = Parse("SUMMARY:Varios días\r\nDTSTART;VALUE=DATE:20261008\r\nDTEND;VALUE=DATE:20261010");
        Assert.Empty(tooLong.Events); Assert.Equal("CalendarLongEvent", Assert.Single(tooLong.Issues).Reason);
    }

    [Fact]
    public void Duration_is_preserved_without_making_up_an_end_for_unspecified_duration()
    {
        var item = Assert.Single(Parse("SUMMARY:Paseo\r\nDTSTART:20261008T130000\r\nDURATION:PT90M").Events);
        Assert.Equal(90, item.DurationMinutes); Assert.Equal(new TimeOnly(14, 30), item.EndsAt);
        Assert.Null(Assert.Single(Parse("SUMMARY:Sin fin\r\nDTSTART:20261008T130000").Events).DurationMinutes);
    }

    [Fact]
    public void File_with_incompatible_events_keeps_valid_events_and_explains_each_rejection()
    {
        const string valid = "UID:valid\r\nSUMMARY:Válido\r\nDTSTART:20261008T130000";
        var preview = Parse(valid,
            "SUMMARY:Recurrente\r\nDTSTART:20261008T130000\r\nRRULE:FREQ=DAILY",
            "SUMMARY:Adjunto\r\nDTSTART:20261008T130000\r\nATTACH:https://example.test/private.pdf",
            "SUMMARY:Fuera\r\nDTSTART:20270101T130000",
            "SUMMARY:Zona\r\nDTSTART;TZID=Custom/Missing:20261008T130000",
            "SUMMARY:Cancelado\r\nDTSTART:20261008T130000\r\nSTATUS:CANCELLED",
            "SUMMARY:Sin fecha");
        Assert.Single(preview.Events); Assert.Equal(6, preview.Issues.Count);
        Assert.Contains(preview.Issues, x => x.Reason == "CalendarRecurringEvent");
        Assert.Contains(preview.Issues, x => x.Reason == "CalendarAttachedEvent");
        Assert.Contains(preview.Issues, x => x.Reason == "CalendarOutsideTrip");
        Assert.Contains(preview.Issues, x => x.Reason == "CalendarUnknownZone");
    }

    [Fact]
    public void Import_identity_is_stable_for_retries_and_changed_event_with_same_uid()
    {
        var first = Assert.Single(Parse("UID:globally-unique@example.test\r\nSUMMARY:Original\r\nDTSTART:20261008T130000").Events);
        var changed = Assert.Single(Parse("UID:globally-unique@example.test\r\nSUMMARY:Editado\r\nDTSTART:20261009T190000").Events);
        Assert.Equal(first.ImportKey, changed.ImportKey);
        Assert.Equal(68, first.ImportKey.Length);
        Assert.Single(Parse("UID:same\r\nSUMMARY:Uno\r\nDTSTART:20261008T130000", "UID:same\r\nSUMMARY:Dos\r\nDTSTART:20261008T140000").Events);
    }

    [Theory]
    [InlineData("20260329T023000")]
    [InlineData("20261025T023000")]
    public void Rejects_invalid_or_ambiguous_local_dst_times(string start)
    {
        var preview = CalendarExchange.Parse(File($"SUMMARY:DST\r\nDTSTART;TZID=Europe/Madrid:{start}"), "Europe/Madrid", new(2026, 1, 1), new(2026, 12, 31));
        Assert.Empty(preview.Events); Assert.Equal("CalendarAmbiguousTime", Assert.Single(preview.Issues).Reason);
    }

    [Fact]
    public void Export_selects_only_requested_events_with_stable_uids_utc_instants_and_utf8_folding()
    {
        var id = Guid.NewGuid();
        var item = Item(id) with { Title = new string('庭', 60), LocationName = "Tokyo, Japan; entrada", Notes = "Primera\nSegunda\\fin\r\nBEGIN:VEVENT" };
        var schedule = new TripScheduleDto(Guid.NewGuid(), "Traveler", "Japan", Start, Start.AddDays(30), [item, Item(Guid.NewGuid())]);
        var output = CalendarExchange.Export(schedule, [id], new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains("DTSTART:20261008T143000Z", output);
        Assert.Contains($"UID:{id:N}.{schedule.TripId:N}@yuku.app", output.Replace("\r\n ", ""));
        Assert.Single(output.Split("\r\nBEGIN:VEVENT\r\n").Skip(1));
        Assert.All(output.Split("\r\n"), line => Assert.InRange(Encoding.UTF8.GetByteCount(line), 0, 75));
        var roundTrip = Assert.Single(CalendarExchange.Parse(output, "Asia/Tokyo", Start, Start.AddDays(30)).Events);
        Assert.Equal(item.Title, roundTrip.Title); Assert.Equal(item.LocationName, roundTrip.Location);
        Assert.Equal(item.Notes.Replace("\r\n", "\n"), roundTrip.Notes);
        Assert.Equal(120, roundTrip.DurationMinutes);
    }

    [Fact]
    public void Exported_period_only_plan_is_an_all_day_event()
    {
        var item = Item(Guid.NewGuid()) with { TimePrecision = ItineraryTimePrecision.PeriodOnly };
        var output = CalendarExchange.Export(new(Guid.NewGuid(), "", "", Start, Start.AddDays(30), [item]), [item.Id], DateTimeOffset.UtcNow);
        Assert.Contains("DTSTART;VALUE=DATE:20261008", output);
        Assert.Null(Assert.Single(CalendarExchange.Parse(output, "Asia/Tokyo", Start, Start.AddDays(30)).Events).StartsAt);
    }

    [Fact]
    public void Unfolds_escaped_text_and_does_not_read_alarm_properties_as_event_fields()
    {
        var item = Assert.Single(Parse("UID:folded\r\nSUMMARY:Un título\r\n  largo\\, bonito\r\nDESCRIPTION:Uno\\nDos\r\nDTSTART:20261008T130000\r\nBEGIN:VALARM\r\nSUMMARY:No mostrar\r\nEND:VALARM").Events);
        Assert.Equal("Un título largo, bonito", item.Title); Assert.Equal("Uno\nDos", item.Notes);
    }

    [Fact]
    public void Invalid_structure_oversize_and_event_count_are_bounded()
    {
        Assert.Empty(Parse("SUMMARY:Bad\r\nDTSTART:bad").Events);
        Assert.Throws<ArgumentException>(() => CalendarExchange.Parse("not a calendar", "Asia/Tokyo", Start, Start.AddDays(30)));
        Assert.Throws<ArgumentException>(() => CalendarExchange.Parse(new string('a', CalendarExchange.MaximumBytes + 1), "Asia/Tokyo", Start, Start.AddDays(30)));
        Assert.Throws<ArgumentException>(() => Parse(Enumerable.Repeat("SUMMARY:Many\r\nDTSTART:20261008T130000", CalendarExchange.MaximumEvents + 1).ToArray()));
    }

    [Fact]
    public void Rejects_ambiguous_duplicate_fields_without_hiding_other_events()
    {
        var preview = Parse("SUMMARY:Uno\r\nSUMMARY:Dos\r\nDTSTART:20261008T130000", "SUMMARY:Bien\r\nDTSTART:20261008T130000");
        Assert.Single(preview.Events); Assert.Equal("CalendarMalformedEvent", Assert.Single(preview.Issues).Reason);
    }

    private static ScheduleItemDto Item(Guid id) => new(id, null, ReservationType.Event, new(2026, 10, 8), new(23, 30),
        null, new(1, 30), "Noche", "Tokyo", "Tokyo", "", "", "", null, null, null, null, null, null,
        TimeZoneId: "Asia/Tokyo");

    [Theory]
    [InlineData(SessionAccessMode.Trip, true, true)]
    [InlineData(SessionAccessMode.Builder, true, true)]
    [InlineData(SessionAccessMode.Trip, false, false)]
    [InlineData(SessionAccessMode.Builder, false, false)]
    [InlineData(SessionAccessMode.BuilderReadOnly, true, false)]
    [InlineData(SessionAccessMode.FreeMapPreview, true, false)]
    public void Export_never_uses_cached_paid_access_after_expiry_or_downgrade(SessionAccessMode mode, bool valid, bool curatedAllowed)
    {
        var recommendation = Recommendation(ContentAccessLevel.Subscription);
        var curated = Item(Guid.NewGuid()) with { RecommendationId = recommendation.Id, PlanningKind = ScheduleItemKind.Recommendation };
        var personal = curated with { Id = Guid.NewGuid(), Owner = ItineraryItemOwner.Traveler };
        var entitlements = new UserEntitlementsDto(Guid.NewGuid(), "", "", [ContentAccessLevel.Subscription], [recommendation.DestinationId], [],
            [new(Guid.NewGuid(), ContentAccessLevel.Subscription, recommendation.DestinationId, null, DateTimeOffset.UtcNow.AddDays(-1), null, "test")]);
        Assert.Equal(curatedAllowed, CalendarExchange.CanExportItem(curated, valid, mode, recommendation, entitlements));
        Assert.True(CalendarExchange.CanExportItem(personal, valid, mode));
    }

    [Fact]
    public void Free_editorial_ideas_and_user_bookings_do_not_require_a_paid_pass()
    {
        var recommendation = Recommendation(ContentAccessLevel.Free);
        var idea = Item(Guid.NewGuid()) with { RecommendationId = recommendation.Id, PlanningKind = ScheduleItemKind.Recommendation };
        Assert.True(CalendarExchange.CanExportItem(idea, false, SessionAccessMode.FreeMapPreview, recommendation));
        Assert.True(CalendarExchange.CanExportItem(idea with { PlanningKind = ScheduleItemKind.ConfirmedReservation }, false, SessionAccessMode.BuilderReadOnly));
        Assert.True(CalendarExchange.CanExportItem(Item(Guid.NewGuid()), false, SessionAccessMode.FreeMapPreview));
        Assert.True(CalendarExchange.CanExportItem(idea with { Type = ReservationType.Lodging }, false, SessionAccessMode.BuilderReadOnly));
        Assert.True(CalendarExchange.CanExportItem(idea with { Type = ReservationType.Flight }, false, SessionAccessMode.BuilderReadOnly));
    }

    [Fact]
    public void Cached_expired_package_grant_and_missing_catalog_metadata_cannot_unlock_editorial_idea()
    {
        var package = Guid.NewGuid();
        var recommendation = Recommendation(ContentAccessLevel.Free) with { PackageIds = [package] };
        var idea = Item(Guid.NewGuid()) with { RecommendationId = recommendation.Id, PlanningKind = ScheduleItemKind.Recommendation };
        var entitlements = new UserEntitlementsDto(Guid.NewGuid(), "", "", [ContentAccessLevel.Paid], [], [package],
            [new(Guid.NewGuid(), ContentAccessLevel.Paid, recommendation.DestinationId, package, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), "test")]);
        Assert.False(CalendarExchange.CanExportItem(idea, true, SessionAccessMode.Builder, recommendation, entitlements));
        Assert.False(CalendarExchange.CanExportItem(idea, true, SessionAccessMode.Builder));
    }

    private static RecommendationDto Recommendation(ContentAccessLevel access) => new(Guid.NewGuid(), Guid.NewGuid(), "Editorial", "Culture", "Tokyo", "Description", [], "low", 35, 139, 60, null, null, access, [], null);
}

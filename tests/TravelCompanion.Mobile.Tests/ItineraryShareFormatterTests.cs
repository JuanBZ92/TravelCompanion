using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class ItineraryShareFormatterTests
{
    [Fact]
    public void Formats_public_itinerary_details_without_private_booking_data()
    {
        var date = new DateOnly(2026, 10, 1);
        var item = new ScheduleItemDto(
            Guid.NewGuid(), null, ReservationType.Event, date, new TimeOnly(10, 0), null, new TimeOnly(11, 0),
            "Museo Edo", "Tokyo", "Ryogoku", "Tokyo", "SECRET-123", "nota privada",
            null, null, null, null, null, null,
            ScheduleItemKind.ConfirmedReservation, ItineraryItemOwner.Traveler,
            ItineraryItemSource.Manual, ItineraryTimePrecision.Exact);

        var text = ItineraryShareFormatter.Format("Japón", date, date.AddDays(1), [item]);

        Assert.Contains("Museo Edo", text);
        Assert.Contains("10:00", text);
        Assert.Contains("Ryogoku", text);
        Assert.DoesNotContain("SECRET-123", text);
        Assert.DoesNotContain("nota privada", text);
    }
}

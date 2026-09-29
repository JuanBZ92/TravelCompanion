using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class DayReviewConfirmation
{
    public static string Key(Guid user, Guid trip, DateOnly date) => $"day-reviewed-{user:N}-{trip:N}-{date:yyyy-MM-dd}";
    public static string Fingerprint(IEnumerable<ScheduleItemDto> items, DateOnly date)
    {
        var day = items.Where(x => x.Date <= date && (x.EndsOn ?? x.Date) >= date).OrderBy(x => x.Id).ToArray();
        // Include the analysis too so changed review rules cannot suppress a new warning.
        var content = JsonSerializer.Serialize(new { Day = day, Review = ScheduleReviewAnalyzer.Analyze(day, date, date) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}

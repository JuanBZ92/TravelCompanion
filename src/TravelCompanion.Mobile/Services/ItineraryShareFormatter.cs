using System.Globalization;
using System.Text;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class ItineraryShareFormatter
{
    public static string Format(
        string destinationName,
        DateOnly startsOn,
        DateOnly endsOn,
        IReadOnlyList<ScheduleItemDto> items)
    {
        var culture = CultureInfo.CurrentCulture;
        var builder = new StringBuilder()
            .Append("Mi viaje a ").AppendLine(destinationName)
            .Append(startsOn.ToString("d MMM", culture))
            .Append(" – ")
            .AppendLine(endsOn.ToString("d MMM yyyy", culture));

        foreach (var group in items
                     .Where(item => item.Type != TravelCompanion.Shared.ReservationType.Lodging)
                     .OrderBy(item => item.Date)
                     .ThenBy(item => item.StartsAt)
                     .GroupBy(item => item.Date))
        {
            builder.AppendLine()
                .AppendLine(group.Key.ToString("dddd, d MMMM", culture));
            foreach (var item in group)
            {
                builder.Append("• ");
                if (item.HasExactTime)
                {
                    builder.Append(item.StartsAt.ToString("HH:mm", culture)).Append(" · ");
                }
                builder.Append(item.Title);
                var location = string.IsNullOrWhiteSpace(item.LocationName) ? item.Address : item.LocationName;
                if (!string.IsNullOrWhiteSpace(location) && !string.Equals(location, item.Title, StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append(" — ").Append(location);
                }
                builder.AppendLine();
            }
        }

        builder.AppendLine().Append("Creado con Yuku Japan");
        return builder.ToString();
    }
}

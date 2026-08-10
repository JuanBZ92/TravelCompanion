using System.Globalization;
using System.Text;

namespace TravelCompanion.Api.Services;

public static class RecommendationCitySlug
{
    public static string FromCity(string? value)
    {
        var city = (value ?? string.Empty).Split(',', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(city))
        {
            return string.Empty;
        }

        var normalized = city.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var pendingSeparator = false;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}

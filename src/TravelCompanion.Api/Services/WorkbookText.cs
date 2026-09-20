using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TravelCompanion.Api.Services;

internal static partial class WorkbookText
{
    public static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var noDiacritics = RemoveDiacritics(value)
            .Replace('/', ' ')
            .Replace('(', ' ')
            .Replace(')', ' ')
            .Replace('-', ' ');
        return WhitespaceRegex().Replace(noDiacritics, " ").Trim().ToLowerInvariant();
    }

    public static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface ITravelAssistantActionPlanner
{
    Task<TravelAssistantActionPlan> CreateAsync(
        TravelChatRequest request,
        TravelChatConversation? conversation,
        CancellationToken cancellationToken);
}

public sealed record TravelAssistantActionPlan(
    string MessageForExecution,
    TravelChatIntentResult Intent,
    DateOnly Date,
    string ResponseMode,
    TravelPreferenceProfilePatchDto? RequestedPreferencePatch,
    TravelPreferenceProfilePatchDto? PendingPreferencePatch,
    string? PendingPreferenceOriginalMessage,
    bool ShouldApplyPendingPreference,
    bool ShouldRejectPendingPreference,
    bool SuppressPreferenceConfirmation,
    TravelPreferenceProfilePatchDto? TemporaryPreferencePatch);

public sealed class TravelAssistantActionPlanner(
    ITravelChatIntentClassifier intentClassifier,
    ITravelPreferenceCommandParser preferenceCommandParser) : ITravelAssistantActionPlanner
{
    public async Task<TravelAssistantActionPlan> CreateAsync(
        TravelChatRequest request,
        TravelChatConversation? conversation,
        CancellationToken cancellationToken)
    {
        var messageForExecution = request.Message;
        var pendingPreferencePatch = preferenceCommandParser.ReadPendingPreferencePatch(conversation);
        var shouldApplyPendingPreference = false;
        var shouldRejectPendingPreference = false;
        var suppressPreferenceConfirmation = false;
        TravelPreferenceProfilePatchDto? temporaryPreferencePatch = null;

        var intent = intentClassifier.Classify(messageForExecution);
        var pendingPreferenceOriginalMessage = conversation?.PendingPreferenceOriginalMessage;

        if (pendingPreferencePatch is not null
            && preferenceCommandParser.IsPreferenceConfirmationReply(request.Message))
        {
            messageForExecution = string.IsNullOrWhiteSpace(pendingPreferenceOriginalMessage)
                ? request.Message
                : pendingPreferenceOriginalMessage;
            intent = intentClassifier.Classify(messageForExecution);
            suppressPreferenceConfirmation = true;

            if (preferenceCommandParser.IsPositiveConfirmation(request.Message))
            {
                shouldApplyPendingPreference = true;
            }
            else
            {
                shouldRejectPendingPreference = true;
                if (intent.IsPlanning)
                {
                    temporaryPreferencePatch = pendingPreferencePatch;
                }
            }
        }

        var requestedPreferencePatch = !suppressPreferenceConfirmation
            && intent.Intent == TravelChatIntents.ViewPreferences
            ? await preferenceCommandParser.CreatePatchFromMessageAsync(
                request.Message,
                cancellationToken).ConfigureAwait(false)
            : null;

        var baseDate = request.Date
            ?? conversation?.LastDate
            ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var date = TravelAssistantDateResolver.ResolveRequestedDate(messageForExecution, baseDate);

        return new TravelAssistantActionPlan(
            messageForExecution,
            intent,
            date,
            intent.ResponseMode,
            requestedPreferencePatch,
            pendingPreferencePatch,
            pendingPreferenceOriginalMessage,
            shouldApplyPendingPreference,
            shouldRejectPendingPreference,
            suppressPreferenceConfirmation,
            temporaryPreferencePatch);
    }
}

internal static class TravelAssistantDateResolver
{
    public static DateOnly ResolveRequestedDate(string? message, DateOnly fallbackDate)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return fallbackDate;
        }

        var normalized = RemoveDiacritics(message).ToLowerInvariant();
        if (ContainsAny(normalized, "pasado manana"))
        {
            return fallbackDate.AddDays(2);
        }

        if (ContainsAny(normalized, "manana"))
        {
            return fallbackDate.AddDays(1);
        }

        if (ContainsAny(normalized, "hoy"))
        {
            return fallbackDate;
        }

        var isoMatch = Regex.Match(normalized, @"\b(?<year>\d{4})-(?<month>\d{1,2})-(?<day>\d{1,2})\b");
        if (isoMatch.Success
            && TryCreateDate(
                int.Parse(isoMatch.Groups["year"].Value, CultureInfo.InvariantCulture),
                int.Parse(isoMatch.Groups["month"].Value, CultureInfo.InvariantCulture),
                int.Parse(isoMatch.Groups["day"].Value, CultureInfo.InvariantCulture),
                out var isoDate))
        {
            return isoDate;
        }

        var slashMatch = Regex.Match(normalized, @"\b(?<day>\d{1,2})[/-](?<month>\d{1,2})(?:[/-](?<year>\d{2,4}))?\b");
        if (slashMatch.Success)
        {
            var year = slashMatch.Groups["year"].Success
                ? NormalizeYear(int.Parse(slashMatch.Groups["year"].Value, CultureInfo.InvariantCulture))
                : fallbackDate.Year;
            if (TryCreateDate(
                year,
                int.Parse(slashMatch.Groups["month"].Value, CultureInfo.InvariantCulture),
                int.Parse(slashMatch.Groups["day"].Value, CultureInfo.InvariantCulture),
                out var slashDate))
            {
                return slashDate;
            }
        }

        var monthMatch = Regex.Match(
            normalized,
            @"\b(?<day>\d{1,2})\s+de\s+(?<month>[a-z]+)(?:\s+de\s+(?<year>\d{4}))?\b");
        if (monthMatch.Success
            && TryParseSpanishMonth(monthMatch.Groups["month"].Value, out var monthNumber))
        {
            var year = monthMatch.Groups["year"].Success
                ? int.Parse(monthMatch.Groups["year"].Value, CultureInfo.InvariantCulture)
                : fallbackDate.Year;
            if (TryCreateDate(
                year,
                monthNumber,
                int.Parse(monthMatch.Groups["day"].Value, CultureInfo.InvariantCulture),
                out var monthDate))
            {
                return monthDate;
            }
        }

        return fallbackDate;
    }

    private static bool TryCreateDate(int year, int month, int day, out DateOnly date)
    {
        try
        {
            date = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            return false;
        }
    }

    private static int NormalizeYear(int year)
    {
        return year < 100 ? 2000 + year : year;
    }

    private static bool TryParseSpanishMonth(string value, out int month)
    {
        month = value switch
        {
            "enero" => 1,
            "febrero" => 2,
            "marzo" => 3,
            "abril" => 4,
            "mayo" => 5,
            "junio" => 6,
            "julio" => 7,
            "agosto" => 8,
            "septiembre" or "setiembre" => 9,
            "octubre" => 10,
            "noviembre" => 11,
            "diciembre" => 12,
            _ => 0
        };

        return month > 0;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(capacity: normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }
}

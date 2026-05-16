using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

internal static class MobilePayloadNormalizer
{
    public static MobileDiscoverDto? Normalize(MobileDiscoverDto? discover)
    {
        if (discover?.Destination is null)
        {
            return null;
        }

        return discover with
        {
            Recommendations = discover.Recommendations ?? []
        };
    }

    public static MobileBootstrapDto? Normalize(MobileBootstrapDto? bootstrap)
    {
        if (bootstrap?.Destination is null || bootstrap.Entitlements is null)
        {
            return null;
        }

        return bootstrap with
        {
            Entitlements = Normalize(bootstrap.Entitlements),
            Recommendations = bootstrap.Recommendations ?? [],
            Packages = bootstrap.Packages ?? [],
            Schedule = Normalize(bootstrap.Schedule)
        };
    }

    public static TripScheduleDto? Normalize(TripScheduleDto? schedule)
    {
        if (schedule is null)
        {
            return null;
        }

        return schedule with
        {
            TravelerName = schedule.TravelerName ?? string.Empty,
            DestinationName = schedule.DestinationName ?? string.Empty,
            Items = schedule.Items ?? []
        };
    }

    public static TravelChatResponse? Normalize(TravelChatResponse? response)
    {
        if (response is null)
        {
            return null;
        }

        return response with
        {
            ConversationId = response.ConversationId ?? string.Empty,
            Message = response.Message ?? string.Empty,
            Intent = response.Intent ?? string.Empty,
            Cards = response.Cards?.Select(Normalize).ToList() ?? [],
            SuggestedReplies = response.SuggestedReplies ?? [],
            MissingContext = Normalize(response.MissingContext)
        };
    }

    private static UserEntitlementsDto Normalize(UserEntitlementsDto entitlements)
    {
        return entitlements with
        {
            Email = entitlements.Email ?? string.Empty,
            DisplayName = entitlements.DisplayName ?? string.Empty,
            AccessLevels = entitlements.AccessLevels ?? [],
            DestinationIds = entitlements.DestinationIds ?? [],
            PackageIds = entitlements.PackageIds ?? [],
            Entitlements = entitlements.Entitlements ?? []
        };
    }

    private static TravelCardDto Normalize(TravelCardDto card)
    {
        return card with
        {
            Type = card.Type ?? string.Empty,
            Title = card.Title ?? string.Empty,
            WhyItFits = card.WhyItFits ?? [],
            Warnings = card.Warnings ?? []
        };
    }

    private static MissingContextDto? Normalize(MissingContextDto? missingContext)
    {
        if (missingContext is null)
        {
            return null;
        }

        return missingContext with
        {
            Field = missingContext.Field ?? string.Empty,
            Message = missingContext.Message ?? string.Empty,
            Suggestions = missingContext.Suggestions ?? []
        };
    }
}

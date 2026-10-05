using System.Text.Json;
using System.Text.Json.Nodes;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

// Stored alongside the public proposal in the existing lease JSON, never returned by the API.
internal sealed class DayPlanProposalState
{
    private const string PropertyName = "_planningState";
    public DayPlanProposalState() { }
    public DayPlanPreferencesDto? Preferences { get; set; }
    public string? Locale { get; set; }
    public HashSet<Guid> SeenRecommendationIds { get; set; } = [];
    public HashSet<string> SeenTitles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SeenProviderPlaceIds { get; set; } = [];
    public HashSet<Guid> AppliedStopIds { get; set; } = [];
    public List<DayPlanReplacementReceipt> Replacements { get; set; } = [];

    public static DayPlanProposalState Read(string json, DayPlanResponse proposal)
    {
        using var document = JsonDocument.Parse(json);
        var state = document.RootElement.TryGetProperty(PropertyName, out var property)
            ? property.Deserialize<DayPlanProposalState>() ?? new() : new DayPlanProposalState();
        state.SeenRecommendationIds.UnionWith(proposal.Days.SelectMany(day => day.Stops).Select(stop => stop.RecommendationId));
        var seenCards = proposal.Days.SelectMany(day => day.Stops)
            .Concat(state.Replacements.SelectMany(change => new[] { change.Before, change.After }).OfType<DayPlanStopDto>()).ToList();
        state.SeenTitles = state.SeenTitles.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.SeenTitles.UnionWith(seenCards.Select(stop => stop.Card.Title).Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));
        state.SeenProviderPlaceIds = state.SeenProviderPlaceIds.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).ToHashSet(StringComparer.Ordinal);
        state.SeenProviderPlaceIds.UnionWith(seenCards.Select(stop => stop.Card.ProviderPlaceId).OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
        return state;
    }

    public string Serialize(DayPlanResponse proposal)
    {
        var node = JsonSerializer.SerializeToNode(proposal)!.AsObject();
        node[PropertyName] = JsonSerializer.SerializeToNode(this);
        return node.ToJsonString();
    }

    public DayPlanResponse Snapshot(DayPlanResponse current, int revision)
    {
        var days = current.Days.ToList();
        // Receipts store only the changed card; undo later changes to replay a stable response
        // without retaining a complete copy of every multi-day proposal per replacement.
        foreach (var change in Replacements.AsEnumerable().Reverse().Where(item => item.Replaced && item.ProposalRevision > revision))
        {
            var index = days.FindIndex(day => day.Date == change.Date);
            var day = days[index];
            days[index] = day with { Stops = day.Stops.Select(stop => stop.Id == change.After!.Id ? change.Before! : stop).ToList() };
        }
        return current with { Days = days, ProposalRevision = revision };
    }
}

internal sealed record DayPlanReplacementReceipt(Guid MutationId, string RequestHash, bool Replaced,
    string Code, string Message, int ProposalRevision, DateOnly Date, DayPlanStopDto? Before, DayPlanStopDto? After);

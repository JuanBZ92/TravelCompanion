using System.Globalization;
using System.Text;
using System.Text.Json;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface ITravelPreferenceCommandParser
{
    Task<TravelPreferenceProfilePatchDto?> CreatePatchFromMessageAsync(
        string? message,
        CancellationToken cancellationToken);

    TravelPreferenceProfilePatchDto? ReadPendingPreferencePatch(TravelChatConversation? conversation);

    bool IsPreferenceConfirmationReply(string? message);

    bool IsPositiveConfirmation(string? message);

    bool IsNegativeConfirmation(string? message);
}

public sealed class TravelPreferenceCommandParser(
    IRecommendationTagCatalogService tagCatalogService) : ITravelPreferenceCommandParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TravelPreferenceProfilePatchDto?> CreatePatchFromMessageAsync(
        string? message,
        CancellationToken cancellationToken)
    {
        var avoidedTags = string.IsNullOrWhiteSpace(message)
            ? []
            : await tagCatalogService.ResolveAvoidedTagsAsync(
                message,
                cancellationToken: cancellationToken).ConfigureAwait(false);

        return CreatePreferencePatchFromMessage(message, avoidedTags);
    }

    public TravelPreferenceProfilePatchDto? ReadPendingPreferencePatch(TravelChatConversation? conversation)
    {
        if (string.IsNullOrWhiteSpace(conversation?.PendingPreferencePatchJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TravelPreferenceProfilePatchDto>(
                conversation.PendingPreferencePatchJson,
                JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool IsPreferenceConfirmationReply(string? message)
    {
        return IsPositiveConfirmation(message) || IsNegativeConfirmation(message);
    }

    public bool IsPositiveConfirmation(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = RemoveDiacritics(message).Trim().ToLowerInvariant();
        return normalized is "si" or "ok" or "dale" or "confirmo"
            || ContainsAny(normalized, "si guardar", "guardar preferencia", "confirmar", "confirmo", "yes", "save preference");
    }

    public bool IsNegativeConfirmation(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = RemoveDiacritics(message).Trim().ToLowerInvariant();
        return normalized == "no"
            || normalized.StartsWith("no ", StringComparison.Ordinal)
            || ContainsAny(normalized, "solo este pedido", "no guardar", "no lo guardes", "dont save", "do not save");
    }

    private static TravelPreferenceProfilePatchDto? CreatePreferencePatchFromMessage(
        string? message,
        IReadOnlyList<string> avoidedRecommendationTags)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var normalized = RemoveDiacritics(message).ToLowerInvariant();
        var interests = new List<string>();
        var foodPreferences = new List<string>();
        var dietaryRestrictions = new List<string>();
        var dislikes = new List<string>();
        string? budgetLevel = null;
        string? travelPace = null;
        int? maxWalkingMinutes = null;
        var hasAvoidSignal = HasAvoidSignal(normalized);

        if (ContainsAny(normalized, "presupuesto bajo", "coste bajo", "costo bajo", "precio bajo", "bajo coste", "bajo costo", "barato", "economico", "gratis", "low budget", "low cost", "cheap", "free"))
        {
            budgetLevel = ContainsAny(normalized, "gratis", "free") ? "free" : "low";
        }
        else if (ContainsAny(normalized, "presupuesto alto", "coste alto", "costo alto", "precio alto", "premium", "caro", "alta gama", "high budget", "high cost", "expensive", "upscale"))
        {
            budgetLevel = "high";
        }
        else if (ContainsAny(normalized, "presupuesto medio", "coste medio", "costo medio", "precio medio", "moderado", "medium budget", "medium cost", "moderate", "mid range"))
        {
            budgetLevel = "medium";
        }

        if (ContainsAny(normalized, "ritmo tranquilo", "ritmo relajado", "sin apuro", "poca caminata", "relaxed pace", "easy pace", "slow pace"))
        {
            travelPace = "relaxed";
        }
        else if (ContainsAny(normalized, "ritmo rapido", "ritmo eficiente", "aprovechar mucho", "fast pace", "efficient pace"))
        {
            travelPace = "efficient";
        }
        else if (ContainsAny(normalized, "ritmo balanceado", "ritmo equilibrado", "balanced pace"))
        {
            travelPace = "balanced";
        }

        if (ContainsAny(normalized, "menos caminata", "poca caminata", "caminar poco", "less walking", "low walking"))
        {
            maxWalkingMinutes = 12;
        }

        if (!hasAvoidSignal && ContainsAny(normalized, "comida", "gastronomia", "restaurante", "cafe", "food"))
        {
            interests.Add("Food");
            foodPreferences.Add("local food");
        }

        if (!hasAvoidSignal && ContainsAny(normalized, "cultura", "culture", "museo", "museum", "historia", "history", "arte", "art"))
        {
            interests.Add("Culture");
        }

        if (!hasAvoidSignal && ContainsAny(normalized, "compras", "shopping", "tiendas"))
        {
            interests.Add("Shopping");
        }

        if (!hasAvoidSignal && ContainsAny(normalized, "barrio", "barrios", "neighborhood"))
        {
            interests.Add("Neighborhood");
        }

        if (ContainsAny(normalized, "vegetariano", "vegetariana", "vegetarian"))
        {
            dietaryRestrictions.Add("vegetarian");
        }

        if (ContainsAny(normalized, "sin gluten", "gluten free", "celiaco", "celiaca"))
        {
            dietaryRestrictions.Add("gluten-free");
        }

        if (ContainsAny(normalized, "no me gusta museos", "sin museos", "evitar museos"))
        {
            dislikes.Add("museum");
        }

        if (ContainsAny(normalized, "no me gusta shopping", "sin shopping", "evitar compras"))
        {
            dislikes.Add("shopping");
        }

        foreach (var dislikedTag in avoidedRecommendationTags)
        {
            AddUnique(dislikes, dislikedTag);
        }

        if (budgetLevel is null
            && travelPace is null
            && maxWalkingMinutes is null
            && interests.Count == 0
            && foodPreferences.Count == 0
            && dietaryRestrictions.Count == 0
            && dislikes.Count == 0)
        {
            return null;
        }

        return new TravelPreferenceProfilePatchDto(
            foodPreferences.Count == 0 ? null : foodPreferences,
            dietaryRestrictions.Count == 0 ? null : dietaryRestrictions,
            budgetLevel,
            travelPace,
            interests.Count == 0 ? null : interests,
            dislikes.Count == 0 ? null : dislikes,
            null,
            maxWalkingMinutes);
    }

    private static bool HasAvoidSignal(string normalized)
    {
        return ContainsAny(
            normalized,
            "evitar",
            "avoid",
            "no me gusta",
            "no quiero",
            "evita",
            "evite",
            "evitando",
            "sin museos",
            "sin museo",
            "sin cultura",
            "sin culture",
            "sin shopping",
            "sin compras");
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
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
}

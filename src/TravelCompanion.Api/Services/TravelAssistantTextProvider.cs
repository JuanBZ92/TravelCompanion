using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface ITravelAssistantTextProvider
{
    string NormalizeLocale(string? locale);
    bool IsSpanish(string? locale);
    string HelpMessage(string? locale);
    IReadOnlyList<string> HelpReplies(string? locale);
    string UnsupportedMessage(string? locale);
    IReadOnlyList<string> UnsupportedReplies(string? locale);
    string SaveRequiresConfirmationMessage(string? locale);
    IReadOnlyList<string> SaveRequiresConfirmationReplies(string? locale);
    string MinimumPreferencesMissingMessage(string? locale);
    IReadOnlyList<string> PreferenceSuggestions(IReadOnlyList<string> missingFields, string? locale);
    string NoActiveTripMessage(DateOnly date, string? locale);
    IReadOnlyList<string> NoActiveTripReplies(string? locale);
    string NoRecommendationsMessage(string city, string? locale);
    IReadOnlyList<string> NoRecommendationsReplies(string? locale);
    string NoPlanningWindowMessage(DateOnly date, string? locale);
    IReadOnlyList<string> NoPlanningWindowReplies(string? locale);
    string ScheduleNoActiveTripMessage(DateOnly date, string? locale);
    IReadOnlyList<string> ScheduleNoActiveTripReplies(string? locale);
    string EmptyScheduleMessage(DateOnly date, string destinationName, string? locale);
    IReadOnlyList<string> EmptyScheduleReplies(DateOnly date, string? locale);
    string ScheduleSummaryMessage(DateOnly date, string destinationName, IReadOnlyList<Reservation> reservations, string? locale);
    IReadOnlyList<string> ScheduleReplies(DateOnly date, string? locale);
    string PreferencesMessage(TravelPreferenceProfileDto profile, string? locale);
    IReadOnlyList<string> PreferencesReplies(string? locale);
    string PreferenceConfirmationMessage(TravelPreferenceProfilePatchDto patch, string? locale);
    string PreferenceConfirmationMissingMessage(string? locale);
    IReadOnlyList<string> PreferenceConfirmationReplies(string? locale);
    string PreferenceConfirmedMessage(TravelPreferenceProfileDto profile, string? locale);
    string PreferenceRejectedMessage(string? locale);
    IReadOnlyList<string> PreferenceAfterChangeReplies(string? locale);
    string AssistantPlanMessage(
        string city,
        (TimeOnly Start, TimeOnly End, int AvailableMinutes) planningWindow,
        IReadOnlyList<ScoredRecommendation> ranked,
        string responseMode,
        string? locale);
    IReadOnlyList<string> SuggestedReplies(string responseMode, string? locale);
}

public sealed class TravelAssistantTextProvider : ITravelAssistantTextProvider
{
    public string NormalizeLocale(string? locale)
    {
        return IsSpanish(locale) ? "es" : "en-US";
    }

    public bool IsSpanish(string? locale)
    {
        return string.IsNullOrWhiteSpace(locale)
            || locale.StartsWith("es", StringComparison.OrdinalIgnoreCase);
    }

    public string HelpMessage(string? locale)
    {
        return IsSpanish(locale)
            ? "Puedo ayudarte en 5 modos:\n1. Planificar: Plan para comer, plan para relajar o plan por fecha.\n2. Ajustar: Recomendar por cercania, por duracion u otra opcion.\n3. Agenda: Ver mi agenda.\n4. Preferencias: Ver mis preferencias o Evitar #culture.\n5. Ayuda: Que puedo pedirte."
            : "I can help in 5 ways:\n1. Plan: food plans, relaxing plans, or plans for a date.\n2. Adjust: recommend nearby, by duration, or another option.\n3. Schedule: show my schedule.\n4. Preferences: show my preferences or avoid #culture.\n5. Help: what can I ask.";
    }

    public IReadOnlyList<string> HelpReplies(string? locale)
    {
        return IsSpanish(locale)
            ? [
                TravelAssistantCommandText.PlanForFood,
                TravelAssistantCommandText.PlanForRelax,
                TravelAssistantCommandText.RecommendNearby,
                TravelAssistantCommandText.ViewSchedule,
                TravelAssistantCommandText.ViewPreferences
            ]
            : ["Plan for food", "Plan to relax", "Recommend nearby", "Show my schedule", "Show my preferences"];
    }

    public string UnsupportedMessage(string? locale)
    {
        return IsSpanish(locale)
            ? "No entendi ese pedido. Puedo proponerte planes, revisar tu agenda o ayudarte a ajustar preferencias."
            : "I did not understand that request. I can suggest plans, review your schedule, or help adjust preferences.";
    }

    public IReadOnlyList<string> UnsupportedReplies(string? locale)
    {
        return IsSpanish(locale)
            ? [
                TravelAssistantCommandText.HelpCapabilities,
                TravelAssistantCommandText.PlanForFood,
                TravelAssistantCommandText.ViewSchedule,
                TravelAssistantCommandText.ViewPreferences,
                TravelAssistantCommandText.RecommendNearby
            ]
            : ["What can I ask", "Plan for food", "Show my schedule", "Show my preferences", "Recommend nearby"];
    }

    public string SaveRequiresConfirmationMessage(string? locale)
    {
        return IsSpanish(locale)
            ? "Para guardar un plan necesito tu confirmacion en la tarjeta recomendada."
            : "To save a plan, confirm it from the recommendation card first.";
    }

    public IReadOnlyList<string> SaveRequiresConfirmationReplies(string? locale)
    {
        return IsSpanish(locale)
            ? ["Confirmar en la tarjeta", "Ver otro plan"]
            : ["Confirm on the card", "Show another plan"];
    }

    public string MinimumPreferencesMissingMessage(string? locale)
    {
        return IsSpanish(locale)
            ? "Antes de proponerte un plan necesito guardar al menos tus intereses, presupuesto y ritmo de viaje."
            : "Before suggesting a plan, I need at least your interests, budget, and travel pace.";
    }

    public IReadOnlyList<string> PreferenceSuggestions(IReadOnlyList<string> missingFields, string? locale)
    {
        var spanish = IsSpanish(locale);
        var suggestions = new List<string>();

        if (missingFields.Contains("interests", StringComparer.OrdinalIgnoreCase))
        {
            suggestions.Add(spanish ? "Guardar intereses" : "Save interests");
        }

        if (missingFields.Contains("budgetLevel", StringComparer.OrdinalIgnoreCase))
        {
            suggestions.Add(spanish ? "Definir presupuesto" : "Set budget");
        }

        if (missingFields.Contains("travelPace", StringComparer.OrdinalIgnoreCase))
        {
            suggestions.Add(spanish ? "Definir ritmo" : "Set pace");
        }

        suggestions.Add(spanish ? "Completar preferencias" : "Complete preferences");
        return suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
    }

    public string NoActiveTripMessage(DateOnly date, string? locale)
    {
        return IsSpanish(locale)
            ? $"No encontre un viaje activo para esa fecha."
            : $"I could not find an active trip for {date:MM/dd}.";
    }

    public IReadOnlyList<string> NoActiveTripReplies(string? locale)
    {
        return IsSpanish(locale)
            ? ["Elegir otra fecha", TravelAssistantCommandText.ViewSchedule]
            : ["Pick another date", "Show my schedule"];
    }

    public string NoRecommendationsMessage(string city, string? locale)
    {
        return IsSpanish(locale)
            ? $"No encontre recomendaciones disponibles para {city}."
            : $"I could not find available recommendations for {city}.";
    }

    public IReadOnlyList<string> NoRecommendationsReplies(string? locale)
    {
        return IsSpanish(locale)
            ? ["Probar otra ciudad", "Ver recomendaciones"]
            : ["Try another city", "View recommendations"];
    }

    public string NoPlanningWindowMessage(DateOnly date, string? locale)
    {
        return IsSpanish(locale)
            ? $"No veo un espacio comodo en tu agenda del {date:dd/MM} para sumar una actividad sin apurarte."
            : $"I do not see a comfortable opening on {date:MM/dd} to add an activity without rushing.";
    }

    public IReadOnlyList<string> NoPlanningWindowReplies(string? locale)
    {
        return IsSpanish(locale)
            ? [TravelAssistantCommandText.ViewSchedule, "Probar otro dia", TravelAssistantCommandText.ViewPreferences]
            : ["Show my schedule", "Try another day", "Show my preferences"];
    }

    public string ScheduleNoActiveTripMessage(DateOnly date, string? locale)
    {
        return IsSpanish(locale)
            ? $"No encontre un viaje activo para el {date:dd/MM}."
            : $"I could not find an active trip for {date:MM/dd}.";
    }

    public IReadOnlyList<string> ScheduleNoActiveTripReplies(string? locale)
    {
        return IsSpanish(locale)
            ? ["Elegir otra fecha", "Proponeme un plan"]
            : ["Pick another date", "Suggest a plan"];
    }

    public string EmptyScheduleMessage(DateOnly date, string destinationName, string? locale)
    {
        return IsSpanish(locale)
            ? $"El {date:dd/MM} no tenes reservas guardadas en {destinationName}. Puedo proponerte un plan libre para anticipar ese dia."
            : $"You do not have saved reservations in {destinationName} on {date:MM/dd}. I can suggest a free-form plan for that day.";
    }

    public IReadOnlyList<string> EmptyScheduleReplies(DateOnly date, string? locale)
    {
        return IsSpanish(locale)
            ? [$"Plan para comer el {date:yyyy-MM-dd}", $"Plan para relajar el {date:yyyy-MM-dd}", TravelAssistantCommandText.ViewPreferences]
            : [$"Plan for food on {date:yyyy-MM-dd}", $"Plan to relax on {date:yyyy-MM-dd}", "Show my preferences"];
    }

    public string ScheduleSummaryMessage(
        DateOnly date,
        string destinationName,
        IReadOnlyList<Reservation> reservations,
        string? locale)
    {
        var lines = reservations
            .Take(5)
            .Select(reservation =>
                $"- {reservation.StartsAt:HH\\:mm}: {reservation.Title} ({reservation.City})")
            .ToList();
        var extra = reservations.Count > 5
            ? IsSpanish(locale)
                ? $" Tambien hay {reservations.Count - 5} reserva(s) mas."
                : $" There are {reservations.Count - 5} more reservation(s)."
            : string.Empty;

        return IsSpanish(locale)
            ? $"Tu agenda del {date:dd/MM} en {destinationName}:\n{string.Join('\n', lines)}{extra}"
            : $"Your {date:MM/dd} schedule in {destinationName}:\n{string.Join('\n', lines)}{extra}";
    }

    public IReadOnlyList<string> ScheduleReplies(DateOnly date, string? locale)
    {
        return IsSpanish(locale)
            ? [$"Plan para comer el {date:yyyy-MM-dd}", TravelAssistantCommandText.RecommendNearby, TravelAssistantCommandText.RecommendByDuration, TravelAssistantCommandText.ViewPreferences]
            : [$"Plan for food on {date:yyyy-MM-dd}", "Recommend nearby", "Recommend by duration", "Show my preferences"];
    }

    public string PreferencesMessage(TravelPreferenceProfileDto profile, string? locale)
    {
        return IsSpanish(locale)
            ? $"Estas son tus preferencias guardadas:\n{FormatPreferenceProfile(profile, locale)}"
            : $"These are your saved preferences:\n{FormatPreferenceProfile(profile, locale)}";
    }

    public IReadOnlyList<string> PreferencesReplies(string? locale)
    {
        return IsSpanish(locale)
            ? ["Cambiar intereses", "Presupuesto bajo", "Ritmo tranquilo", TravelAssistantCommandText.PlanForFood]
            : ["Change interests", "Low budget", "Relaxed pace", "Plan for food"];
    }

    public string PreferenceConfirmationMessage(TravelPreferenceProfilePatchDto patch, string? locale)
    {
        return IsSpanish(locale)
            ? $"Detecte este posible cambio de preferencias:\n{FormatPreferencePatch(patch, locale)}\nQueres guardarlo en tu perfil?"
            : $"I detected this possible preference change:\n{FormatPreferencePatch(patch, locale)}\nDo you want to save it to your profile?";
    }

    public string PreferenceConfirmationMissingMessage(string? locale)
    {
        return IsSpanish(locale)
            ? "Confirma si queres guardar este cambio como preferencia permanente."
            : "Confirm whether you want to save this as a permanent preference.";
    }

    public IReadOnlyList<string> PreferenceConfirmationReplies(string? locale)
    {
        return IsSpanish(locale)
            ? ["Si, guardar preferencia", "No, solo este pedido"]
            : ["Yes, save preference", "No, just this request"];
    }

    public string PreferenceConfirmedMessage(TravelPreferenceProfileDto profile, string? locale)
    {
        return IsSpanish(locale)
            ? $"Listo, actualice tus preferencias:\n{FormatPreferenceProfile(profile, locale)}"
            : $"Done, I updated your preferences:\n{FormatPreferenceProfile(profile, locale)}";
    }

    public string PreferenceRejectedMessage(string? locale)
    {
        return IsSpanish(locale)
            ? "Ok, no modifique tus preferencias."
            : "Ok, I did not change your preferences.";
    }

    public IReadOnlyList<string> PreferenceAfterChangeReplies(string? locale)
    {
        return IsSpanish(locale)
            ? [TravelAssistantCommandText.ViewPreferences, "Proponeme un plan"]
            : ["Show my preferences", "Suggest a plan"];
    }

    public string AssistantPlanMessage(
        string city,
        (TimeOnly Start, TimeOnly End, int AvailableMinutes) planningWindow,
        IReadOnlyList<ScoredRecommendation> ranked,
        string responseMode,
        string? locale)
    {
        if (ranked.Count == 0)
        {
            return IsSpanish(locale)
                ? $"Tenes {planningWindow.AvailableMinutes} minutos libres en {city}, pero no encontre una opcion clara para ese pedido."
                : $"You have {planningWindow.AvailableMinutes} free minutes in {city}, but I did not find a clear option for that request.";
        }

        var top = ranked[0];
        var prefix = responseMode switch
        {
            TravelChatResponseModes.LessWalking => IsSpanish(locale) ? "Busque una opcion con menos caminata" : "I looked for a lower-walking option",
            TravelChatResponseModes.Shorter => IsSpanish(locale) ? "Busque una opcion mas corta" : "I looked for a shorter option",
            TravelChatResponseModes.Food => IsSpanish(locale) ? "Busque algo de comida local" : "I looked for local food",
            TravelChatResponseModes.Culture => IsSpanish(locale) ? "Busque una opcion mas cultural" : "I looked for a more cultural option",
            TravelChatResponseModes.Cheaper => IsSpanish(locale) ? "Busque una opcion de bajo costo" : "I looked for a low-cost option",
            TravelChatResponseModes.MediumCost => IsSpanish(locale) ? "Busque una opcion de coste medio" : "I looked for a medium-cost option",
            TravelChatResponseModes.HighCost => IsSpanish(locale) ? "Busque una opcion premium" : "I looked for a premium option",
            _ => IsSpanish(locale) ? "Te propongo este plan" : "I suggest this plan"
        };
        var walking = top.WalkingMinutes.HasValue
            ? IsSpanish(locale)
                ? $" Queda a unos {top.WalkingMinutes.Value} min caminando."
                : $" It is about {top.WalkingMinutes.Value} min on foot."
            : string.Empty;

        return IsSpanish(locale)
            ? $"{prefix} para tu ventana de {planningWindow.Start:HH\\:mm} a {planningWindow.End:HH\\:mm} en {city}: {top.Recommendation.Title}. {top.PositiveReasons.First()}{walking}"
            : $"{prefix} for your {planningWindow.Start:HH\\:mm}-{planningWindow.End:HH\\:mm} window in {city}: {top.Recommendation.Title}. {top.PositiveReasons.First()}{walking}";
    }

    public IReadOnlyList<string> SuggestedReplies(string responseMode, string? locale)
    {
        if (!IsSpanish(locale))
        {
            return responseMode switch
            {
                TravelChatResponseModes.LessWalking => ["Recommend by duration", "Plan for food", "Show my schedule", "What can I ask"],
                TravelChatResponseModes.Shorter => ["Recommend nearby", "Plan for food", "Show my schedule", "Another option"],
                TravelChatResponseModes.Food => ["Recommend nearby", "Plan to relax", "Recommend by duration", "What can I ask"],
                TravelChatResponseModes.Culture => ["Plan for food", "Recommend nearby", "Recommend by duration", "Show my schedule"],
                TravelChatResponseModes.Cheaper => ["Medium cost", "Premium option", "Recommend nearby", "Show my schedule"],
                TravelChatResponseModes.MediumCost => ["Low cost", "Premium option", "Recommend nearby", "Show my schedule"],
                TravelChatResponseModes.HighCost => ["Low cost", "Medium cost", "Recommend nearby", "Show my schedule"],
                _ => ["Plan for food", "Night plan", "Couple plan", "Recommend nearby"]
            };
        }

        return responseMode switch
        {
            TravelChatResponseModes.LessWalking => [TravelAssistantCommandText.RecommendByDuration, TravelAssistantCommandText.PlanForFood, TravelAssistantCommandText.ViewSchedule, TravelAssistantCommandText.HelpCapabilities],
            TravelChatResponseModes.Shorter => [TravelAssistantCommandText.RecommendNearby, TravelAssistantCommandText.PlanForFood, TravelAssistantCommandText.ViewSchedule, TravelAssistantCommandText.OtherOption],
            TravelChatResponseModes.Food => [TravelAssistantCommandText.RecommendNearby, TravelAssistantCommandText.PlanForRelax, TravelAssistantCommandText.RecommendByDuration, TravelAssistantCommandText.HelpCapabilities],
            TravelChatResponseModes.Culture => [TravelAssistantCommandText.PlanForFood, TravelAssistantCommandText.RecommendNearby, TravelAssistantCommandText.RecommendByDuration, TravelAssistantCommandText.ViewSchedule],
            TravelChatResponseModes.Cheaper => ["Coste medio", "Algo premium", TravelAssistantCommandText.RecommendNearby, TravelAssistantCommandText.ViewSchedule],
            TravelChatResponseModes.MediumCost => ["Coste bajo", "Algo premium", TravelAssistantCommandText.RecommendNearby, TravelAssistantCommandText.ViewSchedule],
            TravelChatResponseModes.HighCost => ["Coste bajo", "Coste medio", TravelAssistantCommandText.RecommendNearby, TravelAssistantCommandText.ViewSchedule],
            _ => [TravelAssistantCommandText.PlanForFood, TravelAssistantCommandText.PlanForNight, TravelAssistantCommandText.PlanForCouple, TravelAssistantCommandText.RecommendNearby]
        };
    }

    private string FormatPreferencePatch(TravelPreferenceProfilePatchDto patch, string? locale)
    {
        var lines = new List<string>();
        var spanish = IsSpanish(locale);

        if (patch.Interests is { Count: > 0 })
        {
            lines.Add($"{Bullet()} {(spanish ? "Intereses" : "Interests")}: {FormatList(patch.Interests, locale)}");
        }

        if (patch.Dislikes is { Count: > 0 })
        {
            lines.Add($"{Bullet()} {(spanish ? "Evitar" : "Avoid")}: {FormatList(patch.Dislikes, locale)}");
        }

        if (patch.FoodPreferences is { Count: > 0 })
        {
            lines.Add($"{Bullet()} {(spanish ? "Comida" : "Food")}: {FormatList(patch.FoodPreferences, locale)}");
        }

        if (patch.DietaryRestrictions is { Count: > 0 })
        {
            lines.Add($"{Bullet()} {(spanish ? "Requisitos alimentarios" : "Dietary needs")}: {FormatList(patch.DietaryRestrictions, locale)}");
        }

        if (!string.IsNullOrWhiteSpace(patch.BudgetLevel))
        {
            lines.Add($"{Bullet()} {(spanish ? "Presupuesto" : "Budget")}: {patch.BudgetLevel}");
        }

        if (!string.IsNullOrWhiteSpace(patch.TravelPace))
        {
            lines.Add($"{Bullet()} {(spanish ? "Ritmo" : "Pace")}: {patch.TravelPace}");
        }

        if (patch.MaxWalkingMinutes.HasValue)
        {
            lines.Add($"{Bullet()} {(spanish ? "Caminata maxima" : "Max walking")}: {patch.MaxWalkingMinutes.Value} min");
        }

        return lines.Count == 0
            ? $"{Bullet()} {(spanish ? "Sin cambios detectados" : "No changes detected")}"
            : string.Join('\n', lines);
    }

    private string FormatPreferenceProfile(TravelPreferenceProfileDto profile, string? locale)
    {
        var spanish = IsSpanish(locale);
        return string.Join(
            '\n',
            [
                $"{Bullet()} {(spanish ? "Intereses" : "Interests")}: {FormatList(profile.Interests, locale)}",
                $"{Bullet()} {(spanish ? "Comida" : "Food")}: {FormatList(profile.FoodPreferences, locale)}",
                $"{Bullet()} {(spanish ? "Restricciones" : "Restrictions")}: {FormatList(profile.DietaryRestrictions, locale)}",
                $"{Bullet()} {(spanish ? "Presupuesto" : "Budget")}: {profile.BudgetLevel}",
                $"{Bullet()} {(spanish ? "Ritmo" : "Pace")}: {profile.TravelPace}",
                $"{Bullet()} {(spanish ? "Max. caminata" : "Max walking")}: {profile.MaxWalkingMinutes} min",
                $"{Bullet()} {(spanish ? "Evitar" : "Avoid")}: {FormatList(profile.Dislikes, locale)}"
            ]);
    }

    private string FormatList(IReadOnlyList<string> values, string? locale)
    {
        return values.Count == 0
            ? IsSpanish(locale) ? "sin definir" : "not set"
            : string.Join(", ", values);
    }

    private static string Bullet() => "-";
}

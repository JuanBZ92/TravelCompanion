using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TravelCompanion.Api.Services;

public interface ITravelChatIntentClassifier
{
    TravelChatIntentResult Classify(string? message);
}

public sealed record TravelChatIntentResult(
    string Intent,
    double Confidence,
    string ResponseMode,
    IReadOnlyList<string> MatchedSignals,
    bool HasPlanningSignal)
{
    public bool IsPlanning => Intent == TravelChatIntents.Plan || HasPlanningSignal;
    public bool IsSupported => Intent != TravelChatIntents.Unsupported;
}

public static class TravelChatIntents
{
    public const string Plan = "plan_between_reservations";
    public const string ViewSchedule = "view_schedule";
    public const string ViewPreferences = "view_preferences";
    public const string SaveItinerary = "save_itinerary";
    public const string Help = "help";
    public const string Unsupported = "unsupported";
}

public static class TravelChatResponseModes
{
    public const string LessWalking = "less_walking";
    public const string Shorter = "shorter";
    public const string Food = "food";
    public const string Culture = "culture";
    public const string Cheaper = "cheaper";
    public const string MediumCost = "medium_cost";
    public const string HighCost = "high_cost";
    public const string Balanced = "balanced";
}

public sealed partial class TravelChatIntentClassifier : ITravelChatIntentClassifier
{
    public TravelChatIntentResult Classify(string? message)
    {
        var normalized = Normalize(message);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new TravelChatIntentResult(
                TravelChatIntents.Unsupported,
                0,
                TravelChatResponseModes.Balanced,
                [],
                HasPlanningSignal: false);
        }

        var responseMode = ResolveResponseMode(normalized);
        var scores = new Dictionary<string, IntentScore>(StringComparer.Ordinal)
        {
            [TravelChatIntents.SaveItinerary] = ScoreSave(normalized),
            [TravelChatIntents.Help] = ScoreHelp(normalized),
            [TravelChatIntents.ViewSchedule] = ScoreSchedule(normalized),
            [TravelChatIntents.ViewPreferences] = ScorePreferences(normalized),
            [TravelChatIntents.Plan] = ScorePlanning(normalized)
        };

        var best = scores
            .OrderByDescending(score => score.Value.Score)
            .ThenByDescending(score => score.Key == TravelChatIntents.Plan ? 1 : 0)
            .First();

        if (best.Value.Score <= 0)
        {
            return new TravelChatIntentResult(
                TravelChatIntents.Unsupported,
                0,
                responseMode,
                [],
                HasPlanningSignal: false);
        }

        return new TravelChatIntentResult(
            best.Key,
            Math.Min(1, best.Value.Score / 8d),
            responseMode,
            best.Value.Signals,
            HasPlanningSignal: scores[TravelChatIntents.Plan].Score >= 4);
    }

    public static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var normalized = message.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (character == '#')
            {
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    private static IntentScore ScoreSave(string normalized)
    {
        var score = new IntentScore();
        score.AddIf(ContainsAny(normalized, "guardar plan", "guardar este plan", "guarda el plan", "guarda este plan", "guardar itinerario"), 8, "save_explicit");
        score.AddIf(ContainsAny(normalized, "guardalo", "guardarlo", "guardar esto", "save plan", "save itinerary"), 7, "save_short");
        score.AddIf(
            ContainsApproximateToken(normalized, 2, "guardar", "guardalo", "guardarlo")
            && ContainsAny(normalized, "plan", "itinerario", "esto"),
            8,
            "save_typo");
        return score;
    }

    private static IntentScore ScoreHelp(string normalized)
    {
        var score = new IntentScore();
        score.AddIf(ContainsAny(normalized, "que puedo pedirte", "que puedes hacer", "que podes hacer"), 8, "help_capabilities");
        score.AddIf(ContainsAny(normalized, "ayuda", "comandos", "help", "what can i ask", "what can you do"), 7, "help_keyword");
        score.AddIf(ContainsApproximateToken(normalized, 1, "ayuda"), 4, "help_typo");
        return score;
    }

    private static IntentScore ScoreSchedule(string normalized)
    {
        var score = new IntentScore();
        var hasExactScheduleSignal = ContainsAny(normalized, "ver mi agenda", "ver agenda", "mi agenda", "agenda", "show my schedule", "my schedule");
        var hasExactReservationSignal = ContainsAny(normalized, "ver mis reservas", "mostrar mis reservas", "mis reservas", "reservas", "show my reservations", "my reservations");
        var hasExactScheduleKeyword = ContainsAny(normalized, "schedule", "schedules", "itinerary", "itinerario", "que tengo", "what do i have");
        score.AddIf(hasExactScheduleSignal, 8, "schedule_agenda");
        score.AddIf(hasExactReservationSignal, 8, "schedule_reservations");
        score.AddIf(hasExactScheduleKeyword, 7, "schedule_keyword");
        score.AddIf(
            !hasExactScheduleSignal
            && !hasExactReservationSignal
            && !hasExactScheduleKeyword
            && ContainsApproximateToken(normalized, 2, "agenda", "reservas", "schedule", "itinerario"),
            4,
            "schedule_typo");
        return score;
    }

    private static IntentScore ScorePreferences(string normalized)
    {
        var score = new IntentScore();
        score.AddIf(ContainsAny(normalized, "preferencias", "mis gustos", "mi perfil", "my preferences", "show my preferences", "preferences", "my profile", "profile"), 8, "preferences_view");
        score.AddIf(ContainsAny(normalized, "cambiar presupuesto", "cambia presupuesto", "actualizar presupuesto", "actualiza presupuesto", "mi presupuesto"), 8, "preferences_budget");
        score.AddIf(ContainsAny(normalized, "cambiar ritmo", "cambia ritmo", "actualizar ritmo", "actualiza ritmo", "mi ritmo"), 8, "preferences_pace");
        score.AddIf(ContainsAny(normalized, "cambiar intereses", "cambia intereses", "actualizar intereses", "actualiza intereses", "mis intereses"), 8, "preferences_interests");
        score.AddIf(ContainsAny(normalized, "evita", "evite", "evitando", "evitar", "avoid"), 12, "preferences_avoid_signal");
        score.AddIf(ContainsAny(normalized, "prefiero", "me gusta", "no me gusta", "no quiero", "i prefer", "i like", "i dislike", "i do not want"), 7, "preferences_preference_signal");
        score.AddIf(ContainsAny(normalized, "soy vegetariano", "soy vegetariana", "soy celiaco", "soy celiaca", "tengo celiaquia", "sin gluten"), 7, "preferences_dietary");
        score.AddIf(ContainsApproximateToken(normalized, 2, "preferencias", "presupuesto", "intereses", "vegetariano", "celiaco"), 4, "preferences_typo");
        return score;
    }

    private static IntentScore ScorePlanning(string normalized)
    {
        var score = new IntentScore();
        score.AddIf(ContainsAny(normalized, "plan", "planes"), 4, "plan_keyword");
        score.AddIf(ContainsAny(normalized, "actividad", "actividades", "algo para hacer", "algo que hacer", "que hago", "que puedo hacer"), 4, "activity_keyword");
        score.AddIf(
            ContainsAny(normalized, "propon", "propron", "recomend", "suger")
            || ContainsApproximateToken(normalized, 2, "propon", "recomendar", "recomenda", "recomienda", "sugerir", "sugeri", "sugier"),
            4,
            "proposal_verb");
        var hasRequestVerb = ContainsAny(normalized, "dame", "dime", "decime", "quiero", "necesito", "me gustaria", "armame", "arma", "crea", "creame", "fabrica", "fabricame", "prepara", "preparame", "give me", "tell me", "i want", "i need", "suggest", "recommend", "make me", "create", "prepare");
        var hasPlanningObject = ContainsAny(normalized, "plan", "planes", "actividad", "actividades", "algo", "opcion", "dia", "hoy", "manana", "fecha", "hacer", "paseo", "itinerario");
        var hasPlanTopic = ContainsAny(
            normalized,
            "comida",
            "food",
            "cultura",
            "culture",
            "museo",
            "historia",
            "caminar",
            "caminata",
            "paseo",
            "pareja",
            "cita",
            "romantico",
            "romantico",
            "nocturno",
            "noche",
            "bailar",
            "baile",
            "relax",
            "relaxing",
            "couple",
            "date",
            "night",
            "dance");
        var hasCostTopic = ContainsCostRequest(normalized);
        var hasCostAdjustment = ContainsAny(normalized, "barato", "gratis", "economico", "coste bajo", "costo bajo", "coste medio", "costo medio", "coste alto", "costo alto", "premium", "caro");
        score.AddIf(hasRequestVerb && hasPlanningObject, 3, "request_verb");
        score.AddIf(hasRequestVerb && hasPlanTopic, 6, "request_topic");
        score.AddIf(hasRequestVerb && hasCostTopic, 6, "request_cost");
        score.AddIf(
            ContainsAny(
                normalized,
                "menos caminata",
                "caminar menos",
                "mas corto",
                "otra opcion",
                "otra alternativa",
                "reemplazar",
                "algo distinto",
                "por cercania",
                "por cercanía",
                "por duracion",
                "por duración",
                "recomendar por cercania",
                "recomendar por duracion",
                "recommend nearby",
                "recommend by duration",
                "another option",
                "show another plan",
                "try another day"),
            4,
            "plan_adjustment");
        score.AddIf(hasCostAdjustment && (!LooksLikePreferenceBudgetUpdate(normalized) || hasRequestVerb), 4, "cost_adjustment");
        score.AddIf(hasPlanTopic, 2, "plan_topic");
        score.AddIf(hasCostTopic, 2, "cost_topic");
        return score;
    }

    private static string ResolveResponseMode(string normalized)
    {
        if (ContainsAny(normalized, "menos caminata", "caminar menos", "poca caminata", "cerca", "cercania", "nearby", "close", "less walking", "relajar", "relajado", "tranquilo", "relax", "relaxing", "easy pace"))
        {
            return TravelChatResponseModes.LessWalking;
        }

        if (ContainsAny(normalized, "mas corto", "rapido", "poco tiempo", "corta", "duracion", "duración", "shorter", "quick", "less time", "duration"))
        {
            return TravelChatResponseModes.Shorter;
        }

        if (ContainsAny(normalized, "coste bajo", "costo bajo", "presupuesto bajo", "bajo coste", "bajo costo", "barato", "gratis", "economico", "free", "low cost", "cheap", "budget"))
        {
            return TravelChatResponseModes.Cheaper;
        }

        if (ContainsAny(normalized, "coste medio", "costo medio", "presupuesto medio", "precio medio", "moderado", "medium cost", "moderate", "mid range"))
        {
            return TravelChatResponseModes.MediumCost;
        }

        if (ContainsAny(normalized, "coste alto", "costo alto", "presupuesto alto", "precio alto", "caro", "premium", "alta gama", "high cost", "expensive", "upscale"))
        {
            return TravelChatResponseModes.HighCost;
        }

        if (ContainsAny(normalized, "comida", "comer", "almorzar", "cenar", "food", "local", "snack", "restaurante", "cafe"))
        {
            return TravelChatResponseModes.Food;
        }

        if (ContainsAny(normalized, "cultura", "cultural", "culture", "museo", "historia", "arte"))
        {
            return TravelChatResponseModes.Culture;
        }

        return TravelChatResponseModes.Balanced;
    }

    private static bool ContainsCostRequest(string normalized)
    {
        return ContainsAny(
            normalized,
            "coste",
            "costo",
            "precio",
            "presupuesto",
            "barato",
            "gratis",
            "economico",
            "premium",
            "caro",
            "alta gama");
    }

    private static bool LooksLikePreferenceBudgetUpdate(string normalized)
    {
        return ContainsAny(
            normalized,
            "prefiero",
            "preferencia",
            "preferencias",
            "mi presupuesto",
            "cambiar presupuesto",
            "cambia presupuesto",
            "actualizar presupuesto",
            "actualiza presupuesto");
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsApproximateToken(string value, int maxDistance, params string[] candidates)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(token => candidates.Any(candidate => IsApproximateTokenMatch(token, candidate, maxDistance)));
    }

    private static bool IsApproximateTokenMatch(string token, string candidate, int maxDistance)
    {
        if (token.Length < 5 || candidate.Length < 5)
        {
            return false;
        }

        if (token.Contains(candidate, StringComparison.OrdinalIgnoreCase)
            || candidate.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Math.Abs(token.Length - candidate.Length) > maxDistance)
        {
            return false;
        }

        return CalculateEditDistance(token, candidate) <= maxDistance;
    }

    private static int CalculateEditDistance(string left, string right)
    {
        var distances = new int[left.Length + 1, right.Length + 1];
        for (var i = 0; i <= left.Length; i++)
        {
            distances[i, 0] = i;
        }

        for (var j = 0; j <= right.Length; j++)
        {
            distances[0, j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[left.Length, right.Length];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private sealed class IntentScore
    {
        private readonly List<string> _signals = [];

        public int Score { get; private set; }
        public IReadOnlyList<string> Signals => _signals;

        public void AddIf(bool condition, int points, string signal)
        {
            if (!condition)
            {
                return;
            }

            Score += points;
            _signals.Add(signal);
        }
    }
}

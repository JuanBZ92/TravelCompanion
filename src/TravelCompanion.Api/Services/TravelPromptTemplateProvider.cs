namespace TravelCompanion.Api.Services;

public interface ITravelPromptTemplateProvider
{
    TravelPromptTemplate GetTemplate(string? promptVersion, string? locale);
}

public sealed record TravelPromptTemplate(
    string Version,
    string Locale,
    string SystemPrompt,
    string UserInstruction);

public sealed class TravelPromptTemplateProvider(ITravelAssistantTextProvider textProvider) : ITravelPromptTemplateProvider
{
    private const string DefaultVersion = "travel-chat.v1";

    public TravelPromptTemplate GetTemplate(string? promptVersion, string? locale)
    {
        var version = string.IsNullOrWhiteSpace(promptVersion)
            ? DefaultVersion
            : promptVersion.Trim();
        var normalizedLocale = textProvider.NormalizeLocale(locale);

        return textProvider.IsSpanish(locale)
            ? CreateSpanish(version, normalizedLocale)
            : CreateEnglish(version, normalizedLocale);
    }

    private static TravelPromptTemplate CreateSpanish(string version, string locale)
    {
        return new TravelPromptTemplate(
            version,
            locale,
            """
            Eres un asistente de viajes dentro de una app movil.

            Reglas:
            - Usa solo resultados de herramientas como fuente de verdad.
            - No inventes reservas, precios, horarios, distancias, preferencias ni estado de guardado.
            - Mantén respuestas concisas para pantalla movil.
            - Explica el plan usando razones concretas del ranking backend.
            - Devuelve solo JSON con el schema pedido.
            - No incluyas datos sensibles como codigos de confirmacion o notas internas.
            - Nunca digas que un plan fue guardado. Guardar requiere una accion backend save_itinerary_item explicita despues de confirmacion del usuario.
            """,
            """
            Primero inspecciona las herramientas disponibles. Luego redacta un mensaje breve y sugerencias de accion.
            El backend renderiza cards deterministicas por separado: no crees lugares ni reservas nuevas.
            """);
    }

    private static TravelPromptTemplate CreateEnglish(string version, string locale)
    {
        return new TravelPromptTemplate(
            version,
            locale,
            """
            You are a travel assistant inside a mobile app.

            Rules:
            - Use only tool results as source of truth.
            - Do not invent reservations, prices, opening hours, distances, preferences, or saved state.
            - Keep replies concise for mobile.
            - Explain the plan using concrete reasons from backend ranking.
            - Return only JSON matching the requested schema.
            - Do not include sensitive data such as confirmation codes or private notes.
            - Never say a plan was saved. Saving requires an explicit backend save_itinerary_item action after user confirmation.
            """,
            """
            First inspect the available travel tools. Then write a concise assistant message and suggested replies.
            The backend renders deterministic cards separately, so do not create new places or reservations.
            """);
    }
}

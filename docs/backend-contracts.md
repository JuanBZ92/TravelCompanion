# Backend Contracts

## Endpoint

```http
POST /api/ai/travel-chat
```

## Request

```json
{
  "message": "Qué puedo hacer antes de mi reserva?",
  "conversationId": "abc123",
  "city": "Rome",
  "date": "2026-09-12",
  "currentLocation": {
    "latitude": 41.9028,
    "longitude": 12.4964
  },
  "locale": "es-ES"
}
```

## Response

```json
{
  "conversationId": "abc123",
  "message": "Tenés 2 horas libres antes de la cena. Te recomiendo una caminata corta y un aperitivo cerca.",
  "intent": "plan_between_reservations",
  "cards": [
    {
      "type": "recommendation",
      "title": "Aperitivo cerca del restaurante",
      "subtitle": "15 minutos caminando",
      "description": "Una opción liviana antes de la cena.",
      "startTime": "18:30",
      "endTime": "19:30",
      "estimatedCost": "medium",
      "distanceKm": 1.1,
      "walkingMinutes": 15,
      "whyItFits": [
        "Está cerca de tu próxima reserva",
        "Encaja con tu presupuesto medio",
        "No exige caminar demasiado"
      ],
      "warnings": [],
      "recommendationId": "rec_123",
      "reservationId": null
    }
  ],
  "suggestedReplies": [
    "Algo más barato",
    "Algo con menos caminata",
    "Guardar este plan"
  ],
  "missingContext": null
}
```

If the authenticated user does not have minimum preference context, the chat returns no cards and sets `missingContext`:

```json
{
  "conversationId": "abc123",
  "message": "Antes de proponerte un plan necesito guardar al menos tus intereses, presupuesto y ritmo de viaje.",
  "intent": "plan_between_reservations",
  "cards": [],
  "suggestedReplies": ["Guardar intereses", "Definir presupuesto", "Definir ritmo", "Completar preferencias"],
  "missingContext": {
    "field": "preferences",
    "message": "Antes de proponerte un plan necesito guardar al menos tus intereses, presupuesto y ritmo de viaje.",
    "suggestions": ["Guardar intereses", "Definir presupuesto", "Definir ritmo", "Completar preferencias"]
  }
}
```

Saving a plan is not performed by chat text. The MAUI app must ask the user for confirmation and then call the explicit backend action:

```http
POST /api/ai/save_itinerary_item
```

```json
{
  "recommendationId": "33333333-3333-3333-3333-333333333301",
  "date": "2026-10-06",
  "startsAt": "11:00:00",
  "endsAt": "12:30:00"
}
```

The model must not say an itinerary item was saved. Only a successful `SaveItineraryItemResponse.saved = true` confirms the save.

Preference profile endpoints:

```http
GET /api/me/travel-preference-profile
PATCH /api/me/travel-preference-profile
```

`PATCH` accepts partial `TravelPreferenceProfilePatchDto` values for interests, food preferences, dietary restrictions, budget level, travel pace, dislikes, tourist-trap avoidance, and max walking minutes.

The chat endpoint also handles deterministic assistant intents without asking the model:

- `view_schedule`: messages such as `Ver mi agenda` return a schedule summary for the requested date.
- `view_preferences`: messages such as `Ver mis preferencias` return the current preference profile.
- `update_preferences`: explicit preference edits such as `Prefiero presupuesto bajo y ritmo tranquilo` or `evitar culture` first return a confirmation prompt. The backend stores the pending patch on the chat conversation and only persists it after an affirmative reply. If the user rejects the change, the pending patch is cleared; planning requests can still use the detected preference as one-off context without modifying the profile.
- `help`: messages such as `Que puedo pedirte` return guided assistant actions using the same five-part mental model as the MAUI UI: `Planificar`, `Ajustar`, `Agenda`, `Preferencias`, and `Ayuda`. This does not require a completed preference profile.
- Unsupported free text returns `missingContext.field = assistantCommand` with guided suggestions instead of treating the assistant as an open chat.

Date requests inside chat text are supported for planning prompts, for example `Proponeme planes para 2026-10-08`, `Proponeme planes para el 8 de octubre`, or `Proponeme planes para mañana`. The backend resolves that date before loading schedules and recommendations.

Card actions can include a `recommendationId` in the chat text, for example `Reemplazar 33333333-3333-3333-3333-333333333301` or `Algo con menos caminata que 33333333-3333-3333-3333-333333333301`. The backend excludes that recommendation when ranking the next response if alternatives are available.

## Suggested C# DTOs

```csharp
public sealed record TravelChatRequest(
    string Message,
    string? ConversationId,
    string? City,
    DateOnly? Date,
    GeoPointDto? CurrentLocation,
    string? Locale
);

public sealed record TravelChatResponse(
    string ConversationId,
    string Message,
    string Intent,
    IReadOnlyList<TravelCardDto> Cards,
    IReadOnlyList<string> SuggestedReplies,
    MissingContextDto? MissingContext
);

public sealed record TravelCardDto(
    string Type,
    string Title,
    string? Subtitle,
    string? Description,
    string? StartTime,
    string? EndTime,
    string? EstimatedCost,
    double? DistanceKm,
    int? WalkingMinutes,
    IReadOnlyList<string> WhyItFits,
    IReadOnlyList<string> Warnings,
    string? RecommendationId,
    string? ReservationId)
{
    public IReadOnlyList<string> Tags { get; init; } = [];
}
```

## Server-side AI configuration

The MAUI app must keep calling only `POST /api/ai/travel-chat`. OpenAI is configured only in the backend:

```json
{
  "OpenAI": {
    "Enabled": true,
    "Model": "gpt-4o-mini",
    "MaxOutputTokenCount": 500
  }
}
```

Do not store `OpenAI:ApiKey` in source control. For local development, use user secrets from the API project:

```powershell
dotnet user-secrets set "OpenAI:ApiKey" "<server-side-api-key>" --project src\TravelCompanion.Api\TravelCompanion.Api.csproj
```

For deployed environments, provide the same key through the platform secret store or an environment variable such as `OpenAI__ApiKey`.

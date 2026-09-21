# Backend Contracts

## Production-Critical Endpoints

These endpoints should be covered by smoke tests and contract regression tests before production deploys:

- `GET /health`: public liveness check.
- `POST /api/auth/login`: mobile session creation.
- `GET /api/destinations`: public destination catalog.
- `GET /api/recommendations`: authenticated trip-session recommendation catalog.
- `GET /api/recommendations/tags`: authenticated trip-session canonical tag catalog.
- `GET /api/mobile/free-map/cities`: cities enabled for a `FreeMapPreview` session.
- `GET /api/mobile/free-map/{citySlug}`: full markers inside the configured radius and redacted markers outside it.
- `GET /api/packages`: public/package-aware package catalog.
- `GET /api/mobile/bootstrap`: authenticated mobile bootstrap payload.
- `GET /api/mobile/discover`: authenticated discover payload.
- `GET /api/mobile/recommendations/{id}`: authenticated recommendation detail payload.
- `GET /api/me/schedule`: authenticated user schedule.
- `GET /api/me/travel-preference-profile`: authenticated preference profile read.
- `PATCH /api/me/travel-preference-profile`: authenticated preference profile update.
- `POST /api/ai/travel-chat`: authenticated assistant entry point.
- `POST /api/ai/save-itinerary-item`: authenticated itinerary save action.

Smoke testing is implemented in `scripts/SmokeTest-Api.ps1`.

## Public Recommendations Endpoint

```http
GET /api/recommendations?destinationSlug=japon&page=1&pageSize=50
```

Returns only recommendations that are safe for anonymous/public catalog use:

- `accessLevel = Free`
- no package association

Paid, subscription, package-bound, and admin-only recommendations must be requested through authenticated mobile/API flows that validate user entitlements server-side.

## Mobile Recommendation Detail Endpoint

```http
GET /api/mobile/recommendations/{id}
Authorization: Bearer <token>
```

Returns the full recommendation payload only when the authenticated user is entitled to that recommendation. Locked or missing recommendations return `404` so the API does not reveal whether protected content exists.

`GET /api/mobile/discover` and `GET /api/mobile/bootstrap` return recommendation summaries for list/map rendering. The full editorial description should be loaded from this detail endpoint when the user opens Details.

## Recommendation Tags Endpoint

```http
GET /api/recommendations/tags?destinationSlug=japon
```

Returns the canonical tag catalog generated from recommendation categories and tags:

```json
[
  {
    "tag": "culture",
    "displayName": "Culture",
    "aliases": ["cultura", "cultural"],
    "recommendationCount": 12,
    "isCategory": true
  }
]
```

The assistant uses the same catalog to resolve preference edits such as `evitar cultura`, `sin baños termales`, or `avoid history` into canonical `Dislikes` values.

## Travel Chat Endpoint

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
  "locale": "es-ES",
  "guidedAction": { "action": "recommend" },
  "criteria": {
    "category": "food",
    "priority": "budget",
    "budget": "low"
  }
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
  "missingContext": null,
  "guidedQuestion": null,
  "criteria": {
    "category": "food",
    "priority": "budget",
    "budget": "low"
  }
}
```

Guided actions and options use stable codes rather than localized labels. Categories are `food`, `relax`, `culture`, `walk`, `dance`, `nature`, `shopping`, `viewpoint`, and `nightlife`. The mobile flow asks for category, budget, and maximum walking distance in that order. Sending `guidedAction.action = alternative` keeps the criteria and excludes all recommendations already shown in the conversation.

`guidedAction.action = full_day` requests a complete five-slot day. `guidedAction.optionId` is a per-request opaque seed so retries return the same selection while a new request can produce another combination. The response uses distinct real catalog recommendations at 09:00 (coffee), 10:30 (morning visit), 13:00 (lunch), 15:30 (afternoon outing), and 19:30 (dinner). When the accessible catalog cannot fill a suitable slot, the response returns fewer cards and says so instead of inventing a place. Budget, walking distance, access permissions, and the selected interest remain server-enforced.

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

The mobile client renders this `preferences` response as the direct category → budget → distance selector and does not render the same message again as an Assistant bubble.

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
- `help`: messages such as `Que puedo pedirte` describe the assistant capabilities. This does not require a completed preference profile.
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
    string? Locale,
    GuidedTravelActionDto? GuidedAction,
    GuidedPlanCriteriaDto? Criteria
);

public sealed record TravelChatResponse(
    string ConversationId,
    string Message,
    string Intent,
    IReadOnlyList<TravelCardDto> Cards,
    IReadOnlyList<string> SuggestedReplies,
    MissingContextDto? MissingContext,
    GuidedQuestionDto? GuidedQuestion,
    GuidedPlanCriteriaDto? Criteria
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

## Improve day and replace events

`GuidedAction.Action = "full_day"` requires `Date` and ignores saved preferences, conversation filters, and submitted `Criteria`.
It fills missing slots with a morning café, a morning visit, lunch, an afternoon visit, and dinner.
Existing events occupy their corresponding slot; exact reservations and flights block overlapping start times. Lodging does not occupy activity slots.
Recommendations already used in the trip are excluded. An unavailable category stays empty rather than being replaced by an unrelated activity.

A full day returns `Intent = "day_complete"` and `existing_day_stop` cards for editable events. This response does not consume an Assistant result quota.
The client offers multiple selection and sends the selected reservation IDs in `GuidedAction.ReplaceReservationIds`.
Only flexible, traveler-owned events created by Travel Assistant on the requested date can be replaced; confirmed reservations cannot be replaced through this flow. The backend identifies existing assistant saves by their persisted `SourceName` (`Travel Assistant`). Items created from the itinerary's add action or the map are protected, including legacy items marked flexible. The traveler create endpoint sets `FixedByTraveler`; updates preserve assistant origin and existing protection instead of accepting client flexibility changes. The mobile editor no longer exposes a planning-status selector.

Curated premium sessions (including demo PIN 2222) remain read-only. They cannot add, edit, delete, or save replacements through the API; the mobile app hides itinerary add actions, map add actions, and Improve Day for these sessions.
Batch replacement leaves both adjustment fields unset and does not use preferences.

For one event, optional `DistanceAdjustment` accepts `closer` or `farther`, and optional `BudgetAdjustment` accepts `cheaper` or `dearer`.
Both filters apply together. Distance compares the longest known transfer to the immediately preceding and following plans, and price compares known catalog price levels against the original event.
With both fields omitted, the server selects a random compatible alternative. Missing reference coordinates, unknown prices, or no matching candidates never relax an explicitly selected constraint.

Day-plan cards set `IsDayPlan`. Replacement cards carry the original `ReservationId` and `ReplacesRecommendationId`.
Transfers exceeding 2 km in a straight line set `HasLongTransfer` and include a warning naming the adjacent plan; these are estimates, not route distances.
The client offers a closer replacement from that warning.

The existing itinerary-save request accepts optional `ReplaceReservationId` and `ExpectedRecommendationId`.
The server validates ownership, date, editability, catalog access, and the expected original recommendation before replacing the event in place.
Its ID, ordering, and start time remain stable. Each replacement saves atomically; the client reports partial batch success and keeps failed alternatives available for retry.
`ClientMutationId` remains stable across retries of one card. Existing clients can omit the new fields.

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

### Full-day proximity and selected replacements

`GuidedAction` with `Action = full_day` retains backward compatibility. Eligible recommendations still pass catalog/free access filters and exclude recommendations already used on the trip. Selection uses seeded randomness unless the user explicitly requests `closer`; that request filters for an improvement and prioritizes the shortest adjacent transfer within the existing city/category preferences.

`ReplaceReservationIds` selects editable events. Optional `DistanceAdjustment` (`closer`, `farther`) and `BudgetAdjustment` (`cheaper`, `dearer`) apply together to each selected event. Distance compares adjacent events; budget compares known price levels. No compatible alternative leaves that event unchanged. Mobile exposes these options together on the selection screen for both single and multiple replacements.

Unsaved suggestions can request an alternative with `GuidedAction.RecommendationId` and `DraftDayStops`, a maximum of five unique `{ recommendationId, startsAt, reservationId? }` entries from the displayed day. The target must appear in that context. All catalog information, coordinates and prices are resolved by the backend using the same access-filtered candidates; draft IDs cannot bypass catalog access. Optional reservation IDs must identify editable events owned by the user on that day and at that time. Saved-event replacements can also include draft neighbours for distance context, without setting `RecommendationId`.

The response contains only the proposed replacement. Draft recommendations are excluded from alternatives. A pending replacement of a saved event retains its original `ReservationId` and `ReplacesRecommendationId` for save-time concurrency checks. Searching never writes itinerary reservations. The mobile app replaces the displayed card in place; the existing save endpoint commits it only when the user chooses **Save to Today** or **Save change to Today**. No alternative or a failed request preserves the current card.

Morning visits and afternoon stops prioritize non-food candidates. If none remain after access, duplicate and explicit replacement filters, Food candidates are eligible as fallback. This uses the same authorized candidate pool: Free stays within its configured radius, while paid access retains its catalog permissions. Period-only morning visits at 10:30 remain morning visits even when their recommendation is Food.

Free map previews include all catalog markers assigned to the selected city, including those beyond CoverageRadiusKm. Only markers inside FreeRadiusKm carry recommendation details; outside markers retain opaque keys and approximate coordinates. Free builder sessions use this preview map in the main Map tab and offer the pass for locked selections.


### Reservation reminders

`GET /api/notifications/reminders?locale=es|en` requires a traveler session (including Free). Returns `ReservationReminderDto[]` for the selected owned published non-archived trip, sorted by UTC delivery time, maximum 60. Each entry contains stable `id`, `reservationId`, `notifyAtUtc`, localized `title` and `body`. No selected trip or no eligible future reservations returns `[]`; unauthenticated calls return 401. Exact confirmed reservations, flights and lodging check-in generate notifications 180/45 minutes before start, using reservation timezone then trip timezone. Clients replace their scheduled snapshot only after a successful response and clear it on logout/scope change. Local permissions are required; this does not register push tokens or send FCM/APNs messages.

Vuelos y check-in con hora exacta también reciben un aviso 24 horas antes; las reservas comunes mantienen solo 180/45 minutos. Los avisos cuyo momento ya pasó no se envían retroactivamente.

Cambios de ciudad: se comparan días consecutivos del itinerario. Si cambia la ciudad, se programa un aviso de preparación a las 09:00 del día anterior en la zona del viaje, sin inferir hora de salida. El DTO usa `reservationId: null`, `tripDayId` y un ID estable `city-change-{dayId}`. Editar/eliminar el cambio lo reemplaza/cancela en la siguiente sincronización.

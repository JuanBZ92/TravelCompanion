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
    string? ReservationId
);
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

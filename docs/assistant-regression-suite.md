# Assistant Regression Suite

Last updated: 2026-05-17

This suite protects the assistant as a controlled product feature, not a free-form chat.

Run it with:

```powershell
dotnet test tests\TravelCompanion.Api.Tests\TravelCompanion.Api.Tests.csproj
```

## Covered Scenarios

- `Que puedo pedirte` returns guided actions without requiring a completed profile.
- Unknown text returns `missingContext.field = assistantCommand` and suggested next actions.
- `Ver mi agenda` returns the schedule for the requested date and does not reuse prior assistant/model text.
- Date planning prompts such as `Proponeme planes para el 8 de octubre` use that date's schedule.
- Preference edits such as `editar preferencia evitar culture` ask for confirmation before persisting.
- Rejected preference edits can be applied as one-off planning context without changing the profile.
- Avoided tag parsing uses the canonical recommendation tag catalog and aliases, for example `baños termales -> onsen`.
- Planning responses filter disliked/avoided tags when alternatives exist.
- Follow-up actions such as `Otra opcion` and `Reemplazar <recommendationId>` avoid repeating the same recommendation when alternatives exist.
- `Algo con menos caminata` switches to a deterministic low-walking mode.
- Model failures fall back to deterministic structured responses.
- Model text that claims an itinerary was saved is ignored unless the explicit backend save action confirms it.
- Explicit save text in chat returns a confirmation requirement instead of saving through the model.
- `POST /api/ai/travel-chat` rejects missing Bearer sessions.
- `POST /api/ai/travel-chat` returns validation errors for blank messages.
- `POST /api/ai/travel-chat` returns a stable structured `TravelChatResponse` contract for authenticated mobile clients.
- Contract snapshots cover plan, missing preferences, agenda, preference confirmation, unsupported command, and save-confirmation-required responses.
- Mobile presentation tests cover malformed assistant payload normalization and actionable recommendation card state.
- Backend assistant telemetry emits structured outcome logs for intents, missing context, preference confirmations/rejections, save confirmation requirements, model fallback, and plan responses without logging raw user prompts.

## Current Test Anchors

Primary file:

- `tests/TravelCompanion.Api.Tests/TravelChatServiceTests.cs`

Supporting tests:

- `tests/TravelCompanion.Api.Tests/RecommendationTagCatalogServiceTests.cs`
- `tests/TravelCompanion.Api.Tests/TravelChatEndpointTests.cs`
- `tests/TravelCompanion.Api.Tests/Snapshots/*.json`
- `tests/TravelCompanion.Mobile.Tests/TravelChatMobilePresentationTests.cs`

## Next Slices

- Add full MAUI ViewModel command tests if the app later extracts navigation/dialog dependencies behind interfaces.
- Add dashboards/alerts over the structured assistant telemetry in Application Insights.

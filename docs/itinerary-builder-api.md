# Itinerary Builder API

## Revisión del día

Las respuestas que incluyen `TripScheduleDto` incorporan `dayReviews`, una revisión determinista por cada fecha del viaje. La app puede presentarla desde caché y recalcularla tras una mutación local sin llamar a un modelo de IA.

Cada revisión contiene:

- `status`: `balanced`, `tight` o `needs_attention`.
- `title` y `summary`: texto breve listo para la interfaz móvil.
- `issues`: avisos `overlap`, `tight_transfer` o `packed_day`, con severidad y los identificadores de los planes afectados.
- `availableMinutes` y `recommendedMinutes` cuando el aviso depende de un traslado.

Los solapamientos solo se afirman cuando ambos horarios tienen un final explícito. Los traslados se estiman cuando existen coordenadas de ambos planes; si faltan, solo se genera un aviso para cambios claros de ciudad. La estimación añade un margen de seguridad de 10 minutos y nunca modifica el itinerario automáticamente.

The builder is a server-authorized product mode. Mobile capability flags are presentation hints only.

An initial test access can be provisioned at startup with `BuilderDemo__Enabled=true` and a six-digit `BuilderDemo__Pin`. The PIN remains server-side and the seed is idempotent.

## Session modes

- `FreeMapPreview`: PIN `0000`; limited free map endpoints only.
- `Builder`: six-digit Admin-issued PIN; Map, Today and Assistant. Itinerary writes are allowed after setup.
- `Trip`: four-digit curated trip PIN; Map, Today, Assistant and Docs. Mobile itinerary writes are denied.

## Free builder trial

PIN `0000` now creates an isolated technical account per app installation. The client sends a random installation identifier; the API stores only its SHA-256-derived account key. Each installation receives its own draft, assistant quota and conversion attribution.

- The 30-minute editing window starts with the first successful `PUT /api/mobile/builder/setup`.
- After editing expires, the draft is read-only and recoverable for seven days.
- A periodic background process permanently removes expired drafts; it first detaches sessions and removes dependent notification rows.
- Free place search, Today and Assistant candidates are restricted server-side to `Free` recommendations inside an enabled city's `FreeRadiusKm`. Google Places remains disabled.
- Three assistant requests that return recommendation cards are included. Validation questions, missing context and service failures do not consume quota.
- `GET /api/mobile/pass/status` returns the authoritative trial state. `POST /api/mobile/pass/redeem` verifies an unused paid builder PIN, transfers the existing draft to that grant, revokes the trial and issues a new Builder session.

The pass price defaults to `24.99 EUR`. `FreePreview:PassPrice`, `Currency`, `PurchaseUrl`, `TrialEditingMinutes`, `DraftRetentionDays` and `AssistantRequestLimit` are configurable. A payment provider or external checkout must issue the unused Builder PIN; the API never treats a client-side purchase claim as proof of payment.

## Setup

`GET /api/mobile/builder/setup` returns the current trip setup or an unconfigured state.

`PUT /api/mobile/builder/setup` validates a continuous, non-overlapping city segment range. The first save creates a published `SelfServiceBuilder` trip with four daily blocks and `AutofillEnabled=false`. Later saves require `ExpectedRevision`.

## Itinerary mutations

- `POST /api/mobile/itinerary`
- `PATCH /api/mobile/itinerary/{id}`
- `DELETE /api/mobile/itinerary/{id}?expectedRevision=`

Only authenticated builder sessions can mutate items. Update and delete are restricted to `Owner=Traveler`. Every mutation checks `PlanRevision`; create also uses an idempotency key. Offline mutations are intentionally unsupported.

## Place search

`POST /api/mobile/places/search` merges YUKU results with Google Places when `GooglePlaces:Enabled=true` and a server-side key is configured. Google failures return YUKU results. Google API keys are never sent to MAUI or logged.

Persisted Google-derived itinerary items keep the provider Place ID and the traveler label only. External details are treated as transient content.

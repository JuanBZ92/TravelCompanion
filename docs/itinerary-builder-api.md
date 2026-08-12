# Itinerary Builder API

The builder is a server-authorized product mode. Mobile capability flags are presentation hints only.

An initial test access can be provisioned at startup with `BuilderDemo__Enabled=true` and a six-digit `BuilderDemo__Pin`. The PIN remains server-side and the seed is idempotent.

## Session modes

- `FreeMapPreview`: PIN `0000`; limited free map endpoints only.
- `Builder`: six-digit Admin-issued PIN; Map, Today and Assistant. Itinerary writes are allowed after setup.
- `Trip`: four-digit curated trip PIN; Map, Today, Assistant and Docs. Mobile itinerary writes are denied.

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

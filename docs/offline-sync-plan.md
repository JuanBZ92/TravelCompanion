# Offline Sync Plan

Phase 3 turns the mobile app into an offline-first client. This document captures the target model and the implemented first slice.

## Goals

- Render useful trip data from local storage before remote calls complete.
- Keep itinerary saves and preference edits from being lost when the network is weak.
- Move toward an explicit `/api/sync` contract with opaque sync tokens.
- Avoid chatty mobile APIs.
- Keep sensitive local data encrypted or protected.

## Current Implementation

The app already has encrypted local snapshots through `OfflineCacheService`:

- `MobileDiscoverStore`: local-first Discover snapshot.
- `MobileBootstrapStore`: local-first aggregate snapshot for destination, recommendations, packages, entitlements, and schedule.

This phase adds the first encrypted mutation queue:

- `OfflineMutationQueueService`
- Stored through `OfflineCacheService`, so queue payloads are encrypted with the device-local cache key.
- Currently supports `save_itinerary_item`.
- Avoids duplicate queued saves for the same recommendation/date/start time.
- Replays queued saves when `TravelChatViewModel.LoadContextAsync()` has a valid token.
- Updates `MobileBootstrapStore` schedule cache after a queued save is confirmed by the backend.

## Current Queue Behavior

When the user taps `Guardar` on an assistant card:

1. MAUI asks for confirmation.
2. MAUI calls `POST /api/ai/save_itinerary_item`.
3. If the backend confirms `saved = true`, the card becomes saved and schedule cache updates.
4. If the request fails because the network is unavailable or times out, MAUI queues the save locally.
5. On the next assistant context load, MAUI replays pending mutations.
6. Only a backend-confirmed replay removes the item from the queue.

The UI text intentionally says the item is queued, not saved, until the backend confirms.

## Target Sync Contract

Future backend endpoint:

```http
POST /api/sync
```

Request:

```json
{
  "syncToken": "opaque-token-or-null",
  "clientChanges": [
    {
      "clientMutationId": "local-guid",
      "entityType": "ItineraryItem",
      "operation": "Upsert",
      "serverId": null,
      "createdAtUtc": "2026-05-17T10:00:00Z",
      "payload": {
        "recommendationId": "33333333-3333-3333-3333-333333333301",
        "date": "2026-10-06",
        "startsAt": "11:00:00",
        "endsAt": "12:30:00"
      }
    }
  ]
}
```

Response:

```json
{
  "serverChanges": [
    {
      "entityType": "ScheduleItem",
      "operation": "Upsert",
      "serverId": "44444444-4444-4444-4444-444444444401",
      "payload": {}
    }
  ],
  "deletedEntities": [],
  "conflicts": [],
  "nextSyncToken": "opaque-next-token"
}
```

Rules:

- `nextSyncToken` is always returned.
- Client timestamps are metadata, not authority.
- Mutations are idempotent by `clientMutationId`.
- Deletes are tombstones.
- Initial conflict policy: server-wins for shared server data, client retries for queued local-only mutations.
- The mobile app should render local state first and reconcile after replay.

## Next Implementation Slices

1. Add offline queue support for preference profile patches.
2. Replay pending mutations from app startup and schedule/discover refresh, not only assistant load.
3. Add a visible pending-sync indicator in the shell or More tab.
4. Add server `/api/sync` endpoint with opaque tokens.
5. Move long-lived snapshots and mutation metadata to SQLite when the data model grows beyond a few encrypted snapshots.
6. Add sync tests for idempotency, conflict handling, and tombstones.


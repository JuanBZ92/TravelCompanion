# Map state retention

## Implementation

- Free and paid pages retain pins across tab navigation. Free updates markers by key; paid retains its existing differential renderer.
- Equivalent catalog payloads do not replace the displayed recommendations. Free comparison ignores the response generation timestamp.
- Initial data/city changes establish bounds. Ordinary refreshes preserve the camera and selected recommendation; removing the selected recommendation closes its card.
- Handler recreation restores the previous visible region from memory. The MAUI map retains the pin collection used by the replacement handler.
- Free cache v2 is scoped by identity, trip, access mode, experience mode and language. Legacy unscoped entries are ignored. Memory holds cities and one city preview; encrypted disk storage remains owned by OfflineCacheService.
- Concurrent Free fetches share their request. Cancelling a caller does not cancel another caller's request. Invalidation and authentication changes reject late results before publishing; writes and cache cleanup are serialized.
- Warm visits avoid loading indicators and catalog downloads while fresh. Stale snapshots are refreshed without clearing the visible map. Automatic attempts are throttled to five minutes; explicit retry/refresh bypasses the automatic throttle.
- Paid retains the existing bootstrap cache and its lifetime. It compares the cached snapshot on a warm visit so updates published by synchronization remain visible.

## Automated verification

The production FreeMapStore runs against controlled API/disk dependencies in FreeMapStoreTests. Cases cover memory reuse without disk/network reads, shared requests, cancellation of one waiter, invalidation, logout, same-account reauthentication, disk recovery, account/trip/language/access isolation, ignored legacy keys, and retry after an offline failure.

Mobile suite: 89 passing tests. Android Debug build checked locally. These checks do not validate native rendering or camera restoration on a physical device.

## Device acceptance still required

- Free and paid: select a pin, pan and zoom, switch Map → Today → Map repeatedly. Check the same camera, gold pin and card, with no full pin redraw.
- Refresh an unchanged catalog, then one with additions/removals or changed coordinates/access. Check only affected pins, preserved camera, and dismissal of a removed selection.
- Switch city, trip, language and account; log out and back in. Verify old cards/pins never appear in the new context.
- Return offline and resume after suspension/native handler recreation. Verify retained content, camera and free-radius overlay.
- Repeat native checks on iOS. No physical Android or iOS validation is claimed by this change.

No backend contract, migration or new package is required. Camera restoration across a full process restart is outside this change.

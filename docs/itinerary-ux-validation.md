# Itinerary UX changes — September 21, 2026

## Behavior

- Selecting a hotel no longer starts another autocomplete request from the programmatic text update. Editing the name still invalidates the selected place and permits a new search.
- Adjacent city segments may share their transfer date. Gaps and overlaps longer than a boundary day remain invalid. The arriving city supplies that day's base/hotel; the original city date ranges are stored independently and survive reopening the setup form.
- Retrying a failed save resubmits the current form. Retrying setup loading preserves entered fields instead of clearing the draft.
- Content pages respect the keyboard safe area, alongside Android's existing resize mode. Scrollable forms retain their content and actions above the keyboard.
- Today headings show only the day period. The locations heading and place/distance descriptions are hidden; personal notes are displayed on activity and reservation cards.
- Traveler-owned activities have the same edit action as reservations. Edited titles are displayed instead of being replaced by the catalog title. Coordinates and other stored metadata remain available for maps and editing.
- Both full-day Assistant paths present recommendations without automatically writing them to the itinerary. Each card's Save action performs the existing idempotent save. Existing completed-day cards remain marked as saved.

## Deployment

Apply `20260921130144_PreserveBuilderCityTransferDates` with the compatible backend before distributing the mobile changes. It adds nullable `Trips.BuilderSegmentsJson`; older trips fall back to their existing day plans. Cleanup/account deletion removes this data, and pending-purchase snapshots preserve it.

## Verification

- Mobile suite: 90 tests passed, including personal notes, edited titles and edit permissions.
- API: 7 BuilderSetupEndpointTests and 30 itinerary/free-trial/commerce regression tests passed. New endpoint cases cover shared dates, reload persistence, arriving-city assignment, gaps and excessive overlap. These suites use the existing test database harness, not a production PostgreSQL migration rehearsal.
- Android Debug build passed with no warnings or errors.
- Still to validate on devices: hotel selection while the keyboard is open, failed-network save/retry with a populated form, keyboard visibility on Android/iOS, manual saving of individual Assistant suggestions, and editing notes on Today cards.

This implementation has not been deployed or uploaded as a new APK in this change.

# Trip expenses

Expenses are personal, scoped by authenticated user and active trip, independent
of itinerary revisions and editorial reservation content. The **Viaje** screen
has **Itinerario / Gastos** sections; activity details can open the expense editor
with an activity snapshot. Free and expired-pass travelers can maintain expenses.
Curated trips and active paid builder passes can request breakdowns and CSV exports.

## Data and API

Apply the additive `AddTripExpenses` migration before deploying the mobile client.
It creates `TripExpenses` and `TripExpenseSettings`; existing trip data is untouched.
Expenses retain their activity title when a reservation is deleted. Deleting a trip
cascades expenses; account deletion explicitly erases expenses even when the account
itself is soft-deleted. Verified account linking transfers ownership.

Endpoints under `/api/mobile/trips/{tripId}/expenses`:

- `GET /`: settings, expense records including tombstones, permitted activity references and premium status.
- `PUT /{id}`: create/update/soft-delete using `ExpectedRevision`, `SettingsRevision`, and `MutationId`.
- `PUT /settings`: budget/base currency with optimistic revision and mutation ID.
- `GET /rate`: currency pair and date; unavailable rates return 204.
- `GET /breakdown` and `GET /export`: server-enforced premium access; 403 opens the pass flow.

Trip locks serialize writes in PostgreSQL. Retries preserve IDs; stale mutations
return 409 instead of replacing newer content. Changing currency revalues every
active expense atomically and replaces the budget with the explicitly entered
amount. If any rate is unavailable, no currency/budget changes are committed.

Amounts use decimal arithmetic and currency precision. Supported currencies are
listed centrally in `ExpensePolicy`; JPY/KRW/CLP use zero fractional digits.
Future split payments can reference the stable expense ID in separate participant,
payment and allocation entities. No payer/debt placeholders are persisted in v1.

## Offline and privacy

Encrypted expense books live in a locale-independent app-data directory, outside
the disposable catalog cache. Saving requires no network. Pending settings and
expense mutations replay on screen refresh or existing connectivity/lifecycle
sync events. Permanent validation failures are flagged for editing. Revision
conflicts retain the local draft and server version until explicitly resolved.
Scope checks stop late requests from writing into another session. Modal content
is hidden on account/trip changes.

Frankfurter is called only by the API, with currency codes/date, never amounts or
travel data. Daily quotes are cached for 24 hours and fixed on each expense.
Manual rates and cached offline quotes retain their source/date. Unconverted
expenses remain saved and are excluded from the partial total; later sync retries
conversion. Rates in a different base currency are also excluded from totals.

CSV sharing is explicit and requires server confirmation of premium access. It
contains the expense data the user requested, with formula-safe quoted text.
Expense content is not sent to product analytics or client diagnostics.

## Release verification

Automated coverage includes amount parsing/precision, fixed rates, missing rates,
atomic currency changes, idempotency, revision conflicts, activity deletion, Free
access/premium enforcement, CSV escaping, offline persistence, session isolation,
account linking and local deletion.

Run `PostgresExpenseTests` with `TRAVELCOMPANION_TEST_POSTGRES` pointing to a test
database whose user can create schemas. It creates/drops a unique temporary schema
and verifies migrations, concurrent retries, conflicting edits and cascade deletion.
Never point this test at the production database.

Before release, validate Android/iOS Release on a device with Free, 3333 and 3334:
save/edit/delete, airplane mode and restart, replay without duplicates, activity
picker, budget conflict, currency change, paid export, pass-return action, back
navigation, large text, TalkBack/VoiceOver and keyboard visibility.

Deployment order: migration and compatible API, then the new mobile build.
This feature does not change the real-purchase activation setting.

## Implementation audit — 2026-09-29

| Plan item | Implementation / evidence | Status |
| --- | --- | --- |
| Access within Viaje without another bottom tab | SchedulePage Itinerario/Gastos switch; Android Back returns to Itinerario | Implemented; device review pending |
| Quick expense entry | Amount, remembered currency, category tiles, optional concept and date; one Save action | Implemented; device keyboard review pending |
| Add from a reservation or link later | Detail action prefills activity; searchable picker; title snapshot survives activity deletion | Implemented; API test passes |
| List, edit and delete | Date-ordered rows, edit modal, confirmed deletion with tombstone | Implemented |
| Budget and total | Optional budget, remaining/over-budget amount, progress, explicit partial totals | Implemented |
| JPY and user currency | Region-based initial summary currency, per-expense currency, decimal precision | Implemented |
| Fixed conversion and offline fallback | Backend quotes, source/date, manual rate, pending conversion; only provider-derived rates are reused for other expenses | Implemented; focused tests pass |
| Explicit summary currency change | Confirmation and atomic backend revaluation; unavailable quotes keep previous settings | Implemented; API test passes |
| Free versus paid | Basic expenses remain available after expiry; breakdown and CSV require current server-validated access | Implemented; active/expired pass tests pass |
| Return from pass purchase | Pending action scoped to user/trip resumes from Gastos | Implemented; purchase-return device test pending |
| Offline persistence and sync | Encrypted user/trip cache, stable mutation IDs, conflict choices, rejected drafts remain editable | Implemented; mobile tests pass |
| Session, account and trip integrity | Scope guards, verified account transfer, deletion cleanup, no itinerary mutation | Implemented; focused tests pass |
| Concurrent writes and migration | Real PostgreSQL 17 isolated temporary instance: full migration, duplicate retries, competing edits and cascade deletion | Passed |
| Future splitting | Stable expense IDs and separate expense entities; no premature participant/debt UI | Implemented as agreed |
| Android Release | Compiles; physical Free/3333/3334 and accessibility checks still require a connected device | Device validation pending |
| iOS Release | Requires a Mac/TestFlight and a physical device | Pending |
| Publication | API deployed to Render; additive migration applied with a prior backup; APK 98 uploaded to Drive | Completed; physical-device validation remains pending |

Audit fixes: manual rates no longer become cached automatic quotes for other
expenses; editing displays the existing fixed quote; keeping a local conflict
preserves its manual rate when the base currency is unchanged; breakdown responses
carry their own currency so another device's currency change cannot mislabel totals.

The isolated PostgreSQL instance was stopped after testing. The existing local
PostgreSQL service was not modified. No physical-device validation is represented
as complete by automated tests.

## Release 98

API commit `c8a0841` was deployed to Render. Production disables automatic startup
migrations, so `20260928224254_AddTripExpenses` was applied separately, in a
transaction, after a database backup. Initial endpoint checks caught the missing
tables; after migration, expense and breakdown reads succeeded for demo accounts
3333 and 3334. Both `/health` and `/health/ready` returned 200.

The signed Android APK reports versionCode 98 and was uploaded as
`YUKU-Japan-98-gastos.apk` to the existing TravelCompanion Drive folder.
Android/iOS visual, accessibility and purchase-return checks remain pending;
no Android device was detected during release.

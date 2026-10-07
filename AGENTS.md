# TravelCompanion
.NET 10 travel application: ASP.NET Core API/admin, .NET MAUI mobile client,
shared contracts and a notifications worker. Tests use xUnit.

## Repository map
- `src/TravelCompanion.Api`: Controllers, Services, EF Core Data/Migrations, Razor Pages admin; PostgreSQL.
- `src/TravelCompanion.Mobile`: Pages, ViewModels, Services, Localization, platform integrations.
- `src/TravelCompanion.Shared`: DTOs, enums and policies shared by API and mobile.
- `src/TravelCompanion.Notifications.Worker`: background notifications; references the API.
- `tests/TravelCompanion.{Api,Mobile,Shared}.Tests`: corresponding tests.
- `infra/terraform`, `scripts`, `tools`: infrastructure, deployment and development/catalog utilities.

## Repository rules
- Preserve existing project boundaries and shared API/mobile contracts.
- Keep AI orchestration and secrets in the backend; mobile calls the backend.
- Add or update focused tests for behavior changes.
- Start exploration in the relevant area; skip bin, obj, artifacts, outputs and logs unless needed.
- Treat documentation as guidance; verify implementation details against current code.
- New or modified mobile screens must match the existing editorial style: warm paper,
  readable serif headings, restrained cards and a clear primary action. Reuse shared
  resources and `EditorialUi` controls rather than introducing another palette.
- Treat UX as part of completion: verify navigation, loading, empty/error/retry states,
  offline behavior, keyboard use, long text and enlarged fonts. Keep icon controls
  accessible with localized descriptions and touch targets of at least 48 dp.
- Localize changed user-facing flows in Spanish and English. Preserve session/trip
  isolation and existing access rules; visual changes must not broaden permissions.
- Validate affected behavior and Android compilation. Review changed screens on a
  device or emulator when available; record actual coverage and any blocked checks.
- Do not run TalkBack tests or enable TalkBack on the user's device, now or in future
  sessions, per the user's explicit preference. Continue other accessibility checks
  without activating it and document screen-reader coverage as unexecuted.
- Use PIN/password for review-app access, not biometrics, per the user's preference.
  Ask the user to unlock the phone's system lock manually when required.
- Preserve the user's system display settings during QA, including density, font
  scale, dark mode and rotation. Prefer overrides confined to the review app.
  Record originals before any authorized system override and restore them in
  cleanup, including interrupted tests; verify restoration before handing back.

## Completing an accepted plan
- Before implementation, record each independently verifiable acceptance criterion
  with a stable ID in a tracked checklist. Map it to the production flow, focused
  verification and status; do not mark a whole area complete because a helper exists.
- Review the complete user flow, including navigation, offline behavior, account/trip
  changes and every relevant access mode. A cached entitlement must not override a
  known revoked or expired permission.
- Complete the final comparison against the accepted plan within the same task.
  Fix known implementation gaps and validate them before handing work back; the user
  should not need to request another review to finish already authorized work.
- Keep implementation, automated verification and native verification separate.
  Missing hardware is a validation limitation, not proof of completion or missing code.
- Bind final evidence to the exact source snapshot and artifact. Changes after tests
  invalidate affected evidence; do not reuse old test totals as proof of new changes.
- Keep the review within the agreed acceptance criteria. Record unrelated ideas in
  a backlog instead of silently expanding the plan and reopening completed work.
- For the current daily-use plan, use `docs/plans/daily-use-clarity.json` and
  `scripts/check-plan-completion.ps1`. See `docs/plan-completion.md` for the closing
  procedure and its limits. Never certify pending native checks from source or unit tests.

## Commands (repository root)
- Use `--verbosity minimal`; target the affected project and installed mobile platform.
- API build: `dotnet build src/TravelCompanion.Api/TravelCompanion.Api.csproj --verbosity minimal`
- Worker build: `dotnet build src/TravelCompanion.Notifications.Worker/TravelCompanion.Notifications.Worker.csproj --verbosity minimal`
- API tests: `dotnet test tests/TravelCompanion.Api.Tests/TravelCompanion.Api.Tests.csproj --verbosity minimal`
- Shared tests: `dotnet test tests/TravelCompanion.Shared.Tests/TravelCompanion.Shared.Tests.csproj --verbosity minimal`
- Mobile logic tests: `dotnet test tests/TravelCompanion.Mobile.Tests/TravelCompanion.Mobile.Tests.csproj --verbosity minimal`
- For narrow changes, add `--filter FullyQualifiedName~RelevantTest`; broaden when cross-cutting behavior requires it.
- Build mobile for one installed target, e.g. `dotnet build src/TravelCompanion.Mobile/TravelCompanion.Mobile.csproj -f net10.0-android --verbosity minimal` (requires MAUI/Android tooling).

## Documentation: read only when relevant
- Setup and technical overview: `docs/TECHNICAL.md`; product behavior: `docs/FUNCTIONAL.md`.
- AI design and ranking: `docs/architecture.md`, `docs/deterministic-ranking.md`.
- API contracts: `docs/backend-contracts.md`; itinerary: `docs/itinerary-builder-api.md`.
- Offline sync: `docs/offline-sync-plan.md`; notifications: `docs/notifications-worker.md`.
- Infrastructure/deployment: `docs/INFRASTRUCTURE.md`, `docs/render-deploy.md`, `docs/production-runbook.md`.
- Catalog imports: `docs/import-templates/csv-import-guide.md`.
- Codex configuration, audit and reactivation: `docs/codex-context.md`.

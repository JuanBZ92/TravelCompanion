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

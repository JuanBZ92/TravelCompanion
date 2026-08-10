# Production Readiness Plan

Last baseline review: 2026-05-17

This document tracks the path from MVP to production. It is meant to be updated at the end of each phase.

## Current Phase

Phase 0: Baseline.

Goal: capture the current state before hardening contracts, infra, offline sync, assistant behavior, data quality, localization, and release operations.

## Repository Baseline

- Current branch: `master`.
- Git status at baseline: branch is ahead of `origin/master` by 1 commit.
- Working tree before this document: clean.
- Main apps:
  - API/Admin: `src/TravelCompanion.Api`
  - Mobile: `src/TravelCompanion.Mobile`
  - Shared contracts: `src/TravelCompanion.Shared`
- Tests:
  - `tests/TravelCompanion.Api.Tests`
  - `tests/TravelCompanion.Shared.Tests`
- Infra:
  - Terraform under `infra/terraform`
  - Azure DevOps pipeline in `azure-pipelines.yml`
  - Manual publish scripts under `scripts`

## Verified Commands

Run these from the repository root.

```powershell
dotnet test tests\TravelCompanion.Shared.Tests\TravelCompanion.Shared.Tests.csproj --no-restore /p:UseSharedCompilation=false
dotnet test tests\TravelCompanion.Api.Tests\TravelCompanion.Api.Tests.csproj --no-restore /p:UseSharedCompilation=false
dotnet build src\TravelCompanion.Mobile\TravelCompanion.Mobile.csproj -f net10.0-android --no-restore /p:UseSharedCompilation=false
```

Terraform validation should also be part of baseline when `terraform` is available:

```powershell
terraform -chdir=infra\terraform fmt -check -recursive
terraform -chdir=infra\terraform init -backend=false
terraform -chdir=infra\terraform validate
```

Latest verification result, 2026-05-17:

- Shared tests: passed, 9/9.
- API tests: passed, 38/38.
- Android build: passed, 0 warnings, 0 errors.
- Terraform: `fmt -check`, `init -backend=false`, and `validate` passed.

## Deployment Baseline

### API

Script: `scripts\Publish-Api.ps1`

Observed behavior:

- Builds and publishes `src\TravelCompanion.Api\TravelCompanion.Api.csproj`.
- Packages API output into `artifacts\api\travelcompanion-api.zip`.
- Deploys to Azure App Service through `az webapp deploy`.
- Resolves `ResourceGroupName`, `AppName`, and `ApiUrl` from Terraform outputs when not passed explicitly.
- Sets `WEBSITE_RUN_FROM_PACKAGE=1` unless disabled.
- Smoke-tests `/health` unless `-SkipSmokeTest` is passed.

Production notes:

- Good MVP script for manual deploys.
- Needs environment-specific guardrails before prod: explicit environment parameter, pre-deploy tests, migration policy, rollback instructions, and confirmation before prod.
- Uses local publish artifacts only; Azure DevOps deploy stage is not implemented yet.

### Mobile

Script: `scripts\Publish-Mobile.ps1`

Observed behavior:

- Builds Android APK/AAB with `TravelCompanionApiBaseUrl` embedded.
- Can install APK with `adb`.
- Supports iOS local Mac build or remote SSH build.
- Resolves API URL from Terraform output, or falls back to the current dev Azure URL.

Production notes:

- Good manual release script.
- Needs store signing workflow, versioning/build numbers, environment selection, and release notes/artifact retention.
- The default API fallback is dev-specific and should not be used for production builds.

## Infra Baseline

Terraform already defines a cost-aware MVP shape:

- Resource Group
- Key Vault
- Log Analytics
- Application Insights with caps
- Optional App Service
- Optional PostgreSQL Flexible Server
- Optional Storage
- Budget alerts

Known state:

- `infra/terraform/.gitignore` ignores `.terraform/`, `terraform.tfvars`, `*.tfstate`, `tfplan`, and `*.tfplan`.
- Local ignored files are present under `infra/terraform`, including `.terraform`, `terraform.tfvars`, `terraform.tfstate`, `tfplan`, and `publish`.
- Tracked files `infra/terraform/travelcomp` and `infra/terraform/travelcomp1` look like local generated artifacts and should be reviewed in Phase 1.
- Azure DevOps pipeline currently builds/tests API, validates Terraform, and can optionally create a dev Terraform plan.
- Pipeline does not deploy API/mobile and does not run Terraform apply.
- Pipeline trigger uses `main` and `develop`, while current branch workflow uses `master`.
- Azure service connection is still `TODO-AZURE-SERVICE-CONNECTION`.

## API Baseline

Observed production-ready pieces:

- `/health` endpoint exists.
- Response compression is enabled.
- Application Insights is configured with adaptive sampling.
- `RequestObservabilityMiddleware` exists.
- PostgreSQL uses EF Core migrations.
- Database initialization runs `MigrateAsync()` and seed at app startup.
- OpenAI is backend-only.
- Travel assistant has deterministic ranking and model fallback.
- Save itinerary is a backend action and requires mobile confirmation.

Risks / gaps:

- Automatic `MigrateAsync()` on app startup is convenient for MVP but should be revisited before prod. Prefer explicit migration step or controlled startup migration policy.
- Health check is basic and does not verify DB/OpenAI dependencies.
- No documented rollback runbook.
- Need smoke tests for auth, bootstrap, recommendations, assistant, and save itinerary.
- Rate limiting is not yet documented as implemented.

## Mobile Baseline

Observed production-aware pieces:

- API URL can be embedded at build time via `TravelCompanionApiBaseUrl`.
- Runtime override exists through `TRAVELCOMPANION_API_BASE_URL`.
- Mobile has local cache/store services for bootstrap/discover.
- Schedule supports refresh and cache update after assistant save.
- Assistant cards are structured and actionable.
- Location is captured with permission flow and sent as `CurrentLocation`.

Risks / gaps:

- Offline-first storage is not yet a formal SQLite sync store.
- Mutation queue for offline saves/preference changes is not implemented.
- Mobile release signing/versioning is not fully documented.
- Crash reporting is not documented as implemented.
- iOS publish depends on local/remote Mac setup and signing configuration.

## Assistant Baseline

Observed production-aware pieces:

- Assistant is intent-driven rather than free chat.
- Supported mental model: Planificar, Ajustar, Agenda, Preferencias, Ayuda.
- Missing context is returned explicitly.
- Preference edits require confirmation.
- Rejected preference edits can be used as one-off planning context.
- Visible recommendation tags can drive preference changes.
- Recommendations are filtered against disliked tags when alternatives exist.
- Save itinerary cannot be claimed as saved unless backend save confirms.
- `/api/recommendations` requires a trip session. Free preview sessions use the redacted `/api/mobile/free-map/*` contract and cannot access trip content.
- Mobile discover/bootstrap send recommendation summaries; full recommendation descriptions are loaded through an authenticated, entitlement-checked detail endpoint.
- Encrypted mobile Discover/Bootstrap disk snapshots expire after 6 hours and are deleted for the current user on logout.

Risks / gaps:

- No offline prompt/evaluation suite for assistant behavior yet.
- No analytics dashboard for assistant intent, save, replace, avoid tag, and failures.
- Localization is not implemented end to end.
- Tags need a canonical catalog with aliases and translations.

## Phase 0 Done Checklist

- [x] Current repo/app/infra shape documented.
- [x] Deploy scripts inspected.
- [x] Pipeline inspected.
- [x] Infra baseline inspected.
- [x] API/mobile/assistant current state documented.
- [x] Shared tests run and result recorded.
- [x] API tests run and result recorded.
- [x] Android build run and result recorded.
- [x] Terraform validation run and result recorded, or blocker recorded.

## Phase 1 Entry Criteria

Phase 1 can start once Phase 0 verification is recorded above.

Recommended Phase 1 scope:

1. Align Azure pipeline branches with the actual branch strategy (`master` vs `main`/`develop`).
2. Review and remove tracked generated Terraform artifacts if confirmed safe: `infra/terraform/travelcomp`, `infra/terraform/travelcomp1`.
3. Add an explicit smoke-test script for API endpoints.
4. Add or update contract documentation for production-critical endpoints.
5. Decide migration policy: startup migrations for dev/staging only, controlled migration for prod.
6. Add a first production runbook: deploy, smoke test, rollback, and common failure checks.

## Phase 1 Done Checklist

- [x] Azure pipeline branch filters include `master`.
- [x] Tracked generated Terraform ZIP artifacts were reviewed and removed: `infra/terraform/travelcomp`, `infra/terraform/travelcomp1`.
- [x] Terraform ignore rules were extended to prevent those artifacts from being tracked again.
- [x] API smoke-test script added: `scripts/SmokeTest-Api.ps1`.
- [x] Production-critical endpoints documented in `docs/backend-contracts.md`.
- [x] Initial production runbook added: `docs/production-runbook.md`.
- [x] Migration policy documented in the production runbook.
- [ ] Azure DevOps service connection configured. Current value remains `TODO-AZURE-SERVICE-CONNECTION`.
- [ ] API deploy stage added to Azure DevOps. Manual deploy script remains the current path.
- [ ] Mobile store signing and release pipeline configured.

Phase 1 is complete for repository hardening. The unchecked items require Azure/project configuration decisions and should remain open as operational follow-ups.

Latest Phase 1 verification result, 2026-05-17:

- `scripts/SmokeTest-Api.ps1` syntax parse: passed.
- Shared tests: passed, 9/9.
- API tests: passed, 38/38.
- Android build: passed, 0 warnings, 0 errors.
- Terraform: `fmt -check`, `init -backend=false`, and `validate` passed.

## Phase 3 Done Checklist

- [x] Existing encrypted offline snapshot stores reviewed.
- [x] Encrypted offline mutation queue added for assistant itinerary saves.
- [x] Assistant save flow queues network/time-out failures instead of losing confirmed user intent.
- [x] Pending itinerary saves replay when assistant context loads with a valid token.
- [x] Schedule cache updates after queued save replay is confirmed by the backend.
- [x] Target `/api/sync` contract documented in `docs/offline-sync-plan.md`.
- [ ] Preference profile patch mutations queue offline.
- [ ] Pending sync indicator added to shared mobile UI.
- [ ] Replay runs from app startup or global refresh, not only assistant context load.
- [ ] Backend `/api/sync` endpoint implemented.
- [ ] SQLite sync store introduced if encrypted snapshot files become too limited.

Phase 3 first slice is complete. The remaining items define the next offline/sync slices.

## Phase 4 Done Checklist

- [x] Assistant regression suite documented: `docs/assistant-regression-suite.md`.
- [x] Regression coverage added for model text that falsely claims itinerary save state.
- [x] Regression coverage added for schedule chip behavior after a prior model response.
- [x] Regression coverage added for one-off avoided-tag planning using canonical tag aliases.
- [x] Existing coverage confirms preference edit confirmation, rejection, one-off planning context, date parsing, agenda view, guided help, unsupported command guidance, model fallback, less-walking mode, and replacement actions.
- [x] Endpoint-level auth/schema tests added for `POST /api/ai/travel-chat`.
- [x] Contract snapshots added for representative `TravelChatResponse` payloads.
- [x] Assistant telemetry events implemented through structured backend outcome logs.
- [x] Mobile assistant presentation tests added for malformed payload normalization and card action state.

Latest Phase 4 verification result, 2026-05-17:

- Shared tests: passed, 9/9.
- API tests: passed, 56/56.
- Mobile tests: passed, 2/2.

Phase 4 is complete for the MVP-to-product hardening pass. Remaining future work is dashboarding the structured telemetry and adding deeper MAUI command tests if navigation/dialog dependencies are extracted behind interfaces.

## Phase 5 Done Checklist

- [x] Canonical recommendation tag catalog service added.
- [x] Tag catalog is generated from live recommendation categories and tags.
- [x] Tag aliases are centralized in backend service instead of embedded only in chat parsing.
- [x] Public tag catalog endpoint added: `GET /api/recommendations/tags`.
- [x] Assistant preference parsing uses the same tag catalog to resolve avoided tags.
- [x] Contract docs updated for recommendation tags.
- [x] Admin UI shows the canonical tag catalog and normalizes aliases before saving recommendation tags.
- [ ] Mobile profile/preferences UI can browse/search tag catalog before editing dislikes.
- [ ] Tag display names and aliases are localized for `es` and `en`.
- [ ] Recommendation import pipeline validates unknown tags.

Latest Phase 5 verification result, 2026-05-17:

- Shared tests: passed, 9/9.
- API tests: passed, 44/44.
- Android build: passed, 0 warnings, 0 errors.

Phase 5 data-quality slices are complete for backend catalog, assistant parsing, endpoint exposure, and Admin tag normalization. The remaining items are the next mobile data-quality and localization slices.

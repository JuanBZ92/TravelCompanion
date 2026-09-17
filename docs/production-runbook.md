# Production Runbook

This runbook is the initial operational guide for Travel Companion. Keep it short, explicit, and updated whenever deployment behavior changes.

## Environments

Current target environments:

- `dev`: local and low-risk Azure testing.
- `staging`: production-like validation before real users.
- `prod`: real users and real data.

Each environment should have separate:

- Terraform state.
- Azure Resource Group.
- PostgreSQL database.
- Key Vault secrets.
- Application Insights instance.
- Mobile API base URL.

## Pre-Deploy Checklist

Run from the repository root:

```powershell
dotnet test tests\TravelCompanion.Shared.Tests\TravelCompanion.Shared.Tests.csproj --no-restore /p:UseSharedCompilation=false
dotnet test tests\TravelCompanion.Api.Tests\TravelCompanion.Api.Tests.csproj --no-restore /p:UseSharedCompilation=false
dotnet test tests\TravelCompanion.Mobile.Tests\TravelCompanion.Mobile.Tests.csproj --no-restore /p:UseSharedCompilation=false
dotnet build src\TravelCompanion.Mobile\TravelCompanion.Mobile.csproj -f net10.0-android --no-restore /p:UseSharedCompilation=false
terraform -chdir=infra\terraform fmt -check -recursive
terraform -chdir=infra\terraform init -backend=false
terraform -chdir=infra\terraform validate
```

Run the real PostgreSQL transaction and concurrency test against an empty, disposable test
database. The test creates and removes its own uniquely named schema:

```powershell
$env:TRAVELCOMPANION_TEST_POSTGRES = '<disposable-postgresql-connection-string>'
dotnet test tests\TravelCompanion.Api.Tests\TravelCompanion.Api.Tests.csproj `
  --filter FullyQualifiedName~PostgresItineraryIdempotencyTests
Remove-Item Env:\TRAVELCOMPANION_TEST_POSTGRES
```

Without that environment variable the PostgreSQL test is reported as skipped; it must run and
pass in staging before a production release.

Before deploying `prod`, also confirm:

- The target branch and commit are intentional.
- No local-only `terraform.tfvars`, `tfstate`, plans, ZIPs, APKs, or secrets are staged.
- The database has a recent backup.
- A restore point is visible for the paid Render PostgreSQL database and the last restore drill succeeded.
- Required secrets exist in Key Vault or environment variables.
- The expected API URL is the one embedded into the mobile build.

## API Deploy

Manual deploy script:

```powershell
.\scripts\Publish-Api.ps1 `
  -ResourceGroupName <resource-group> `
  -AppName <app-service-name> `
  -ApiUrl https://<api-host> `
  -SmokeTestPath /health
```

If Terraform outputs are configured locally, `ResourceGroupName`, `AppName`, and `ApiUrl` can be omitted.

For staging/prod, prefer passing all target values explicitly to avoid accidental deploys to the wrong environment.

## API Smoke Test

Public checks:

```powershell
.\scripts\SmokeTest-Api.ps1 -BaseUrl https://<api-host>
```

Authenticated checks with login:

```powershell
.\scripts\SmokeTest-Api.ps1 `
  -BaseUrl https://<api-host> `
  -Email <smoke-user-email> `
  -Password <smoke-user-password> `
  -DestinationSlug <destination-slug> `
  -IncludeAssistant
```

Authenticated checks with an existing bearer token:

```powershell
.\scripts\SmokeTest-Api.ps1 `
  -BaseUrl https://<api-host> `
  -Token <bearer-token> `
  -DestinationSlug <destination-slug> `
  -IncludeAssistant
```

`-IncludeAssistant` uses the deterministic `Que puedo pedirte` help path and should not require OpenAI ranking.

## Mobile Build

Android:

```powershell
.\scripts\Publish-Mobile.ps1 `
  -Platform Android `
  -Configuration Release `
  -AndroidPackageFormat apk `
  -ApiUrl https://<api-host>
```

For Play Store/internal release, use `-AndroidPackageFormat aab`.

iOS must run on a Mac or over SSH to a Mac:

```powershell
.\scripts\Publish-Mobile.ps1 `
  -Platform iOS `
  -Configuration Release `
  -ApiUrl https://<api-host> `
  -MacHost <host> `
  -MacUser <user> `
  -MacRepoPath <repo-path>
```

## Migration Policy

The API does not migrate the database during normal production startup. Development enables
`Database:ApplyMigrationsOnStartup`; Render explicitly disables it. Run the reviewed migration
as a separate release step using the same production connection string:

```powershell
$env:DATABASE_URL = '<production-connection-string>'
$env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet run --project src\TravelCompanion.Api\TravelCompanion.Api.csproj --configuration Release -- --migrate
Remove-Item Env:\DATABASE_URL
Remove-Item Env:\ASPNETCORE_ENVIRONMENT
```

The `--migrate` process applies migrations and seed data, then exits without starting HTTP.
Run it from an approved release environment; do not paste the connection string into logs.

Before changing production schema:

1. Review generated EF Core migration.
2. Confirm migration is backwards-compatible when possible.
3. Confirm backup/restore point exists.
4. Run migration against staging.
5. Run staging smoke tests.
6. Run `--migrate` against production.
7. Deploy API.
8. Check `/health` and `/health/ready`, then run production smoke tests.

Avoid destructive migrations in the same release as dependent app code unless there is a tested rollback plan.

## Rollback

API rollback options:

1. Redeploy the previous ZIP artifact if available.
2. Redeploy the previous Git commit using `Publish-Api.ps1`.
3. If the issue is configuration-only, revert the App Service setting or Key Vault secret and restart the app.

Database rollback:

- Prefer forward fixes for non-destructive migrations.
- For destructive or corrupting migrations, restore from backup/PITR into a new database and repoint the app only after validation.

Quarterly restore drill:

1. Restore the latest backup into a separate database.
2. Run `--migrate` against the restored database if it is behind.
3. Start the API against the restored database and require `/health/ready` to return `200`.
4. Run authenticated schedule, documents, assistant and idempotent-save smoke checks.
5. Record the backup timestamp, recovery time and result before deleting the restored database.

Mobile rollback:

- Internal testing: distribute the previous APK/AAB/IPA.
- Store release: use Play Console/App Store phased rollout controls or submit a fixed build.

## Common Failure Checks

- `/health` failing: check service startup, configuration, and logs.
- `/health/ready` failing: check the PostgreSQL connection, credentials, capacity, and migration state.
- Auth smoke failing: check seed/smoke user, session table, password policy, and bearer token header.
- Mobile bootstrap failing: check user entitlements, destination slug, seed data, and DB migrations.
- Assistant failing: check user profile, schedules, recommendations, OpenAI config, and deterministic fallback logs.
- Slow responses: check Application Insights dependency timings and PostgreSQL query plans.

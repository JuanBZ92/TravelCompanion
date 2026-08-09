# Render deploy

This repo can deploy the ASP.NET Core API to Render as a Docker web service with a managed Render Postgres database.

## First deploy

1. Commit and push `Dockerfile`, `.dockerignore`, `render.yaml`, and the API `DATABASE_URL` support in `src/TravelCompanion.Api/Program.cs` to `newapproach`.
2. Open the Blueprint flow:
   `https://dashboard.render.com/blueprint/new?repo=https://github.com/JuanBZ92/TravelCompanion`
3. Select branch `newapproach` if Render asks.
4. Fill the secret values requested by the Blueprint:
   - `AdminAuth__Username`
   - `AdminAuth__Password`
5. Apply the Blueprint and wait for `travelcompanion-api` to become live.
6. Test `https://<your-render-service>.onrender.com/health`.

## Optional OpenAI

The Blueprint starts with `OpenAI__Enabled=false` so the deterministic assistant works without a paid API key.
To enable model-backed replies later, add:

- `OpenAI__Enabled=true`
- `OpenAI__ApiKey=<your key>`

## Mobile app

After Render gives you the API URL, update the mobile API base URL/configuration and publish the mobile app again.

## Notes

- Render free web services can sleep, so the first request after inactivity can be slow.
- The notifications worker is intentionally not included in this free Blueprint. Background workers and cron jobs are not a reliable free path for time-sensitive push notifications.

# API
- Follow the existing Controllers/Services/Data organization; admin UI uses Razor Pages.
- Resolve user identity and authorization in the backend before accessing traveler data or executing AI tools.
- Never expose provider credentials to mobile responses or logs.
- Keep production schema migration separate from normal startup; consult `docs/production-runbook.md` (repository root) for database/release work.
- API behavior tests live in `tests/TravelCompanion.Api.Tests` at the repository root.

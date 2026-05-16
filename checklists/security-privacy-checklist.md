# Security and Privacy Checklist

Before finalizing any implementation, verify:

## Secrets

- [ ] OpenAI API key is only server-side.
- [ ] No API keys are stored in MAUI.
- [ ] No secrets are committed to git.
- [ ] Local development secrets use user secrets, environment variables or Key Vault.

## Authorization

- [ ] Authenticated user is the source of truth for userId.
- [ ] The model is never trusted to provide userId.
- [ ] Tool calls validate user ownership.
- [ ] User cannot access another user's reservations, profile or itinerary.

## Data minimization

- [ ] Only necessary profile/reservation fields are sent to the model.
- [ ] Internal IDs are minimized where possible.
- [ ] Sensitive fields are excluded.
- [ ] Logs do not contain raw secrets or excessive PII.

## AI safety

- [ ] Model cannot perform destructive actions without explicit user confirmation.
- [ ] Booking, cancellation, reservation modification and payments require confirmation.
- [ ] The model cannot invent booking status.
- [ ] The model cannot invent reservations, prices, opening hours or distances.

## Error handling

- [ ] OpenAI failures return a graceful fallback.
- [ ] Tool failures do not expose internal stack traces.
- [ ] The user sees friendly mobile-appropriate messages.

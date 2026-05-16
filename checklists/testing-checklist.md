# Testing Checklist

## Backend tests

- [ ] Unauthorized user cannot access another user's reservations.
- [ ] Missing profile triggers a preference question or `missingContext`.
- [ ] Missing reservations returns a useful response.
- [ ] Ranking prefers closer and compatible options.
- [ ] Ranking penalizes options that are too far.
- [ ] Dietary restrictions are respected.
- [ ] Disliked categories are penalized.
- [ ] Time windows between reservations are respected.
- [ ] Structured response schema is valid.
- [ ] Tool errors are handled gracefully.
- [ ] OpenAI/model failure returns fallback response.
- [ ] Save itinerary action requires explicit user confirmation.

## MAUI tests / validation

- [ ] User message renders.
- [ ] Assistant message renders.
- [ ] Recommendation cards render.
- [ ] Suggested replies call backend.
- [ ] Loading state works.
- [ ] Error state works.
- [ ] Malformed backend response does not crash UI.
- [ ] No OpenAI calls are present in mobile code.

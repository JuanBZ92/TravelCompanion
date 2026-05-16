# Prompt 01 - Create the Codex skill

Use this prompt in Codex if you want Codex to create the skill file in your repository.

```text
Create a Codex skill named travel-ai-assistant.

This skill must help implement a travel AI assistant inside a .NET backend + .NET MAUI mobile application.

The assistant should support these core use cases:
1. Ask and persist traveler preferences: food, budget, pace, interests, dislikes, walking tolerance.
2. Rank recommendations according to the user's profile, reservations, location, budget, distance, opening hours and trip context.
3. Explain why each recommendation fits the traveler.
4. Propose plans between existing reservations.

Architecture constraints:
- The MAUI app must never call OpenAI directly.
- OpenAI API calls must happen only in the backend/BFF.
- The backend must expose a safe endpoint such as POST /api/ai/travel-chat.
- The assistant must use application tools/functions to access reservations, profile, recommendations and itinerary data.
- The model must not invent reservations or persisted user data.
- Responses intended for the app must be structured JSON that can render chat messages, recommendation cards and suggested replies.
- Prefer deterministic scoring for ranking, and use the model mainly for natural-language reasoning, explanation and conversational flow.
- Do not store API keys in mobile code or appsettings committed to git.
- Add tests for orchestration, scoring, authorization and JSON response shape.

The skill should include:
- backend implementation rules
- MAUI UI rules
- data contracts
- tool/function definitions
- prompt template
- testing checklist
- security/privacy checklist
- MVP vertical slice guidance

Create the skill as .agents/skills/travel-ai-assistant/SKILL.md.
```

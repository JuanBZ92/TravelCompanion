# Prompt 03 - Implement MVP vertical slice

Use this prompt once the skill exists and you want the first working slice.

```text
Use the $travel-ai-assistant skill.

Implement the first MVP vertical slice for the travel chatbot:

"Propose a plan between my existing reservations today."

Please inspect the current repo structure first and adapt to existing conventions.

Expected backend work:
- Add POST /api/ai/travel-chat.
- Add request/response DTOs.
- Add AI orchestration service.
- Add interfaces for user profile, reservations, recommendations and ranking.
- Add deterministic ranking service.
- Add structured response model.
- Add tests for orchestration and ranking.
- Do not call OpenAI from MAUI.
- Do not commit secrets.

Expected MAUI work:
- Add a basic chat screen or viewmodel if the app structure exists.
- Render assistant message, recommendation cards and suggested replies.
- Keep UI simple for now.

If a dependency or domain service already exists, reuse it.
If something is missing, create interfaces and mock/fake implementations so the vertical slice compiles and is testable.
```

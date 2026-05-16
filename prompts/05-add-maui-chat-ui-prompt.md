# Prompt 05 - Add MAUI chat UI

Use this after the backend endpoint exists.

```text
Use the $travel-ai-assistant skill.

Add or update the .NET MAUI UI for the travel AI assistant.

Requirements:
- Add a chat screen or integrate into the existing navigation.
- Add a viewmodel that calls POST /api/ai/travel-chat through an existing API client pattern.
- Render assistant/user messages.
- Render recommendation cards from the structured backend response.
- Render suggested replies as chips/buttons.
- Show loading state.
- Show friendly error state.
- Do not call OpenAI directly.
- Do not store API keys or secrets in the mobile app.
- Keep the UI simple and testable.

If the project already uses MVVM, CommunityToolkit.Mvvm, dependency injection, or an existing HTTP client abstraction, follow the existing pattern.
```

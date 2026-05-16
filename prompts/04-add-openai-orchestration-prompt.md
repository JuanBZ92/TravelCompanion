# Prompt 04 - Add OpenAI orchestration

Use this after the MVP works with fake/mock orchestration, or if you already have backend services.

```text
Use the $travel-ai-assistant skill.

Add OpenAI orchestration to the existing travel chat backend.

Requirements:
- The MAUI app must continue calling only the backend.
- OpenAI API key must be loaded from secure server-side configuration.
- Add an abstraction such as ITravelAiModelClient or IOpenAiTravelClient.
- Use tool/function calling for app data access.
- Use structured outputs for the final response consumed by MAUI.
- Keep deterministic ranking outside the model.
- Do not send unnecessary PII to the model.
- Add tests with a fake model client.
- Add graceful fallback behavior if the model call fails.

The model must not invent:
- reservations
- prices
- opening hours
- distances
- persisted user preferences
- booking status

Return structured JSON compatible with TravelChatResponse.
```

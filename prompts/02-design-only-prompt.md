# Prompt 02 - Design only, no code changes

Use this prompt before implementing. It forces Codex to inspect the repo and propose a concrete plan.

```text
Use the $travel-ai-assistant skill.

Analyze this repository and propose an implementation plan for adding a travel AI chatbot.

Focus on:
- current backend architecture
- current MAUI architecture
- where to place AI orchestration
- required DTOs
- required services/interfaces
- testing strategy
- security risks
- MVP vertical slice

Do not modify files yet.

Produce a concrete step-by-step plan with proposed file paths.
Be specific about where each class, interface, DTO, test and MAUI view/viewmodel should live.
```

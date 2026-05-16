# Travel AI Assistant Skill Package v2

Este ZIP contiene una skill de Codex para implementar un chatbot/asistente de viajes en una aplicación **.NET MAUI + backend .NET**.

## Cambio principal de esta versión

Los documentos y checklists ahora están dentro de la propia skill como referencias internas:

```text
.agents/
  skills/
    travel-ai-assistant/
      SKILL.md
      references/
        architecture.md
        backend-contracts.md
        deterministic-ranking.md
        prompt-template.md
        security-privacy-checklist.md
        testing-checklist.md
        definition-of-done.md
```

Esto hace que Codex pueda usarlos como material de apoyo cuando la skill se active.

Los prompts siguen quedando afuera, porque son para que vos los copies manualmente en Codex:

```text
prompts/
  01-create-skill-prompt.md
  02-design-only-prompt.md
  03-implement-mvp-vertical-slice-prompt.md
  04-add-openai-orchestration-prompt.md
  05-add-maui-chat-ui-prompt.md
```

## Cómo usarlo

Copiá esta carpeta dentro de la raíz de tu repo:

```text
.agents/skills/travel-ai-assistant
```

Luego, en Codex, podés usar alguno de los prompts de la carpeta `prompts`.

Recomendación de orden:

1. Usar `02-design-only-prompt.md` para que Codex inspeccione el repo y proponga un plan.
2. Usar `03-implement-mvp-vertical-slice-prompt.md` para implementar el primer flujo.
3. Usar `04-add-openai-orchestration-prompt.md` para conectar OpenAI con tools/function calling.
4. Usar `05-add-maui-chat-ui-prompt.md` para crear o adaptar la UI de chat en MAUI.

## Idea central

El asistente no debe ser un chatbot libre y desconectado. Debe ser un **asistente de viaje controlado por tu backend**, con herramientas seguras para consultar:

- perfil del viajero
- preferencias
- reservas existentes
- recomendaciones
- itinerario

La app MAUI nunca debe llamar directo a OpenAI. Toda llamada a OpenAI debe pasar por backend/BFF.

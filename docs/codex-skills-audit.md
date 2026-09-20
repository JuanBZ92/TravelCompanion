# Instalación y catálogo de skills

Revisión local: 2026-09-20. Por petición posterior del usuario, se deshabilitaron globalmente las 31 skills de la lista. No se borraron archivos.

## Instalación completada

- Context7: MCP remoto global `https://mcp.context7.com/mcp`, registrado mediante
  `codex mcp add context7 --url https://mcp.context7.com/mcp`.
  Se verificaron initialize, tools/list y una consulta resolve-library-id sin
  credenciales. Expone resolve-library-id y query-docs. Se canceló el login OAuth
  opcional iniciado por el instalador; no se autenticó ninguna cuenta.
  Para autenticarlo posteriormente: `codex mcp login context7`.
  Mantener la regla global de consultar sólo cuando se necesite documentación actual.
  Estar disponible añade herramientas; sus respuestas también consumen contexto.
- Caveman: skill de JuliusBrussee/caveman, commit
  `3ee70a102609e550bd2e68004bf5990a9341c851`, instalada con el helper Skill Installer
  en `C:/Users/juanb/.codex/skills/caveman`.
  El catálogo generado por Codex ya reconoce caveman. Para invocación explícita:
  `$caveman lite`. Lite conserva frases completas; no se forzó un estilo permanente.
  Se instaló sólo la skill, sin proxy, middleware ni hooks. No comprime el contexto
  entrante ni cambia el enrutamiento de las solicitudes de Codex.
- La skill estará disponible en el siguiente turno; si Desktop no actualiza la lista,
  reiniciar y abrir una tarea nueva. Context7 requiere que la sesión cargue el MCP nuevo.

## Efecto sobre contexto y tokens

Codex carga nombre/descripción de las skills disponibles, y las instrucciones completas
al usarlas. Tener skills sin invocar sí añade contexto de catálogo, pero no equivale
a cargar todos sus SKILL.md ni sus referencias.

El render local de `codex debug prompt-input` contiene 93 entradas de skills y
21.467 caracteres en sus líneas de catálogo (incluidas rutas, excluidos encabezados).
Los 31 candidatos de abajo suman 5.940 caracteres en esas líneas. Estas medidas son
caracteres, no tokens facturados ni ahorro garantizado: intervienen el catálogo que
cada superficie expone, caché, tokenizador y activación efectiva.

Caveman añade su propia entrada e instrucciones cuando se usa. Reduce principalmente
prosa de salida; el ahorro neto de una sesión no se ha medido. Context7 aporta
documentación actual y no es un compresor de tokens.

## 31 candidatos no esenciales para el stack actual

No esenciales para TravelCompanion no significa inútiles en otros proyectos.

| Área | Skills candidatas | Motivo |
| --- | --- | --- |
| Ansible | ansible-generator, ansible-validator | Infraestructura actual con Terraform |
| Figma | figma, figma-code-connect-components, figma-create-design-system-rules, figma-create-new-file, figma-generate-design, figma-generate-library, figma-implement-design, figma-use | Sólo necesarias al trabajar expresamente con Figma |
| Fluent Bit | fluentbit-generator, fluentbit-validator | Sin necesidad identificada en el mapa del proyecto |
| GitLab CI | gitlab-ci-generator, gitlab-ci-validator | El repo tiene Azure Pipelines/GitHub |
| Godot | godot-game-development | Aplicación de viajes, no juego Godot |
| Helm | helm-generator, helm-validator | No forma parte del despliegue identificado |
| Jenkins | jenkinsfile-generator, jenkinsfile-validator | No forma parte del CI identificado |
| Kubernetes | k8s-debug, k8s-yaml-generator, k8s-yaml-validator | No forma parte del despliegue identificado |
| Loki | logql-generator, loki-config-generator | Sin necesidad identificada |
| Make | makefile-generator, makefile-validator | Flujo principal dotnet/PowerShell |
| Prometheus | promql-generator, promql-validator | Sin necesidad identificada |
| Terragrunt | terragrunt-generator, terragrunt-validator | Terraform existente; Terragrunt no identificado |
| WinUI | winui-app | App MAUI; útil sólo para trabajo WinUI específico |

## Opcionales según frecuencia

- plugin-creator, skill-creator, skill-installer: creación/gestión de extensiones.
- security-ownership-map, security-threat-model: auditorías específicas; mantener acceso bajo demanda.
- imagegen, visualize: recursos visuales y explicaciones gráficas.
- bash-script-generator, bash-script-validator: si se trabaja con shell/CI Linux.
- maui-aspire, maui-hybridwebview, maui-speech-to-text, maui-sqlite-database:
  valorar por funcionalidad concreta; no desactivar todo MAUI por su tamaño.
- maui-performance y maui-performance-reviewer: posible solapamiento, no duplicados demostrados.

Conservar como base .NET/MAUI, travel-ai-assistant, mobile-azure-mvp-architecture,
Azure Pipelines, Terraform, Docker, GitHub Actions, Render y las herramientas habituales.
Sheets sigue teniendo utilidad para el catálogo Excel.

Preferir deshabilitar antes de borrar. En la prueba previa, los overrides de skills
locales no surtieron efecto; los globales sí, afectando todos los proyectos.
El usuario autorizó esta lista y se aplicó en ~/.codex/config.toml.
Validación: las 31 entradas desaparecieron del catálogo generado por Codex;
Caveman, ASP.NET Core, MAUI, Terraform y viajes permanecen disponibles.
Se guardó una copia de la configuración previa junto al archivo global, con nombre
config.toml.before-disable-skills-20260920-141053.bak.
Para reactivar una skill, cambiar su enabled a true y recargar Codex.

Fuentes: [skills de Codex](https://learn.chatgpt.com/docs/build-skills),
[Context7 para Codex](https://context7.com/docs/clients/codex),
[Caveman](https://github.com/JuliusBrussee/caveman).

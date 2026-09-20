# Contexto de Codex: auditoría y mantenimiento

Auditoría: 2026-09-20. Leer este documento sólo para ajustar Codex.

## Hallazgos y cambios
- El AGENTS global estaba vacío y el repositorio no tenía AGENTS raíz ni anidados.
  No existían instrucciones duplicadas ni documentación pesada que extraer.
- Se añadieron 24 líneas globales de investigación, salida concisa y continuidad;
  un mapa del repo y reglas específicas para API y Mobile.
- El README describe un paquete de skills, no el estado completo de la aplicación.
  El mapa apunta a docs/TECHNICAL.md y a documentos por tema; contrastar con código.
- Había 11 plugins habilitados globalmente y 85 directorios en ~/.codex/skills
  (incluidos directorios de soporte; no equivale al número de skills cargadas).
- .codex/config.toml deshabilita sólo en este proyecto documents, presentations,
  pdf, template-creator y google-drive. No desinstala ni desconecta cuentas.
- No se modificaron runtime, dependencias, permisos, modelo ni scripts de despliegue.
  Los comandos principales de build/test están en AGENTS.md con salida minimal.

## Integraciones
- Mantener GitHub para trabajo de repositorio, codex-app-tools y la integración
  de navegador/escritorio existente. node_repl forma parte de esa integración;
  no deshabilitarlo aisladamente por parecer un servidor adicional.
- Sheets se conserva por el catálogo Excel del proyecto; visualize permanece disponible.
- Activar documentos, presentaciones, PDF, plantillas o Drive cuando la tarea los necesite.
  En .codex/config.toml, cambiar el enabled del plugin a true o quitar su bloque
  para heredar la configuración global. Reiniciar Codex y abrir una tarea nueva.
- En la auditoría inicial no estaban Context7 ni Caveman. Posteriormente, a petición
  del usuario, se registró Context7 y se instaló la skill Caveman; ver
  `docs/codex-skills-audit.md` para instalación, alcance y candidatos a desactivar.
- Si se incorpora Context7, consultar sólo documentación actual necesaria para la tarea.

## Skills especializadas: limitación verificada
La prueba con `codex debug prompt-input` mantuvo las skills deshabilitadas mediante
configuración de proyecto. Una prueba temporal en configuración global sí excluyó
ansible-generator; se restauró inmediatamente el archivo global original.
Se retiraron los overrides locales ineficaces. Posteriormente el usuario autorizó
deshabilitar globalmente las 31 skills listadas en docs/codex-skills-audit.md;
la exclusión se verificó en el catálogo generado por Codex.

Candidatos para desactivar globalmente y reactivar según necesidad: Ansible,
Fluent Bit, GitLab CI, Godot, Helm, Jenkins, Kubernetes, Loki/LogQL, Makefile,
PromQL, Terragrunt y WinUI. Conservar .NET/MAUI, viajes, Azure, Terraform, Docker,
GitHub Actions, Render y seguridad. No borrar skills ni modificar cachés de plugins.
Ejemplo de ajuste reversible en ~/.codex/config.toml (ruta de esta instalación):

```toml
[[skills.config]]
path = 'C:\Users\juanb\.codex\skills\ansible-generator\SKILL.md'
enabled = false
```

Cambiar a true o eliminar esa entrada para reactivar; reiniciar Codex.
No se bajó el límite del catálogo, de instrucciones o de salida: podría ocultar
reglas, errores o capacidades útiles en vez de seleccionar lo irrelevante.

## Validación y límites
- El render de contexto de la CLI aceptó la configuración y excluyó cuatro entradas
  de skills de plugins: documents, pdf, presentations y template-creator.
- Drive no figuraba en ese catálogo de CLI inicial; su ausencia no valida por sí sola
  el efecto en Desktop. Comprobar el catálogo de una nueva tarea tras reiniciar.
- Esta conversación conserva el contexto ya recibido. No hay ahorro retroactivo.
- No se midieron tokens facturados ni ahorro porcentual; los caracteres totales del
  render no bajaron en la prueba. No equiparar menos entradas con ahorro ya demostrado.
- Mayor oportunidad: catálogo global de skills y definiciones de herramientas.
  AGENTS no era una fuente de exceso; ahora añade un mapa breve para ahorrar exploración.
  docs ya estaba separado. El ahorro en comandos y sesiones largas depende del uso;
  no se analizaron historiales ni logs masivos para estimarlo.
- No se ejecutaron tests de aplicación: los cambios sólo afectan instrucciones/configuración.

Referencias oficiales: [configuración](https://learn.chatgpt.com/docs/config-file/config-reference)
y [skills](https://learn.chatgpt.com/docs/build-skills).

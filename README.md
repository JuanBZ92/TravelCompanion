# TravelCompanion · YUKU Japan

Aplicación de viajes en .NET 10: cliente .NET MAUI, API y administración ASP.NET Core, contratos compartidos y worker de notificaciones. PostgreSQL almacena contenido, viajes, accesos y cambios sincronizados.

## Producto actual

- **Viaje:** agenda por día, búsqueda, resumen de mañana, reservas y alternativas flexibles; planificación de uno o varios días con propuesta antes de aplicar.
- **Mapa y Asistente:** recomendaciones curadas, filtros, favoritos y planificación según permisos. Las llamadas a IA se realizan en el backend.
- **Journal:** recuerdos libres o vinculados a actividades, borradores y fotos locales; las entradas confirmadas sincronizan su texto.
- **Carpeta de viaje:** preparación por categorías y documentos personales cifrados disponibles también para usuarios gratuitos con viaje activo.
- **Gastos:** registro básico, cotizaciones y sincronización; desglose y exportación sujetos a los permisos actuales del pase.
- **Cuenta y pase:** recuperación por correo, cambio de viaje, desbloqueo con biometría o PIN/correo, compra y restauración con verificación del servidor.

Los archivos personales, fotos y borradores permanecen en el dispositivo. Las compras reales dependen de la configuración de lanzamiento. Consultar [funcionalidades y límites](docs/FUNCTIONAL.md) y [runbook de lanzamiento](docs/japan-launch.md).

## Repositorio

| Directorio | Responsabilidad |
|---|---|
| `src/TravelCompanion.Api` | API, Razor Pages admin, EF Core y migraciones PostgreSQL |
| `src/TravelCompanion.Mobile` | App MAUI, pantallas, servicios y almacenamiento local |
| `src/TravelCompanion.Shared` | DTOs y políticas compartidas |
| `src/TravelCompanion.Notifications.Worker` | Procesamiento de notificaciones |
| `tests` | Pruebas API, móviles y Shared |
| `tools`, `scripts`, `infra` | Desarrollo, mediciones, publicación e infraestructura |

## Desarrollo y validación

Instalar el SDK .NET 10 y el workload de la plataforma MAUI utilizada. El desarrollo local con PostgreSQL se describe en [TECHNICAL](docs/TECHNICAL.md). Configurar los secretos fuera de archivos versionados; una APK de revisión debe usar un backend local y una identidad de aplicación separada.

```powershell
dotnet test tests/TravelCompanion.Shared.Tests/TravelCompanion.Shared.Tests.csproj --verbosity minimal
dotnet test tests/TravelCompanion.Api.Tests/TravelCompanion.Api.Tests.csproj --verbosity minimal
dotnet test tests/TravelCompanion.Mobile.Tests/TravelCompanion.Mobile.Tests.csproj --verbosity minimal
dotnet build src/TravelCompanion.Mobile/TravelCompanion.Mobile.csproj -f net10.0-android --verbosity minimal
```

Las pruebas que requieren PostgreSQL real deben ejecutarse con su base local configurada. Los tests de lógica móvil no sustituyen la compilación Android ni la revisión visual en dispositivo.

`tools/DayPlanReview` ofrece datos sintéticos para una base local aislada. `--daily-ux` crea una cuenta nueva para la fecha actual de Asia/Tokyo, con hotel, reservas y planes flexibles para hoy y mañana. No modifica cuentas existentes ni ejecuta el seed general. El programa rechaza cualquier conexión distinta de `127.0.0.1:55439/tc_dayplanner_review`.

## Publicación y mantenimiento

Las migraciones de producción se aplican explícitamente; no forman parte del arranque normal. Publicar únicamente cambios validados siguiendo [production-runbook](docs/production-runbook.md) y el procedimiento del entorno correspondiente: [Render](docs/render-deploy.md) o [infraestructura Azure](docs/INFRASTRUCTURE.md).

Las reglas de contribución, estilo editorial, accesibilidad y aislamiento de sesiones están en [AGENTS.md](AGENTS.md). Los prompts y referencias del asistente en `prompts` y `.agents/skills` son material auxiliar; este repositorio es la aplicación completa.

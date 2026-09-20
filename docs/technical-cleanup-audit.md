# Auditoría técnica y limpieza conservadora

Fecha: 20 de septiembre de 2026. Base: `a0bd13d` (`duracion opcional`), rama `newapproach`, árbol inicialmente limpio.

## Resultado y alcance

Se aplicaron dos lotes pequeños: eliminación de código móvil sin consumidores y consolidación de dos duplicaciones exactas. No se modificaron contratos HTTP, DTO, autorización, serialización, modelos persistidos, migraciones, dependencias ni configuración de despliegue. La auditoría no certifica que todo el repositorio esté libre de errores ni sustituye pruebas funcionales en dispositivos.

La exploración combinó búsquedas dirigidas con `rg`, símbolos y referencias de Serena, revisión de XAML y registros DI, y detección estática de candidatos privados y cuerpos duplicados. Las coincidencias automáticas se trataron como candidatos, no como autorización para borrar. Se revisaron los diffs relevantes; RTK se utilizó para resumir el diff final.

Durante la ejecución aparecieron modificaciones concurrentes ajenas en Assistant, navegación, itinerarios, localización y contratos. Se conservaron. Para atribuir correctamente los resultados, el parche exclusivo de esta auditoría se aplicó sobre `a0bd13d` en `C:\Users\juanb\.codex\worktrees\audit-validation\TravelCompanion`. Los resultados finales corresponden a esa copia, no a una certificación del árbol combinado que sigue cambiando.

## Baseline previo a las modificaciones

| Comprobación | Resultado |
|---|---|
| API, Worker y Android Debug | Compilan; cero errores y advertencias |
| API | 264 aprobadas, 4 omitidas |
| Shared | 21 aprobadas |
| Lógica móvil | 40 aprobadas |
| Total | 325 aprobadas, 4 omitidas, cero fallos |

Las cuatro omisiones corresponden a `PostgresItineraryIdempotencyTests`: cuota simultánea, versiones de catálogo, consumo OTP y reintentos de itinerario. No está configurada `TRAVELCOMPANION_TEST_POSTGRES`.

## Lote 1: código sin uso — SAFE

Se comprobaron referencias C#, enlaces XAML y comandos generados antes de eliminar:

- `ScheduleViewModel`: `LoadRoutesAsync`, `LoadRouteAsync`, `CreateAllocatedRecommendationIdsBefore`, `RebuildDefaultTypeSections`, `GetInitialScheduleType` y `FormatShortDate`.
- `SchedulePage`: handlers `OnTypeFilterTapped`, `OnCityFilterTapped`, `OnScheduleItemTapped` y `OnTimelineItemTapped` sin enlace activo.
- `TravelChatPage`: handlers `OnOpenRecommendationDetailClicked`, `OnRequestLessWalkingClicked`, `OnMarkUsefulClicked`, `OnMarkNotUsefulClicked` y `OnHideSimilarClicked` sin enlace activo.
- `TravelChatViewModel`: `QueueSaveItineraryItemAsync`, sin consumidores; también su dependencia de constructor `OfflineMutationQueueService`, usada exclusivamente allí.

Se eliminó además `CancelRouteLoading` y sus dos llamadas internas de ciclo de vida: cancelaba un campo que únicamente asignaba la carga automática ya inaccesible. Es un cambio de superficie pública del ViewModel móvil, no de contrato compartido/API. No se encontraron consumidores externos, enlaces ni acceso por reflexión. Se conserva el cálculo manual `CalculateRouteAsync`, su comando y su caché. Se mantienen la sincronización offline y los comandos reales de las tarjetas.

Validación del lote: Android compila y las 40 pruebas móviles pasan. La eliminación de handlers no demuestra por sí sola cobertura funcional de las pantallas; queda la revisión manual indicada abajo.

## Lote 2: duplicación exacta — LIKELY SAFE, validada automáticamente

1. Los dos `FormatPriceLevel` de lista y detalle tenían cuerpos idénticos. Ahora usan `RecommendationPriceFormatter`, interno al cliente. Se conservaron todos los alias, tratamiento de null/vacíos, espacios, mayúsculas y textos desconocidos. Las etiquetas españolas existentes no se tradujeron en esta refactorización para evitar alterar comportamiento.
2. `SlowDbCommandLoggingInterceptor.CommandFailed` repetía el helper que ya usa el camino asíncrono. Ahora delega en él, conservando nivel, excepción, plantilla y argumentos del log.

Se añadieron 17 casos de prueba del formato de precios y el enlace del archivo al proyecto de pruebas móviles. No se eliminaron pruebas.

## Áreas revisadas y decisiones

| Área | Hallazgo / decisión |
|---|---|
| Async y cancelación | Las coincidencias examinadas de `.Result` eran propiedades de DTO/resultados, no esperas sobre tareas. Se conserva cancelación y manejo de errores de los caminos activos. |
| LINQ, nulls y validaciones | No surgió una simplificación suficientemente demostrada para cambiar reglas de negocio. No se borraron validaciones activas. |
| DI y wrappers | No se detectaron registros genéricos idénticos redundantes en API/Worker. Las siete inscripciones de `ISessionStateResettable` son intencionales: logout enumera todos los estados. |
| API y errores | Se mantienen wrappers por endpoint y sus códigos de respuesta. Los endpoints retirados que responden 410 siguen siendo contratos de compatibilidad. |
| Persistencia y configuración | Se preservan entidades, migraciones, opciones y seeding activo. Las plantillas de rutas siguen siendo utilizadas por Admin. |
| Frontend y código generado | Se comprobaron XAML, comandos generados y ciclo de vida. No se eliminaron métodos `[RelayCommand]` por ausencia de llamadas C# directas. |
| Pruebas | Las factorías InMemory repetidas usan identificadores independientes intencionalmente; no se creó una fixture global que altere el aislamiento. |
| Rendimiento | La retirada afecta caminos muertos; no se afirma una mejora de latencia, memoria o FPS sin medición. No se reescribieron ViewModels grandes. |
| Logging | Se consolidó una duplicación exacta. Los catches de workers que preservan cancelación, scope y continuidad del bucle permanecen. |

## Candidatos conservados y clasificación

- **REQUIRES REVIEW:** `FreeTrialAccessService.RequireAssistantQuotaAsync` y `RecordSuccessfulAssistantRequestAsync` parecen sin consumidores locales, pero son públicos y afectan cuotas/permisos. No se retiraron por una búsqueda negativa.
- **REQUIRES REVIEW:** mapeos de entitlements aparentemente iguales. Algunos ordenan por nivel y fecha; otros no. Una consolidación global podría cambiar orden, acceso o expiración.
- **REQUIRES REVIEW:** Haversine/distancias en mapa, ranking y preview free. Difieren tipos, nullabilidad y redondeo; su impacto sobre radio permitido y ranking impide considerarlos equivalentes.
- **REQUIRES REVIEW:** helpers de caché de `MobileDiscoverStore` y `MobileBootstrapStore`. Comparten fragmentos, pero cada uno mantiene generación y contexto de cuenta; extraerlos requiere pruebas específicas contra respuestas obsoletas y cambios de sesión.
- **REQUIRES REVIEW:** importadores con normalización y lectura de celdas similares; algunos reciben `IXLRow` y otros `IXLRangeRow`. No se añadió una abstracción por semejanza textual.
- **REQUIRES REVIEW:** tablas de ranking de presupuesto coincidentes. No se movió lógica entre capas sin justificar una política compartida.
- **LIKELY SAFE, pendiente:** `DatabaseSeeder.RemoveLegacySeedDataAsync` y sus listas privadas parecen inaccesibles. Se conservaron en esta pasada; cualquier retirada futura debe mantener el seeding activo y su prueba de conservación de contenido.
- **Duplicación intencional:** suscripciones y desuscripciones de páginas, factorías de bases de prueba y adaptadores de proveedores. No justifican una clase base o un wrapper adicional.
- **DO NOT TOUCH:** contratos públicos de integración, endpoints retirados, entidades persistidas, migraciones y controles de autorización. La falta de referencias internas no demuestra ausencia de consumidores externos.

## Comandos y evidencia

Desde la raíz correspondiente:

```powershell
dotnet build src/TravelCompanion.Api/TravelCompanion.Api.csproj --verbosity minimal
dotnet build src/TravelCompanion.Notifications.Worker/TravelCompanion.Notifications.Worker.csproj --verbosity minimal
dotnet build src/TravelCompanion.Mobile/TravelCompanion.Mobile.csproj -f net10.0-android --verbosity minimal
dotnet test tests/TravelCompanion.Api.Tests/TravelCompanion.Api.Tests.csproj --verbosity minimal
dotnet test tests/TravelCompanion.Shared.Tests/TravelCompanion.Shared.Tests.csproj --verbosity minimal
dotnet test tests/TravelCompanion.Mobile.Tests/TravelCompanion.Mobile.Tests.csproj --verbosity minimal
git diff --check
```

Se utilizaron `-m:1`, `-p:UseSharedCompilation=false`, `DOTNET_PROCESSOR_COUNT=2` y GC de estación para limitar recursos. Android utiliza `JavaMaximumHeapSize=1536m` y Java con dos procesadores/SerialGC. Son ajustes del comando, no cambios del proyecto.

Logs locales bajo `%TEMP%`: `audit-baseline-*.log`, `audit-batch1-*.log`, `audit-batch2-*.log` y `audit-isolated-*.log`. Un build intermedio del árbol compartido falló por `RecommendationDto.DisplayTitle` en el archivo concurrente `TravelChatService.DayPlanning.cs`; no pertenece al parche auditado. La API aislada compila y sus pruebas pasan.

## Métricas del parche exclusivo

Sin contar este informe: 10 archivos afectados, 49 líneas añadidas y 258 eliminadas; reducción neta de 209 líneas. Incluye los dos archivos nuevos: formatter (16 líneas) y pruebas (27 líneas). No se eliminaron archivos ni clases.

Se retiraron 17 métodos obsoletos (16 privados y el cancelador interno expuesto públicamente), un campo y una dependencia de constructor sin uso. Además se consolidaron dos métodos de formato duplicados en uno y un cuerpo de logging en su helper existente. Se añadieron 17 casos de prueba; ninguno fue eliminado.

Validación final aislada: API 264 aprobadas y 4 omitidas, Shared 21 aprobadas, móvil 57 aprobadas: **342 aprobadas y cero fallos de pruebas**. Worker compila con cero advertencias y errores, incluyendo API como dependencia. `git diff --check` no detecta errores de whitespace.

Android Debug también compila y firma correctamente en la copia aislada: cero advertencias y errores. El primer intento terminó con `MSB6006` en D8 (`java.exe`, código 1, sin causa más específica en el log mínimo). El reintento con heap de 1024 MB y log normal completó en 1 min 44 s. No se alteró código para resolverlo ni se presenta la causa del primer fallo como confirmada.

## Limitaciones y revisión manual pendiente

- Ejecutar las cuatro pruebas contra PostgreSQL real; InMemory no acredita concurrencia relacional.
- Verificar Today/Assistant, navegación, detalle/lista de recomendaciones y cambio de sesión en dispositivo Android. El build XAML no sustituye interacción real.
- Compilar/probar iOS en un entorno compatible.
- Volver a validar el árbol combinado cuando finalice el trabajo concurrente; no atribuir a esta auditoría cambios funcionales de esa otra implementación.
- No se hicieron despliegues, publicación de APK, commits, benchmarks ni validación de compras sandbox.

La revisión final del parche exclusivo no muestra cambios en autorización, serialización, persistencia o validaciones activas. Los cambios de constructor y cancelación del ViewModel son los únicos ajustes de superficie móvil que merecen atención al integrar con trabajo simultáneo.

## Segunda revisión: resolución de REQUIRES REVIEW

Esta sección actualiza las decisiones de los seis candidatos anteriores. No incluye cambios de seeding, migraciones ni funcionalidades nuevas.

| Candidato | Decisión | Estado de verificación |
|---|---|---|
| Presupuesto | Mejorado | Caracterización antes/después |
| Importación | Mejorado; adaptadores conservados | Caracterización y libros de prueba antes/después |
| Entitlements | Mejorado | DTO/JSON y orden de cuatro consumidores |
| Distancias | Conservado justificadamente | Frontera free y precisión del backend; dispositivo pendiente |
| Cachés | Conservado justificadamente | Revisión estática; carreras reales pendientes |
| Cuotas antiguas | Dos métodos eliminados | Consumidores compilados y PostgreSQL real |

### Base y aislamiento

Se creó `C:\Users\juanb\.codex\worktrees\review-candidates\TravelCompanion` sobre `a0bd13d` y se copiaron los 27 archivos modificados/nuevos existentes al comenzar, incluidos los cambios concurrentes de Assistant. Esa instantánea se registró en el índice del worktree para separar el parche nuevo. No se reutilizó el estado antiguo de `audit-validation`.

Baseline de esta instantánea: API y Worker compilan; API 270 pruebas aprobadas y cuatro PostgreSQL omitidas, Shared 21 y Mobile 58: 349 aprobadas. Android compiló C#, pero el empaquetado falló por agotamiento de memoria y D8. El fallo precede a los cambios de esta revisión. Otro fallo de memoria en el compilador de tests desapareció al limitar el heap de .NET a 512 MB; no se desactivaron analizadores ni se cambió el proyecto.

Las pruebas PostgreSQL se habilitaron posteriormente usando un contenedor PostgreSQL 17 temporal, expuesto solo en loopback. Se aplicaron las migraciones existentes exclusivamente en esquemas de prueba. No se utilizó la base local ni producción.

### A. Presupuesto — mejorado

**Evidencia:** `PriceRank` y `BudgetRank` contenían exactamente los mismos alias y valor por defecto. Sus consumidores aplican filtros y desempates distintos, que permanecen en cada servicio.

**Cambio:** helper interno `RecommendationBudget.GetRank`. No se movió lógica al móvil ni se mezcló con el formatter de etiquetas.

**Verificación:** `BudgetRankingCharacterizationTests` añade 21 casos de puntuaciones, alias, null/vacíos, desconocidos, orden por título, modos de precio y selección múltiple. Junto con `DeterministicRecommendationRankerTests`, 26 pruebas pasan antes y después. Las pruebas llaman al ranker real y a los pasos reales de ordenación; no comparan dos copias nuevas del algoritmo.

**Límite:** no se afirma una mejora de rendimiento; se elimina mantenimiento duplicado manteniendo resultados.

### B. Importación — mejorado parcialmente; adaptadores conservados

**Evidencia:** normalización, diacríticos y expresión regular de espacios son idénticos. Los adaptadores reciben tipos distintos (`IXLRow`/`IXLRangeRow`) y el importador de viajes tiene aliases heredados y criterios de fila vacía propios.

**Cambio:** helper interno `WorkbookText` para las transformaciones puras y una sola expresión regular generada. Se mantienen `CreateHeaderMap`, `ReadCell`, validación, errores, filas y persistencia en sus importadores; no se añadió una jerarquía de adaptadores.

**Verificación:** siete casos nuevos prueban null/vacíos, acentos, Unicode combinado, japonés, espacios, cabeceras duplicadas (gana la primera), columnas ausentes y lectura recortada. La suite de ambos importadores ejecuta sus libros reales de prueba, previews, errores e importaciones idempotentes. Total del lote: 19 aprobadas antes y después.

**Límite:** se mantiene la semántica Unicode existente; eliminar marcas diacríticas puede afectar caracteres de otros idiomas. No se introdujo una política de normalización nueva bajo apariencia de limpieza.

### C. Entitlements — mejorado

**Evidencia:** los cuatro cuerpos de proyección eran idénticos. Users/Mobile ordenan por nivel y fecha; planificación/Today preservan el orden de entrada. El filtrado temporal se realiza antes de la proyección.

**Cambio:** `UserEntitlementProjection.Map`, interno, recibe la lista ya materializada. Cada consumidor conserva su reloj, filtro de expiración y ordenamiento. El helper no concede permisos, no consulta la base y no selecciona pases.

**Verificación:** dos escenarios recorren los cuatro consumidores: usuario sin permisos y mezcla de permisos activos/vencidos, niveles repetidos, destinos/paquetes repetidos y fechas con offset. Se comparan JSON completo y orden de IDs. Pasan antes del cambio; después, junto con endpoints free y Assistant, pasan 65 pruebas.

**Límite:** no se cambió la decisión de expiración exactamente en el instante actual; el reloj sigue en el consumidor original.

### D. Distancias — conservado justificadamente

**Evidencia:** las implementaciones examinadas usan radio terrestre de 6371 km, pero difieren en tipos y redondeo. FreeMap usa decimal a tres decimales y lo reutiliza la elegibilidad free; catálogo/mapa usa decimal a dos; ranker usa double a dos; Today usa decimal nullable a uno. El ranker descarta además una ubicación a más de 100 km y desempata por caminata/título.

**Decisión:** no crear un núcleo público en Shared para ahorrar unas líneas a costa de exponer opciones de precisión y conversiones entre capas. Se mantienen las variantes y sus consumidores.

**Verificación:** seis casos nuevos cubren origen idéntico, un grado sobre el ecuador, antimeridiano, polo, origen ausente y distancia free de 1,999 / 2,000 / 2,001 km. La prueba de frontera ejecuta el filtro real de acceso y el servicio real de mapa: incluye el límite, excluye el punto exterior, conserva el orden y oculta la recomendación bloqueada. Las pruebas existentes de ranking cubren ubicación lejana y los nuevos tests de presupuesto añaden desempate por título.

**Límite:** la fórmula privada de MapViewModel se revisó estáticamente; no se ejecutó ese ViewModel en dispositivo. No se extrapola la verificación del backend a una prueba móvil completa.

### E. Bootstrap/Discover — conservado justificadamente; pruebas de carreras pendientes

**Evidencia:** normalización y comparación de scope coinciden, pero los stores tienen estados y escrituras propios. El contexto incluye usuario, viaje, idioma, versión de sesión y generación. Bootstrap también actualiza agenda y emite notificaciones de UI.

**Decisión:** conservar todos los helpers, campos, locks, cancelaciones y claves. El proyecto móvil de tests enlaza fuentes seleccionadas y no ejecuta estos stores. Sus dependencias concretas acceden a `Preferences`, `SecureStorage`, `FileSystem` y `MainThread`; controlar las carreras completas exige incorporar un arnés móvil o separar esos accesos. No se hizo esa refactorización mayor para justificar una extracción pequeña.

**Verificación realizada:** revisión estática de captura de contexto, invalidación y comprobaciones antes/después de refresh. No se sustituyeron los stores por réplicas simplificadas en tests.

**Evidencia aún necesaria:** ejecutar los stores reales con respuestas HTTP retenidas y cambio de cuenta/viaje/idioma/logout durante cada await; comprobar memoria, archivos y eventos; repetir con cancelación y peticiones concurrentes. Las comprobaciones estáticas no acreditan aislamiento completo. En particular, hay escrituras asíncronas entre comprobaciones de contexto: esto requiere pruebas, no permite afirmar por sí solo un fallo confirmado.

### F. Métodos antiguos de cuota — eliminados

**Evidencia:** Serena no encontró referencias y las búsquedas dirigidas en API, Worker, herramientas, tests y documentación solo encontraban las declaraciones. Se revisaron reflexión y distribución: API es un proyecto Web; Worker y CatalogAdmin lo referencian directamente, sin publicación NuGet ni carga dinámica identificada. No eran endpoints ni parte de los DTO compartidos.

**Cambio:** retirar `FreeTrialAccessService.RequireAssistantQuotaAsync` y `RecordSuccessfulAssistantRequestAsync`. Se mantiene el servicio, su registro DI, permisos de edición, estado de prueba y restricciones geográficas. No se añadieron delegaciones que alteren semántica.

**Flujo vigente:** `AiController` reserva, confirma o cancela mediante `AssistantUsageService`, separando claves de chat y `full-day:`. Propuestas/rutas también emplean ese servicio. No se modificó ninguno de estos flujos.

**Verificación:** pruebas de límites independientes, cancelaciones sin consumo, doble confirmación e idempotencia; se reforzó la prueba existente para repetir reserva antes y después de confirmar tanto chat como Mejorar día. Las cuatro pruebas PostgreSQL ejecutan concurrencia real, incluidas cuotas. El lote posterior a la retirada pasó 26 pruebas de comercio, prueba free y PostgreSQL.

**Compatibilidad:** se retiran dos miembros públicos de una clase del backend, no un contrato HTTP. La evidencia cubre los consumidores distribuidos en este repositorio; no puede certificar binarios privados externos no inventariados. Las herramientas referenciadas se incluyen en la validación de compilación final.

### Evidencia reproducible y límites

Logs locales: `%TEMP%/review-baseline-*.log`, `review-A-before-retry.log`, `review-A-after.log`, `review-B-before.log`, `review-B-after.log`, `review-C-before.log`, `review-C-after.log`, `review-DF-postgres.log`, `review-F-after.log` y `review-final-*.log`.

Los comandos son los del apartado anterior, con filtros por las clases indicadas para cada lote. Para PostgreSQL se configura `TRAVELCOMPANION_TEST_POSTGRES` hacia una base de pruebas aislada. El límite de memoria es un ajuste temporal del entorno, sin cambios en archivos de proyecto.

No hay dispositivo conectado en `adb devices`. Today/Assistant, cambios de sesión y radio free no pudieron comprobarse manualmente. iOS requiere un host compatible. No se hicieron despliegues ni cambios en contratos HTTP, DTO, esquema o dependencias.

### Validación final e integración de la segunda revisión

La copia aislada pasó 310 pruebas API (incluidas las cuatro PostgreSQL), 21 Shared y 58 Mobile: **389 aprobadas, cero omitidas y cero fallos**. Se incorporaron 36 casos nuevos y se reforzó la prueba de idempotencia existente. Compilan API, Worker, CatalogAdmin y DevBootstrap; los tres builds explícitos finales de Worker/herramientas no tienen advertencias ni errores.

El parche nuevo se integró en el directorio principal mediante `git apply --check` y aplicación del diff respecto de la instantánea inicial, seguido de copia de los siete archivos nuevos de esta revisión. No se reemplazó el árbol completo. Se preservaron los cambios concurrentes de Assistant; siete archivos de ese trabajo habían cambiado respecto de la copia aislada. `git diff --check` pasa tras integrar.

Android Debug en la copia aislada terminó compilado, empaquetado y firmado, con cero advertencias/errores. El comando final limitó el heap de .NET a 256 MB y Java a 512 MB, y completó en 1 min 54 s. Los fallos de memoria del baseline quedan registrados; no se cambiaron paquetes ni configuración persistida para resolverlos.

Después de integrar, el árbol principal pasó **312 pruebas API, 21 Shared y 58 Mobile: 391 aprobadas, cero omitidas y cero fallos**, nuevamente contra PostgreSQL real. La diferencia de dos pruebas API respecto de la instantánea corresponde a cambios concurrentes de Assistant preservados durante la integración. Logs: `%TEMP%/review-integrated-Api-tests.log`, `review-integrated-Shared-tests.log` y `review-integrated-Mobile-tests.log`.

Worker y Android también compilan en el árbol integrado, con cero advertencias/errores. Android necesitó aumentar el límite de heap de .NET a 768 MB y Java a 1024 MB para generar stubs; el reintento completó en 1 min 15 s sin cambios de código (`review-integrated-android-retry.log`). La revisión final confirmó que todos los archivos de código de este parche coinciden con los validados en la copia aislada, ignorando terminadores CRLF. El contenedor PostgreSQL temporal se detuvo y eliminó al terminar las pruebas. No se realizaron commits ni publicación de APK.

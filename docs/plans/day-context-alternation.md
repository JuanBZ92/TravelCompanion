# Alternar A continuación / Mañana a un vistazo

Incremento acotado de Viaje, posterior a QA147. Se registra sobre HEAD
`51c996264bf45d7562b45bc8658d0f51d5cc6af9` y el árbol previo sin commit de QA147, cuya huella
`B76939D5ED3CA730F88E31FA15A7C2ECAA6AB98AA402080B035BC2BED502D060`
corresponde a 829 fuentes del [proof histórico](../../artifacts/native-acceptance-20261007/closure-evidence-147.json).
No se modifica ni recertifica la matriz o evidencia del plan anterior.

La [matriz incremental](day-context-alternation.json) usa el esquema 1 del
[comprobador existente](../../scripts/check-plan-completion.ps1). Registra 12 criterios
de implementación independientes, revisados y marcados como implementados en la
[revisión independiente](../../artifacts/day-context-20261007/review.md), incluidos
dos gaps detectados y resueltos antes de congelar las fuentes. Cada referencia usa
casos pertinentes del TRX; aprobar una clase no demuestra todos los escenarios.
Contexto/caché y navegación del ScheduleViewModel se registran como revisión de
fuente, con tests complementarios del resolver, sin afirmar una suite del VM completo.

| ID | Aceptación y comprobación concreta |
|---|---|
| dayctx01 | Bloque sólo para hoy en la zona del viaje; ayer/mañana y UTC distinto no confunden hoy. |
| dayctx02 | Un solo bloque; actividad antes de Mañana y sólo si mañana pertenece al viaje. |
| dayctx03 | Actividad activa antes de futura; terminadas/flexibles no bloquean Mañana; noche y check-in compatibles. |
| dayctx04 | Cronología, prioridad en empates e ID estable; mismo resultado con entradas invertidas. |
| dayctx05 | En curso / In progress y A continuación mediante recursos ES/EN. |
| dayctx06 | Antes del inicio, inicio, final y después del final; actualización por reloj sin refresh manual. |
| dayctx07 | Medianoche del viaje y último día; actividades nocturnas vigentes. |
| dayctx08 | Cambio de cuenta/viaje/fecha y respuesta tardía no publican contexto anterior. |
| dayctx09 | Carga sin datos confirmados no inventa final; caché confirmada conserva contenido en fallos. |
| dayctx10 | Acciones de actividad, Maps, documento y rato libre conservan contexto y condiciones. |
| dayctx11 | Resumen y acceso a Mañana conservan reserva/hotel/documentos, fecha y permisos. |
| dayctx12 | Diff sin permisos, endpoints, DTOs, tablas ni migraciones nuevos; caché y estilo compatibles. |

Fuentes: `TripDayOverview.cs`, `ScheduleViewModel.cs`, `SchedulePage.xaml`,
`UpcomingActivitySelector.cs`, recursos móviles ES/EN y navegación de Mañana.
Pruebas nuevas: `TravelCompanion.Mobile.Tests.TripDayContextTests` y
`TravelCompanion.Shared.Tests.UpcomingActivitySelectorTests`. Se requieren suites
**Mobile y Shared** y **Android Release**, con APK **QA148** del package
`com.yuku.travelcompanion.plannerpaidreview`. No se reutilizan los totales QA147
como validación de cambios posteriores. Los TRX actuales contienen **658/658 Mobile**
y **83/83 Shared**, sin fallos ni omisiones: [Mobile](../../artifacts/day-context-20261007/mobile-full-results/mobile-full.trx)
y [Shared](../../artifacts/day-context-20261007/shared-full-results/shared-full.trx).
Incluyen 14 casos nuevos del resolver móvil y 12 del selector Shared; son parte de
las suites, no totales adicionales. La compilación final **Android Release/QA148**,
metadatos y firma terminaron con salida 0. La APK instalada tiene **24.967.198 bytes**
y SHA-256 `24FEC97D6759CD78E3720D99CA433485369DCE140E593F6BAF187A3B10008873`.
El [proof de compilación](../../artifacts/day-context-20261007/apk-proof-148.json)
identifica package, versión y fuentes sin cambios durante esa compilación; el
[proof de instalación](../../artifacts/day-context-20261007/native-install-proof.json)
comprueba que el hash instalado coincide. Su campo de instalación pendiente escrito
por el agente de compilación es histórico y no sustituye este registro posterior.

La primera APK falló al abrir Viaje con `MissingMethodException`: la dependencia
Shared para ARM64 usada por el enlazador era una salida obsoleta sin `IsInProgress`.
Se resolvió compilando Shared con `-r android-arm64` y publicando Mobile en salidas
aisladas nuevas, sin editar producción ni borrar los `bin/obj` globales. El primer
publish aislado también falló por la dependencia RID ausente; la salida 0 corresponde
al [publish final](../../artifacts/day-context-20261007/android-release-148-clean-retry.log).
Se conservan [diagnóstico y hashes de ambos artefactos](../../artifacts/day-context-20261007/qa148-build-diagnosis.json)
y [fallo inicial](../../artifacts/day-context-20261007/apk-initial-failure-148.json).

## Cobertura nativa QA148 observada

Pruebas con datos sintéticos en el perfil original del Android, usando el reloj real.
Las observaciones siguientes pertenecen exclusivamente a la APK final QA148:

| Recorrido | Resultado y evidencia efectiva |
|---|---|
| Actividad futura → en curso → Mañana | En español, un solo bloque pasa de A continuación a En curso con la misma actividad y después a Mañana por avance del reloj, sin refresh manual ni cambio del reloj del teléfono. [Futura](../../artifacts/day-context-20261007/es-future-timed.png), [En curso](../../artifacts/day-context-20261007/es-in-progress.png), [Mañana](../../artifacts/day-context-20261007/es-after-last-activity.png); XML y logs homónimos registran los textos. |
| Otra fecha | Al seleccionar el 9/10, distinto de hoy del viaje, no aparecen A continuación ni Mañana a un vistazo. [Captura](../../artifacts/day-context-20261007/es-other-date.png). |
| Resumen y Ver mañana | El detalle muestra la fecha siguiente, hotel y ausencia de documentos relacionados; Ver mañana selecciona ese día. [Resumen](../../artifacts/day-context-20261007/es-tomorrow-detail.png), [día abierto](../../artifacts/day-context-20261007/es-open-tomorrow-day.png). No demuestra un caso con documento vinculado a mañana. |
| Actividad y Maps | Se abre el detalle de la reserva correspondiente; la acción de ubicación lleva Google Maps al primer plano. [Detalle](../../artifacts/day-context-20261007/es-activity-detail.png), [ventana Maps](../../artifacts/day-context-20261007/maps-focus.log). |
| Documento vinculado | La acción abre el selector de aplicaciones Android para el PDF. [Ventana del chooser](../../artifacts/day-context-20261007/document-focus.log). No se verificó la lectura del PDF en un visor. |
| Inglés | Se observan UP NEXT / In progress y Tomorrow at a glance con sus textos localizados. [En curso, PNG](../../artifacts/day-context-20261007/en-in-progress.png), [Mañana](../../artifacts/day-context-20261007/en-tomorrow.png). El XML y log `en-in-progress` muestran el estado Mañana anterior: el refresh terminó entre la lectura del árbol y la captura PNG. Se conserva esta discrepancia; la observación de En curso procede del PNG, no de ese XML. El texto español del nombre/lugar pertenece al contenido sintético, no a recursos de interfaz. |
| Cuenta/viaje durante carga | La petición de bootstrap retenida se cancela al cambiar de cuenta/viaje; las capturas anteriores y posteriores al intervalo de unos 90 segundos muestran el otro viaje del 20/10, sin actividad ni resumen contextual del viaje anterior. [Antes del intervalo](../../artifacts/day-context-20261007/es-other-account-trip.png), [después](../../artifacts/day-context-20261007/es-other-account-after-delay.png), [tras terminar la espera](../../artifacts/day-context-20261007/es-other-account-late-response.png), [eventos del proxy](../../artifacts/day-context-20261007/proxy-events.ndjson). El proxy registra `delayed-client-gone`, no una respuesta tardía aplicada. |
| Caché con HTTP 503 efectivo | El proxy devuelve cuatro respuestas `bootstrap-503` entre 15:57:59 y 15:58:13 UTC y dos `today-503` a 15:58:13/15:58:15. [Durante el fallo](../../artifacts/day-context-20261007/es-cache-http503.png) y [después](../../artifacts/day-context-20261007/es-cache-http503-settled.png), con sus XML, mantienen A continuación, la reserva 09:00 y las tres acciones Maps/detalle/documento; no aparece Mañana. El bootstrap agota sus reintentos en ese ciclo. El fallback Today puede recuperarse en otro intento: no se afirma un fallo permanente de toda la red ni `Connectivity` offline. |
| Datos y preferencias | La instalación conserva **250 recuerdos y 3 fotos** y la app habitual permanece en v119. [Antes](../../artifacts/day-context-20261007/journal-data-before.log), [después](../../artifacts/day-context-20261007/journal-data-after.log), [instalación](../../artifacts/day-context-20261007/native-install-proof.json). Los registros [antes](../../artifacts/day-context-20261007/phone-before.json) y [después](../../artifacts/day-context-20261007/phone-after.json) coinciden: densidad física 600, fuente 1,0, sistema en oscuro y demás ajustes registrados. |

Los cinco checks nativos se registran `passed` con los alcances y límites de la tabla:
instalación, alternancia, acciones, caché/contexto y presentación. TalkBack permanece
`excluded`. Las capturas anteriores con backend inaccesible o los primeros intentos
etiquetados como 503 no acreditan este fallo HTTP efectivo ni `Connectivity` offline;
la evidencia válida son los eventos 503 del proxy y `es-cache-http503{,-settled}`.
Tampoco se
acredita medianoche real desde la alternancia futura/activa: los límites de fecha,
último día y empates están cubiertos con instantes inyectados en tests.

Se usó PIN, sin biometría ni TalkBack. No se cambiaron reloj, densidad, fuente,
idioma global ni otros ajustes globales del teléfono. No se ejecutó una matriz de
fuentes ampliadas o anchos adicionales en esta revisión. Una limitación de hardware
es validación pendiente, no código implementado ni una omisión deducida de la falta
de captura. La cobertura QA147 permanece histórica e inmutable.

## Cierre de evidencia

1. Revisar el flujo real de cada criterio. Registrar archivos y casos aprobados;
   cambiar `implementation` a `done` sólo cuando la revisión lo justifique.
2. Estabilizar fuentes y matriz. Obtener la huella antes y después de Mobile/Shared
   y Android Release. El comprobador rechaza criterios pendientes incluso al pedir
   `-ShowFingerprint`; ese rechazo inicial es intencional.
3. Crear un proof nuevo en `artifacts/day-context-20261007/closure-evidence-148.json`,
   con esquema 1 y `before`/`after` del snapshot vigente; registros `suites` para
   `mobile`/`shared`, `builds` con `android-release` y salida 0, y registros con
   path/SHA-256 de APK, `apkMetadata`, `apkSignature` y archivos de evidencia.
   Aapt debe identificar QA148 y la firma debe verificar. Conservar logs y TRX.
4. Marcar los checks nativos sólo con observaciones de esa APK y asociar sus
   capturas/manifest con hash, fuente, perfil y alcance. Si cambia la matriz al
   registrar resultados, capturar nuevamente su huella; si cambia código, repetir
   la validación afectada. Mantener los límites explícitos.

```powershell
pwsh -File scripts/check-plan-completion.ps1 -MatrixPath docs/plans/day-context-alternation.json -ShowFingerprint
pwsh -File scripts/check-plan-completion.ps1 -MatrixPath docs/plans/day-context-alternation.json -EvidencePath artifacts/day-context-20261007/closure-evidence-148.json
pwsh -File scripts/check-plan-completion.ps1 -MatrixPath docs/plans/day-context-alternation.json -EvidencePath artifacts/day-context-20261007/closure-evidence-148.json -RequireNativeChecks
```

Salida 1: código/evidencia pendiente, obsoleta o fallida. Salida 2 con
`-RequireNativeChecks`: código y pruebas aprobados, cobertura nativa pendiente.
Salida 0 normal no certifica checks nativos pendientes. Esta matriz no autoriza
push, despliegue, cambios de base ni modificaciones del teléfono.

## Cierre ejecutado QA148

El 7 de octubre de 2026, el comprobador terminó con **salida 0** usando
`-RequireNativeChecks`: **12 criterios implementados, 5 checks nativos aprobados**
y TalkBack excluido por solicitud del usuario. El
[proof final](../../artifacts/day-context-20261007/closure-evidence-148.json)
asocia los TRX Mobile 658/658 y Shared 83/83, compilación Android Release,
metadatos, firma, APK y archivos de evidencia mediante SHA-256. Las fuentes
coinciden con el checkpoint anterior a la APK; la matriz se reconcilió después
con las observaciones reales, sin reescribir el checkpoint histórico.

El [manifest nativo](../../artifacts/day-context-20261007/native-validation-148.json)
registra capturas, casos, perfil y límites. La carga inicial observada mostró
«Cargando reservas» sin ninguno de los dos estados; al llegar los datos mostró
la actividad correcta. La QA quedó abierta en Viaje, en la cuenta sintética
original y con los horarios originales restaurados. Se cerró el proxy de fallos
y se restauró la conexión USB al backend QA local habitual.

La comprobación [final de ajustes](../../artifacts/day-context-20261007/phone-final.json)
confirma tema oscuro del sistema, escala de texto 1,0, densidad original e idioma
QA sin override. Los [contadores finales](../../artifacts/day-context-20261007/journal-data-final-restored.log)
siguen en 250 recuerdos y 3 fotos. La app habitual permanece en versión 119;
únicamente la instalación QA se actualizó a 148. Sin push, despliegue ni cambios
en producción.

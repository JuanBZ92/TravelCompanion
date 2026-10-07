# Uso diario y claridad: implementación

Base de revisión: `main`, `51c9962`. Esta entrega conserva las pestañas, permisos, cifrado local y contratos existentes; añade únicamente criterios opcionales de ventana temporal. No requiere migraciones PostgreSQL ni infraestructura nueva. No incluye publicación en producción; la compilación e instalación QA son locales y usan otra identidad de aplicación.

## Estado del cierre: 7 de octubre de 2026, QA147 instalada y cobertura nativa parcial

La revisión del plan se descompone ahora en **56 condiciones independientes** en la
[matriz de aceptación](plans/daily-use-clarity.md), con [datos verificables](plans/daily-use-clarity.json).
Los criterios separan implementación, pruebas automáticas y comprobación nativa.
La revisión independiente completó las 56 condiciones sin encontrar otras omisiones
concretas de implementación. Las correcciones están compiladas y las suites pasaron;
QA147 está instalada; se completaron las comprobaciones físicas concretas de
Asistente/Mapa y la serie de gastos. El navegador del admin completó su recorrido
final. La serie nueva de miniaturas se interrumpió y las combinaciones nativas
restantes conservan su estado pendiente. El cierre de código y pruebas no se
presenta como certificación de toda la matriz visual ni de mediciones incompletas.

Esta pasada corrigió el permiso del desglose offline: una sesión `BuilderReadOnly`
con login todavía vigente no puede heredar premium de un libro cacheado. La regresión
`CachedPremiumCannotOverrideAReadOnlyBuilderSessionWithAStillValidLogin` falló antes
y pasó después. También se completaron tres mensajes `StringLength` del admin en
español y se añadió `MissingThumbnailKeepsPlaceholderAndDoesNotStopLaterVisiblePhotos`
sobre el JournalViewModel real, sin modificar la presentación de fotos. Al retomar
el Android se corrigieron además mensajes de vinculación de documentos que estaban
sólo en español y el acceso al administrador que aún aparecía en inglés.

Últimas suites completas aprobadas: **644/644 Mobile y 75/75 Shared**, con resultados
en [Mobile final](../artifacts/native-acceptance-20261007/mobile-final-results/mobile-final-current.trx)
y [Shared final](../artifacts/native-acceptance-20261007/shared-final-results/shared-final-current.trx). La última suite
API definitiva pasó **561/561**, incluidas **35/35 pruebas PostgreSQL reales**, sin
fallos ni omisiones: [TRX](../artifacts/native-acceptance-20261007/api-telemetry-final.trx),
[log y salida 0](../artifacts/native-acceptance-20261007/api-telemetry-final.log) y
[resumen con huellas de fuentes](../artifacts/native-acceptance-20261007/api-telemetry-final-summary.json).
La base temporal exclusiva se eliminó después de comprobar cero sesiones y schemas
de pruebas; [registro de limpieza](../artifacts/native-acceptance-20261007/postgres-api-inline-final.json).
El corte [API143](../artifacts/native-acceptance-20261007/api-143.trx), **557/557 y 34
PostgreSQL**, se conserva como historia y no sustituye esta ejecución definitiva.
Mobile/Shared se repitieron en esta revisión con salida 0. Cubren las fuentes de
lógica enlazadas por esos proyectos, no las páginas MAUI ni el ViewModel completo
del Asistente. El ajuste posterior de `PlannerStaleHelp` reutiliza un recurso ES/EN
existente y se valida en la compilación Android final. La revisión nativa continúa
separada de esas suites.

Los cuatro casos API nuevos cubren tres variantes de fechas inválidas al crear un
viaje y la conexión preabierta de telemetría. El primer intento completo de 560
casos falló en una señal de telemetría; se conservaron su [TRX](../artifacts/native-acceptance-20261007/api-inline-final.trx)
y la [reproducción sin builds paralelos](../artifacts/native-acceptance-20261007/api-concurrent-generation-quiet.trx).
El diagnóstico separó aperturas de conexión de comandos SQL: las aperturas locales
con `Pooling=false` consumían gran parte del límite vigente de dos segundos.
No se aumentó ese límite ni se relajaron aserciones para obtener el resultado verde.
Los recursos móviles ES/EN conservan **962 claves iguales y únicas**.

`DayPlanService.TrackSafelyAsync` mantiene ahora una conexión EF durante toda la
unidad de telemetría, abierta dentro del mismo presupuesto de dos segundos y cerrada
en `finally` sólo si esa operación la abrió. Una conexión preabierta permanece abierta;
fallos de telemetría o de cierre no invalidan una propuesta ya comprometida. La
[regresión PostgreSQL focalizada](../artifacts/native-acceptance-20261007/api-telemetry-focused.trx)
pasó **2/2** y conserva las comprobaciones de respuesta idéntica, un uso consumido y
una señal deduplicada, además de verificar el estado final de las conexiones.

La [comparación instrumentada](../artifacts/native-acceptance-20261007/analytics-connection-comparison.json)
registró **una apertura para cinco comandos SQL** en la operación ganadora: apertura
de 840,19 ms y 13,02 ms acumulados de SQL. Antes se obtenían tres respuestas iguales
y un uso comprometido, pero cero señales; después se obtuvo exactamente una señal.
Es un diagnóstico sintético local, una ejecución por versión, sin cinco calentamientos
ni treinta repeticiones: **no es un benchmark p50/p95 ni prueba de mejora de latencia
en Android o producción**. El coste de conexión también cambió entre corridas; no se
atribuye la diferencia del tiempo total exclusivamente a esta corrección.

La revisión de navegador detectó desbordamientos del formulario y validaciones de
ciudad/fechas incompletas. Se ajustaron el CSS responsive, los mensajes próximos a
cada campo, la recuperación del formulario y la inicialización de validación en filas
dinámicas. CSS y JavaScript usan `asp-append-version` para evitar recursos antiguos.
La revisión también corrigió los menús abiertos del admin para que permanezcan
dentro del ancho disponible y permitan desplazar su contenido. La compilación final
API/worker que incluye ese CSS aprobó **tres proyectos, cero errores y cero warnings**,
salida 0: [log](../artifacts/native-acceptance-20261007/api-worker-menufinal.log)
y [exit code](../artifacts/native-acceptance-20261007/api-worker-menufinal.exit).
El backend Review usa la DLL final aislada registrada en [api-runtime.json](../artifacts/native-acceptance-20261007/api-runtime.json),
con [health 200 y base disponible](../artifacts/native-acceptance-20261007/api-menufinal-health.json).
El [recorrido final de navegador](../artifacts/native-acceptance-20261007/admin-browser-final.json)
pasó **105/105 comprobaciones**, incluidas **54 de menús abiertos**, y conserva **92
capturas**. Revisó navegación activa, login en español, formulario, errores inline,
foco, preservación del contenido, filas dinámicas y filtros. La lista se comprobó en
anchos de 320, 384, 600 y 1280 píxeles CSS, temas claro/oscuro y fuentes de 100/150/200 %;
los menús abiertos se comprobaron a 320/384/600. Los [bindings finales](../artifacts/native-acceptance-20261007/admin-browser-final-bindings.json)
vinculan runtime, fuentes, compilación y las respuestas HTTP 200 de los cuatro
recursos CSS/JavaScript versionados. La ejecución usó Edge/Playwright aislado y
escalado de fuentes CSS de prueba: **no acredita teclado del sistema, preferencias
de texto del dispositivo ni lector de pantalla**. La base QA conservó sus cinco
viajes; la paginación con más de 50 sigue cubierta por API, no por este recorrido
de navegador. Los datos son sintéticos y no se modificó producción.

En el Asistente, QA144 corrigió el inicio recortado con fuentes ampliadas al incluir
la cabecera y los estados en el contenido desplazable. QA145 corrige una pregunta
guiada que podía quedar oculta por el desplazamiento hacia la última respuesta:
revela el header de la lista virtualizada, comprueba aparición y contexto y respeta
reducción de movimiento. En QA147, tocar Ajustar reveló inmediatamente la pregunta
del cliente «¿Qué querés ajustar?» y sus opciones; [captura nativa](../artifacts/native-acceptance-20261007/qa147-guided-reveal-original-profile.png).
Se comprobó en español a 384 dp y fuente 100 %, con el perfil restaurado del teléfono.
Esta comprobación no se atribuye a una pregunta nueva generada por el servidor.

La captura [QA144 Mapa expandido](../artifacts/native-acceptance-20261007/qa144-map-full-detail-384-100.png)
mostró que la descripción seguía en una línea pese a `MaxLines=-1`, por conservar
`TailTruncation`. `MapPage` y `FreeMapPage` cambian ahora a `WordWrap` al expandir.
QA147 incorpora esta corrección y el mensaje `PlannerStaleHelp` traducido. QA146
se conserva como corte intermedio, anterior a ese último ajuste. La [comprobación
nativa QA147](../artifacts/native-acceptance-20261007/qa147-map-expanded-original-profile.png)
mostró la descripción sintética completa en varias líneas, Mostrar menos y las
acciones fijas visibles, en español a 384 dp y fuente 100 %. La selección revisada
fue Culture Tokyo 18. La lectura completa queda acreditada por esta captura final,
sin trasladar ese resultado a la captura incompleta de QA144.

Los primeros intentos Mobile/Shared no pudieron conectar el proceso testhost dentro
del sandbox y no ejecutaron casos; sus logs/TRX se conservan con sufijo `testhost-initial`.
Se repitieron con comunicación local autorizada y las suites definitivas pasaron.
El primer build aislado del worker tampoco completó la compilación; el reintento
autorizado compiló los tres proyectos con salida 0. No se presentan esos intentos
como fallos de la aplicación ni como pruebas aprobadas.

APK final instalada: [Yuku-QA-v147.apk](../artifacts/native-acceptance-20261007/Yuku-QA-v147.apk),
**24.963.102 bytes**, SHA-256 **`9AF8E8E278B485F8F4B7677A62241111F0D91FAFC31D277A72B341ACC2F342B3`**.
Android Release ARM64, package `com.yuku.travelcompanion.plannerpaidreview`, versionCode
147, minSdk 23 y targetSdk 36, compilación y firma verificadas con salida 0.
Actualiza la identidad separada de QA, con backend local y diagnósticos de revisión.
QA147 se instaló y abrió con acceso por PIN. El perfil restaurado del teléfono se
volvió a verificar: densidad física 600 sin override, fuente 1,0, modo nocturno
activado y rotación automática 1; [registro de ajustes](../artifacts/native-acceptance-20261007/phone-restored-20261007.json).
No se vuelven a cambiar los ajustes globales durante esta comprobación final.
La instalación habitual y sus datos se conservan. El reverse del backend QA se
restauró al terminar la medición interrumpida y se volvió a leer el perfil del
teléfono, sin cambios de densidad, fuente, modo nocturno o rotación. **La cobertura
nativa final es parcial; no se continúan las pruebas en el dispositivo en este
corte.** No se hicieron push, despliegue ni migraciones.

El [procedimiento de cierre](plan-completion.md) quedó incorporado a [AGENTS.md](../AGENTS.md).
La revisión final y corrección de omisiones forman parte de la misma tarea. El
[comprobador](../scripts/check-plan-completion.ps1) contrasta criterios, huellas de fuentes,
matriz, TRX, compilaciones, hash, package, versión y firma del APK. Sus **16 fixtures**
validan fallos y evidencia obsoleta, sin sustituir las pruebas de la app ni la revisión
manual del plan. Un prefijo de prueba vacío se rechaza; los prefijos usan clases reales,
incluidas clases parciales, no nombres de archivos.

[Evidencia histórica QA142](../artifacts/plan-acceptance-20261007/closure-evidence.json)
y su [resultado de cierre](../artifacts/plan-acceptance-20261007/closure-check.log) se
conservan intactos. No son una prueba de cierre de QA147: el comprobador rechaza esa
evidencia porque cambiaron fuentes, matriz y APK. La [evidencia final QA147](../artifacts/native-acceptance-20261007/closure-evidence-147.json)
vincula las fuentes estables, resultados definitivos y cobertura nativa observada,
conservando los checks pendientes. El [comprobador final](../artifacts/native-acceptance-20261007/closure-check-final.log)
aprobó código y pruebas con salida 0. Al exigir toda la cobertura nativa devuelve
2; la evidencia histórica se rechaza con salida 1. La única fuente modificada
después de compilar Android fue el CSS del admin, validado por la compilación
API/worker y las 105 comprobaciones de navegador; la reconstrucción de huellas
demuestra que no cambió ninguna otra fuente del checkpoint de QA147.

Mañana quedó comprobado en QA145: reserva del 8 de octubre, hotel separado, documento
vinculado visible y apertura del día correcto, en inglés a 600 dp y texto al 150 %.
La apertura del PDF con conexión realmente desactivada conserva su evidencia QA142.
Las capturas y sus hashes están en [native-review.json](../artifacts/native-acceptance-20261007/native-review.json).
La instalación QA147, el cierre de navegador y la nueva serie de gastos están
comprobados y la evidencia final quedó consolidada. Quedan la serie de miniaturas
y las combinaciones nativas no ejecutadas. Se conserva el perfil restaurado solicitado
del teléfono y este host no dispone de AVD configurado; no se certifican esas
combinaciones desde fuentes o tests. Las notas no convierten una compilación o
captura parcial en un éxito completo. El
comprobador exige los checks nativos correspondientes con `-RequireNativeChecks`;
la evidencia histórica obsoleta devuelve 1.

## Mediciones nativas actuales: QA145 y QA147

Cada serie completada excluye cinco calentamientos y conserva treinta muestras
válidas de la operación instrumentada. Las versiones y perfiles se mantienen
separados; no se presentan como una comparación entre versiones.

| Operación y perfil | p50 / p95 (ms) | Asignaciones p50 / p95 (B) | Peticiones y lecturas por muestra |
|---|---|---|---|
| Guardar recuerdo, QA145, 600 dp / 150 % / EN | 204,25 / 281,52 | 18.593.552 / 36.350.224 | 0 peticiones, 2 lecturas |
| Guardar gasto, QA147, 384 dp / 100 % / ES | 46,535052 / 51,409322 | 152.728 / 154.032 | 0 peticiones, 1 lectura |

La medición de Journal QA145 usó un diario sintético de 250 recuerdos. Se retiró sólo el reverse
del backend QA; no se afirma desconexión general de Android. Se midió la operación
del store, excluyendo automatización, teclado y navegación. Las asignaciones del
intervalo son de todo el proceso, sin atribuirlas exclusivamente al guardado.
Las lecturas de aproximadamente 993 KB por operación son
un riesgo de crecimiento que queda registrado. No se compara con QA137 porque el
perfil y los datos difieren. [Informe y contadores](../artifacts/native-acceptance-20261007/qa145-journal-600-150-en-native-analysis/report.md).

Gastos QA147 capturó **35/35 eventos**, excluyó los cinco calentamientos y obtuvo
treinta valores válidos para cada contador. Cada muestra medida registró cero
peticiones y una lectura; los bytes leídos fueron **p50 6.844 B y p95 6.917 B**.
El [manifiesto](../artifacts/native-acceptance-20261007/qa147-expense-384-100-es-native.manifest.json)
vincula el APK QA147 y el perfil original. El [análisis](../artifacts/native-acceptance-20261007/qa147-expense-384-100-es-native-analysis/capture-analysis.json),
el [resumen de operación](../artifacts/native-acceptance-20261007/qa147-expense-384-100-es-native-analysis/operation/summary.json)
y la [verificación de fidelidad](../artifacts/native-acceptance-20261007/qa147-expense-384-100-es-native-analysis/raw-fidelity.json)
conservan los ticks exactos y los contadores originales. Se hizo inaccesible el
backend retirando únicamente el reverse de QA: **no se cambió Internet ni el
estado `Connectivity` de Android y no se declara modo offline**. La duración cubre
sólo el store, sin navegación, teclado ni interacción visual; las asignaciones
corresponden al intervalo global del proceso. El estado de cancelación no estaba
informado en las treinta muestras y se conserva como desconocido, sin convertirlo
en cero cancelaciones. No se compara con las series históricas de gastos porque
no existe una baseline equivalente de datos y perfil.

La nueva serie de miniaturas QA147 quedó **incompleta**. El [manifiesto](../artifacts/native-acceptance-20261007/qa147-thumbnails-384-100-es-native.manifest.json)
y las [muestras parciales](../artifacts/native-acceptance-20261007/qa147-thumbnails-384-100-es-native.samples.json)
registran sólo tres iteraciones de calentamiento, nueve eventos de lectura y **cero
muestras medidas**; no alcanzó los cinco calentamientos y treinta repeticiones.
El guard detectó que la aplicación QA había dejado de estar en primer plano y
detuvo la captura sin leer ni operar la otra aplicación. El [trial independiente](../artifacts/native-acceptance-20261007/qa147-thumbnail-trial.json)
confirma únicamente tres lecturas en una apertura de prueba; **no es un benchmark**.
No se analizaron las muestras parciales ni se publican p50/p95 de esta serie.
La medición nativa actual de miniaturas permanece pendiente; la evidencia histórica
QA137 conserva su versión y no la reemplaza. El reverse QA quedó restaurado.
TalkBack permanece excluido permanentemente. VoiceOver no se certifica en este host.
Las mediciones host/Android que siguen abajo conservan su versión y sus límites;
no se etiquetan como nuevas mediciones de QA145/QA147. El estado actual sustituye los totales y
el artefacto final de las revisiones anteriores, que se conservan como historia.

## Cambios y comprobación del plan

| Punto | Implementación | Validación |
|---|---|---|
| Guardado local independiente de HTTP | Journal y Gastos usan gates separados para sincronización y escritura; ACK fusionado por mutación/revisión sobre el índice vigente. | Pruebas con HTTP retenido, edición/borrado concurrentes, reintentos y conflictos. |
| Cancelación del asistente | Operación creada antes de token/GPS, contexto cuenta/viaje/fecha/página comprobado después de await; cancelación y reintento conservan el pedido exacto. El guardado de tarjetas captura contexto antes de caché/cola y conserva la fecha al abrir editor/pase. | Pruebas de ubicación cancelada, cambio de contexto, cola con fecha/página cambiadas y dos reintentos idempotentes; fallo de caché conserva la tarjeta y usa error localizado. |
| Contraste y sistema visual | Metadatos de propuestas y Asistente con EditorialAccentText, secundarios EditorialMuted y fallback de mapa Ink. El estilo EditorialBody y EditorialUi usan 16 dp; se conservan tamaños específicos para metadatos y controles secundarios. | Cálculo local de contraste y XML válido. QA138: Búsqueda a 320 dp/200 %. QA139: PIN, Viaje, Asistente, Journal, Pase y Mapa en inglés a 600 dp/150 %; no acredita todas las pantallas/estados. |
| Salir de gasto modificado | Snapshot de campos editables; confirma descartar sólo si cambió el formulario. | Pruebas de snapshot y revisión nativa de confirmación, continuar editando y descartar. |
| Preparación local inmediata | Importación antigua independiente; adjuntar y declaraciones no esperan HTTP ni flush de analítica. En Carpeta, tocar la categoría abre sus documentos y el icono de añadir abre el selector con esa categoría elegida. | VM real con servidor/analítica retenidos, contexto cambiado y no repetición. QA139: cancelar conserva 0/4, guardar PDF en Alojamiento llega a 1/4, mover a Transporte actualiza ambas categorías y borrar el último archivo vuelve a 0/4. |
| Documentos vacío/error/offline | Vacío sólo después de una carga válida local/remota; caché y adjuntos conservados; error y reintento diferenciados. La búsqueda protege cuenta/viaje y aperturas duplicadas. Refresh descarta un token recuperado si cambió el contexto antes de sincronizar. | VM real: vacío, fallo/EOF, offline sin descarga, contenido conservado, reintento y respuesta tardía; dos regresiones finales para token tardío de otra cuenta/viaje. QA139: guardar, abrir PDF local sin conexión, mover y eliminar con confirmación; los contadores de Carpeta se actualizan. |
| Motivo de propuesta bloqueada | Conexión, permiso, revisión, selección y recuperación junto a aplicar. | Pruebas de propuesta offline/selección conservada. |
| Estado de hoy fiel | Distingue día vacío, ideas flexibles y ausencia de próximas reservas. | Prueba determinista; zona horaria existente. |
| Journal eficiente | Filas estables por identidad, rango visible con margen, caché de 48 miniaturas; archivos cifrados separados con migración gradual legacy y limpieza/vinculación compatibles. | Pruebas de portada, fotos ausentes, migración parcial, borradores y cuenta vinculada; host y Android medidos. Lista con 250 recuerdos, scroll y regreso revisados en QA137. |
| Mapa progresivo | Preview breve, detalle desplazable sólo al expandir, acciones fuera del scroll; comparación estructural sin JSON y reutilización del índice de búsqueda. | Pruebas de cambios de catálogo y preview. QA139 inglés 600 dp/150 %: preview, View details, Show less e iconos fijos revisados. |
| Pase contextual | Beneficios exclusivos primero, detalle ampliable, funciones personales gratuitas separadas. | Pruebas de origen y beneficio; permisos/compras intactos. QA139 inglés: beneficios gratuitos explícitos y Restaurar visible; venta deshabilitada en backend local, sin validar compra real. |
| PIN/biometría | Preferencia por cuenta; PIN vuelve a verificación de login, conserva requerimiento de desbloqueo. | Pruebas de persistencia, cambio de cuenta durante prompt, fallo biométrico y ruta PIN. QA139: nuevo login PIN tras retirar permisos sólo a la app QA; login PIN también revisado en inglés 600 dp/150 %. |
| Admin | Catálogo/Viajes/Negocio, activo aria-current, español, validación/foco inline; viajes 50 por página con filtros/conteos. | Pruebas de paginación, validación y codificación; sintaxis JS. |
| Búsqueda local | Menú Viaje y lupa Documentos/Journal; nombre, lugar, texto confirmado y fecha; filtrado por tipo. No lee PDF/fotos ni hace búsquedas remotas. | Pruebas de acentos, fecha, tipos e identidad. QA138: ancho 320 dp, texto 200 %, teclado/IME, resultado desplazado, abrir detalle y volver conservando consulta y posición. |
| Mañana | Resumen secundario únicamente si mañana pertenece al viaje: primera reserva, hotel, documento vinculado y estado offline. Los documentos incluidos conservan su permiso al mostrar/abrir aunque estén descargados; los personales siguen gratuitos. | Pruebas de fechas, orden, separación hotel/reserva, tipo de documento, revocación y expiración durante apertura. La comprobación física QA139 se interrumpió porque el usuario desconectó el teléfono; no falta implementación por esa causa. |
| Rato libre | Inicio y duración de 30/60/90/120 minutos editables; backend recorta por compromiso real y valida duración, traslado y catálogo autorizado. El móvil comprueba fecha, cuenta/viaje y operación activa después de leer caché/ciudad. | API/Shared y móvil; acceso, cuota, replay, ventanas inválidas y duplicados; seis regresiones ejecutan el partial de producción frente a respuesta tardía. QA139: ubicación opcional denegada, ventana 120 min a las 14:00 de Tokio, alternativas, reemplazo individual y agregado confirmado actualizan Viaje. |
| Desglose offline | Cálculo compartido de categorías/días desde libro local, parcial/conversiones pendientes; premium conocido vigente. CSV mantiene permisos/red. | Pruebas de totales, pendiente, borrado, sesión gratuita/expirada; guardado y desglose offline revisados en Android, resolución de conflicto manteniendo la versión local. |
| Comparar propuestas | Dos snapshots cifrados del mismo rango; elegir una completa conserva selección, IDs, revisión, guardados y recuperación. Al comparar, el pie muestra únicamente elegir la anterior o conservar la actual; aplicar, selección, reemplazo y recuperación quedan bloqueados hasta elegir. La ayuda está localizada. | Pruebas de reinicio, alternancia, rango distinto, offline y bloqueo antes de elegir ambas alternativas; la elección no amplía permisos. QA138: sin Añadir mientras compara; elegir la actual vuelve a 12 ideas. |
| Documentación/legacy | README y docs reflejan producto actual; revisión de referencias de pantallas antiguas. | Revisado contra rutas, DI y contratos actuales. |

## Matriz de fuentes y pruebas

La tabla vincula cada punto funcional con archivos de producción y evidencia concreta. Las pruebas corresponden a las suites definitivas indicadas abajo; las comprobaciones estáticas y nativas se identifican por separado. Una prueba de política o ViewModel no certifica por sí sola navegación, foco o lector de pantalla.

| Punto | Fuente principal | Prueba o evidencia |
|---|---|---|
| Guardado local / HTTP | [JournalStore.cs](../src/TravelCompanion.Mobile/Services/JournalStore.cs), [JournalStore.FreeEntries.cs](../src/TravelCompanion.Mobile/Services/JournalStore.FreeEntries.cs), [ExpenseStore.cs](../src/TravelCompanion.Mobile/Services/ExpenseStore.cs) | [JournalStoreTests.cs](../tests/TravelCompanion.Mobile.Tests/JournalStoreTests.cs), [ExpenseStoreTests.cs](../tests/TravelCompanion.Mobile.Tests/ExpenseStoreTests.cs): HTTP retenido, ACK antiguo, mutación concurrente, conflicto y borrado; revisión final añade tombstone con escritura/fotos pendientes y sucesor de presupuesto ante ACK propio. |
| Cancelación / contexto del Asistente | [AssistantRequestScope.cs](../src/TravelCompanion.Mobile/Services/AssistantRequestScope.cs), [TravelChatViewModel.cs](../src/TravelCompanion.Mobile/ViewModels/TravelChatViewModel.cs) | [AssistantOperationTests.cs](../tests/TravelCompanion.Mobile.Tests/AssistantOperationTests.cs): antes del token, GPS tardío, respuesta de otro contexto/página, nueva operación y fecha/página cambiadas durante cola. |
| Sistema visual / contraste | [Colors.xaml](../src/TravelCompanion.Mobile/Resources/Styles/Colors.xaml), [Styles.xaml](../src/TravelCompanion.Mobile/Resources/Styles/Styles.xaml), [EditorialUi.cs](../src/TravelCompanion.Mobile/Pages/EditorialUi.cs) | [Contraste calculado](../artifacts/daily-ux-20261006/contrast-metadata.json), XML validado, compilación final QA140 y capturas QA138/139 de fuentes ampliadas. La lectura con lector no fue ejecutada. |
| Gasto modificado al salir | [ExpenseEditorSnapshot.cs](../src/TravelCompanion.Mobile/Services/ExpenseEditorSnapshot.cs), [ExpenseEditorPage.cs](../src/TravelCompanion.Mobile/Pages/ExpenseEditorPage.cs) | [ExpenseEditorSnapshotTests.cs](../tests/TravelCompanion.Mobile.Tests/ExpenseEditorSnapshotTests.cs); QA135 continuar/descartar. |
| Preparación inmediata | [TripPreparationViewModel.cs](../src/TravelCompanion.Mobile/ViewModels/TripPreparationViewModel.cs), [TripDocumentStore.cs](../src/TravelCompanion.Mobile/Services/TripDocumentStore.cs) | [TripPreparationViewModelTests.cs](../tests/TravelCompanion.Mobile.Tests/TripPreparationViewModelTests.cs), [TripPreparationOrganizerTests.cs](../tests/TravelCompanion.Mobile.Tests/TripPreparationOrganizerTests.cs): importación una vez, no espera HTTP/analítica, decisiones posteriores, aislamiento y selector. |
| Documentos / vacío / error / offline | [DocsViewModel.cs](../src/TravelCompanion.Mobile/ViewModels/DocsViewModel.cs), [DocumentListPresentation.cs](../src/TravelCompanion.Mobile/ViewModels/DocumentListPresentation.cs), [DocsPage.xaml.cs](../src/TravelCompanion.Mobile/Pages/DocsPage.xaml.cs) | [DocsViewModelTests.cs](../tests/TravelCompanion.Mobile.Tests/DocsViewModelTests.cs), [DocumentListPresentationTests.cs](../tests/TravelCompanion.Mobile.Tests/DocumentListPresentationTests.cs): VM real, contenido conservado, reintento, usuario gratis, cuenta/viaje y filas tras mover/borrar. |
| Propuesta bloqueada con motivo | [DayPlannerViewModel.cs](../src/TravelCompanion.Mobile/ViewModels/DayPlannerViewModel.cs), [DayPlanProposalPage.xaml](../src/TravelCompanion.Mobile/Pages/DayPlanProposalPage.xaml) | [DayPlannerViewModelTests.cs](../tests/TravelCompanion.Mobile.Tests/DayPlannerViewModelTests.cs), [DayPlannerComparisonTests.cs](../tests/TravelCompanion.Mobile.Tests/DayPlannerComparisonTests.cs): offline, acceso/revisión, selección conservada y elección necesaria. |
| Estado de Hoy | [TripDayOverview.cs](../src/TravelCompanion.Mobile/Services/TripDayOverview.cs), [ScheduleViewModel.cs](../src/TravelCompanion.Mobile/ViewModels/ScheduleViewModel.cs) | [TripSearchAndOverviewTests.cs](../tests/TravelCompanion.Mobile.Tests/TripSearchAndOverviewTests.cs): vacío, flexible y ausencia de próxima reserva. |
| Journal / miniaturas / migración | [JournalEntries.cs](../src/TravelCompanion.Mobile/Services/JournalEntries.cs), [JournalStore.cs](../src/TravelCompanion.Mobile/Services/JournalStore.cs), [JournalViewModel.cs](../src/TravelCompanion.Mobile/ViewModels/JournalViewModel.cs) | [JournalAndNavigationTests.cs](../tests/TravelCompanion.Mobile.Tests/JournalAndNavigationTests.cs), [JournalStoreTests.cs](../tests/TravelCompanion.Mobile.Tests/JournalStoreTests.cs): filas estables, rango visible, portada, migración parcial, fotos ausentes y cuenta vinculada; benchmarks host/QA137. |
| Mapa progresivo / comparación | [MapPage.xaml](../src/TravelCompanion.Mobile/Pages/MapPage.xaml), [FreeMapPage.xaml](../src/TravelCompanion.Mobile/Pages/FreeMapPage.xaml), [MapContentComparison.cs](../src/TravelCompanion.Mobile/Services/MapContentComparison.cs) | [MapContentComparisonTests.cs](../tests/TravelCompanion.Mobile.Tests/MapContentComparisonTests.cs): catálogo deserializado, cambios de permisos/editorial, reutilización de búsqueda y preview; expansión/contracción QA139. |
| Pase contextual | [PaywallPresentation.cs](../src/TravelCompanion.Mobile/Services/PaywallPresentation.cs), [PaywallViewModel.cs](../src/TravelCompanion.Mobile/ViewModels/PaywallViewModel.cs), [PaywallPage.xaml](../src/TravelCompanion.Mobile/Pages/PaywallPage.xaml) | [PaywallPresentationTests.cs](../tests/TravelCompanion.Mobile.Tests/PaywallPresentationTests.cs): ocho orígenes/fallback; funciones gratuitas y Restaurar visibles QA139, sin checkout real. |
| PIN / biometría por cuenta | [AuthSessionService.cs](../src/TravelCompanion.Mobile/Services/AuthSessionService.cs), [LocalUnlockRouting.cs](../src/TravelCompanion.Mobile/Services/LocalUnlockRouting.cs), [BiometricUnlockViewModel.cs](../src/TravelCompanion.Mobile/ViewModels/BiometricUnlockViewModel.cs) | [LocalUnlockTests.cs](../tests/TravelCompanion.Mobile.Tests/LocalUnlockTests.cs): persistencia, cuenta distinta, cambio durante prompt, PIN verificado y fracaso bloqueado. |
| Admin / navegación / 50 filas / validación | [_Layout.cshtml](../src/TravelCompanion.Api/Pages/Shared/_Layout.cshtml), [TripPlanEditorService.cs](../src/TravelCompanion.Api/Services/TripPlanEditorService.cs), [admin.js](../src/TravelCompanion.Api/wwwroot/admin.js) | [TripPlanEditorServiceTests.cs](../tests/TravelCompanion.Api.Tests/TripPlanEditorServiceTests.cs), [AdminInputValidationTests.cs](../tests/TravelCompanion.Api.Tests/AdminInputValidationTests.cs): páginas/filtros/conteos, payload inválido retenido, codificación y revisión obsoleta. JS revisado sintácticamente; foco y navegación en navegador no certificados como suite UI. |
| Búsqueda local | [TripSearchIndex.cs](../src/TravelCompanion.Mobile/Services/TripSearchIndex.cs), [TripSearchPage.cs](../src/TravelCompanion.Mobile/Pages/TripSearchPage.cs) | [TripSearchAndOverviewTests.cs](../tests/TravelCompanion.Mobile.Tests/TripSearchAndOverviewTests.cs): acentos/fecha/tipo, identidad actividad-recuerdo, fuentes fallidas/vacías y respuesta tardía; teclado/Abrir/Atrás QA138. |
| Mañana | [TripDayOverview.cs](../src/TravelCompanion.Mobile/Services/TripDayOverview.cs), [TomorrowOverviewPage.cs](../src/TravelCompanion.Mobile/Pages/TomorrowOverviewPage.cs), [TripDocumentStore.cs](../src/TravelCompanion.Mobile/Services/TripDocumentStore.cs) | [TripSearchAndOverviewTests.cs](../tests/TravelCompanion.Mobile.Tests/TripSearchAndOverviewTests.cs), [TripPreparationProgressTests.cs](../tests/TravelCompanion.Mobile.Tests/TripPreparationProgressTests.cs), [TripDocumentStoreTests.cs](../tests/TravelCompanion.Mobile.Tests/TripDocumentStoreTests.cs): dentro del viaje, primera reserva distinta del hotel, offline y permisos de documento incluido/personal; QA139 física interrumpida por desconexión. |
| Rato libre | [AssistantFreeTimeWindow.cs](../src/TravelCompanion.Mobile/Services/AssistantFreeTimeWindow.cs), [TravelChatViewModel.FreeTime.cs](../src/TravelCompanion.Mobile/ViewModels/TravelChatViewModel.FreeTime.cs), [TravelChatService.FreeTime.cs](../src/TravelCompanion.Api/Services/TravelChatService.FreeTime.cs), [TravelTimeWindowPolicy.cs](../src/TravelCompanion.Shared/TravelTimeWindowPolicy.cs) | [AssistantFreeTimeWindowTests.cs](../tests/TravelCompanion.Mobile.Tests/AssistantFreeTimeWindowTests.cs), [AssistantFreeTimeViewModelTests.cs](../tests/TravelCompanion.Mobile.Tests/AssistantFreeTimeViewModelTests.cs), [TravelChatFreeTimeTests.cs](../tests/TravelCompanion.Api.Tests/TravelChatFreeTimeTests.cs), [TravelChatFreeTimeEndpointTests.cs](../tests/TravelCompanion.Api.Tests/TravelChatFreeTimeEndpointTests.cs), [TravelTimeWindowPolicyTests.cs](../tests/TravelCompanion.Shared.Tests/TravelTimeWindowPolicyTests.cs); recorrido satisfactorio QA139. |
| Desglose offline | [ExpenseBreakdownPage.cs](../src/TravelCompanion.Mobile/Pages/ExpenseBreakdownPage.cs), [ExpenseStore.cs](../src/TravelCompanion.Mobile/Services/ExpenseStore.cs), [ExpenseService.cs](../src/TravelCompanion.Api/Services/ExpenseService.cs), [ExpensePolicy.cs](../src/TravelCompanion.Shared/ExpensePolicy.cs) | [ExpenseStoreTests.cs](../tests/TravelCompanion.Mobile.Tests/ExpenseStoreTests.cs), [ExpenseServiceTests.cs](../tests/TravelCompanion.Api.Tests/ExpenseServiceTests.cs), [ExpenseBreakdownTests.cs](../tests/TravelCompanion.Shared.Tests/ExpenseBreakdownTests.cs): moneda, total, permisos gratis/expirados, conversiones y conflictos; QA137 offline. |
| Comparar alternativas | [DayPlannerStore.cs](../src/TravelCompanion.Mobile/Services/DayPlannerStore.cs), [DayPlannerViewModel.cs](../src/TravelCompanion.Mobile/ViewModels/DayPlannerViewModel.cs), [DayPlanProposalPage.xaml.cs](../src/TravelCompanion.Mobile/Pages/DayPlanProposalPage.xaml.cs) | [DayPlannerComparisonTests.cs](../tests/TravelCompanion.Mobile.Tests/DayPlannerComparisonTests.cs), [DayPlannerStoreTests.cs](../tests/TravelCompanion.Mobile.Tests/DayPlannerStoreTests.cs): reinicio, elección completa, rango, recuperación, aislamiento y protección offline; QA138 elección real. |
| Documentación / retirada legacy | [README](../README.md), [TECHNICAL](TECHNICAL.md), [FUNCTIONAL](FUNCTIONAL.md), [MauiProgram.cs](../src/TravelCompanion.Mobile/MauiProgram.cs), [AppShell.xaml.cs](../src/TravelCompanion.Mobile/AppShell.xaml.cs) | Referencias y enlaces revisados contra archivos/rutas actuales. Las pantallas legacy y su DI se retiraron; se conserva catálogo completo de bootstrap/offline y detalle. Es comprobación documental, sin atribuirle un test UI. |

Además, las protecciones transversales se revisan en [ViewModelBase.cs](../src/TravelCompanion.Mobile/ViewModels/ViewModelBase.cs) y [ScheduleViewModel.cs](../src/TravelCompanion.Mobile/ViewModels/ScheduleViewModel.cs), con [ViewModelLoadIdentityTests.cs](../tests/TravelCompanion.Mobile.Tests/ViewModelLoadIdentityTests.cs) y [LoadErrorPresentationTests.cs](../tests/TravelCompanion.Mobile.Tests/LoadErrorPresentationTests.cs): una carga de contexto anterior no altera flags/errores nuevos; EOF/IO de Android se presenta en ES/EN, se conserva la semántica de errores HTTP/validación y cancelar permanece silencioso. Los guards finales de menú y token descritos más abajo tienen revisión de fuente y compilación; no se inventa una prueba física de cambio de cuenta durante cada control nativo.

La instrumentación reside en [MobileOperationMeasurement.cs](../src/TravelCompanion.Mobile/Services/MobileOperationMeasurement.cs) y [DiagnosticJournal.cs](../src/TravelCompanion.Mobile/Services/DiagnosticJournal.cs); [DiagnosticJournalTests.cs](../tests/TravelCompanion.Mobile.Tests/DiagnosticJournalTests.cs) verifica privacidad, concurrencia, rotación y fallo de almacenamiento. Los ejecutores [host](tools/measure-journal-host.ps1), [analizador nativo](tools/analyze-native-performance.ps1) y [arranque Android](../scripts/measure-android-startup.ps1) conservan muestras/manifiestos de los escenarios medidos. Sus resultados son evidencia de medición; no aserciones de tiempos absolutos en CI.

## Revisión histórica contra el plan: 6 de octubre de 2026 (QA141)

Este apartado conserva el corte anterior de revisión, con sus totales, artefactos y
límites de aquella fecha. No sustituye el estado actual de QA147 descrito al comienzo
del informe ni atribuye sus pruebas a fuentes modificadas después.

La nueva revisión contrastó los 19 puntos con código de producción, navegación y pruebas, además de las cinco funciones nuevas. La lista anterior tenía implementación, pero conservaba bordes incompletos. Se cerraron los siguientes; no se dio por válida una función sólo por tener un helper probado.

| Pendiente encontrado | Corrección y evidencia |
|---|---|
| Documentos offline y selección de categoría | Explica que el contenido del pase aún no está disponible en el dispositivo, aunque haya adjuntos personales. Cambiar cuenta/viaje o cancelar mientras se elige categoría descarta la operación antes del selector de archivos. [DocsViewModelTests](../tests/TravelCompanion.Mobile.Tests/DocsViewModelTests.cs): 15 casos; los tres casos del selector fallaban antes y pasan después. |
| Journal: bootstrap tardío y cobertura de la lista | No solicita bootstrap con un token de un contexto anterior. [JournalViewModelTests](../tests/TravelCompanion.Mobile.Tests/JournalViewModelTests.cs) ejecuta el VM de producción con 250 recuerdos: carga sólo seis miniaturas iniciales y las del rango visible, conserva identidades y no relee miniaturas al actualizar. |
| Gastos: guardado visible, aislamiento y vinculación | El panel muestra la escritura local durante HTTP, limpia la cuenta/viaje anterior y descarta resultados y errores antiguos. El store rechaza gastos de otro viaje y cotizaciones obsoletas; una vinculación interrumpida conserva el libro de origen. [ExpenseStoreTests](../tests/TravelCompanion.Mobile.Tests/ExpenseStoreTests.cs) incluye las regresiones y la restricción de moneda en ES/EN. El cierre del editor sólo quita su propio modal. Los guards de panel/modal tienen revisión de fuente y compilación; no se presentan como pruebas nativas nuevas. |
| Mañana: metadatos y hotel | Conserva documentos y manifiesto anteriores si falla un reintento. Incluye el vínculo de la primera reserva y del hotel, deduplicado por archivo/URL; muestra carga y recuperación. [TripSearchAndOverviewTests](../tests/TravelCompanion.Mobile.Tests/TripSearchAndOverviewTests.cs) cubre fallo parcial, cancelación, contexto y deduplicación. |
| Rato libre: pulsaciones y cambio de modo | No inicia dos lecturas por doble pulsación y descarta una continuación si se cambia entre Preguntar, Buscar y Rato libre mientras se recupera caché o ciudad. [AssistantFreeTimeViewModelTests](../tests/TravelCompanion.Mobile.Tests/AssistantFreeTimeViewModelTests.cs) ejecuta el parcial de producción. |
| Mapa gratuito y menú de Viaje | El mapa no cierra la sesión nueva ni inicia peticiones con un token anterior después de token/sincronización tardíos. [FreeMapViewModelTests](../tests/TravelCompanion.Mobile.Tests/FreeMapViewModelTests.cs): ocho casos sobre el VM real. El menú de Viaje revalida contexto y aparición después del selector y evita aperturas duplicadas; este último control se verificó en fuente y compilación. |
| Compra, recuperación y sesión | Conserva cada intención por cuenta, guarda el recibo bajo el propietario original y descarta activaciones tardías. La sesión persiste el token cifrado provisional antes de publicar conjuntamente perfil y referencia; logout, cambios de contexto y errores no sobrescriben una sesión nueva. La expiración conocida no se hereda entre propietarios/viajes; dentro del mismo contexto mantiene la restricción, incluso vencida. [PaywallViewModelContextTests](../tests/TravelCompanion.Mobile.Tests/PaywallViewModelContextTests.cs), [StorePurchaseRecoveryContextTests](../tests/TravelCompanion.Mobile.Tests/StorePurchaseRecoveryContextTests.cs) y [AuthSessionAtomicTests](../tests/TravelCompanion.Mobile.Tests/AuthSessionAtomicTests.cs): 45 casos nuevos, incluidos formato antiguo, datos legacy corruptos y fallo de limpieza. Las compras son sintéticas; no hubo checkout real. |
| Idioma del admin | Completados títulos y acciones en español de diez vistas Razor; conserva URLs, handlers y confirmaciones. JavaScript válido y compilación API/worker aprobada. No se atribuye una revisión visual de navegador a estas comprobaciones. |

Validación completa posterior: **642/642 Mobile**, **557/557 API** —incluidas **34 pruebas con PostgreSQL real**— y **75/75 Shared**, sin fallos ni omisiones. Son **74 casos móviles adicionales** respecto de final8; los focales están incluidos en la suite y no se suman dos veces. Evidencia: [Mobile](../artifacts/plan-recheck-20261006/mobile-results/mobile-recheck-final.trx), [API](../artifacts/plan-recheck-20261006/api-results/api-recheck.trx) y [Shared](../artifacts/plan-recheck-20261006/shared-results/shared-recheck.trx). API/worker compilan con salida 0; 17 archivos XML modificados son válidos y los 958 recursos ES/EN tienen claves únicas e iguales. PostgreSQL se ejecutó en una base local temporal, eliminada después; quedaron cero schemas de performance y la base QA no se modificó.

La repetición host sobre las fuentes finales conserva cinco calentamientos y treinta muestras por escenario; usa las mismas condiciones en la base `51c9962` y el código actual. [Resultados y contadores](../artifacts/plan-recheck-20261006/host-performance/comparison.json), [huellas de fuentes](../artifacts/plan-recheck-20261006/host-performance/run-manifest.json).

| Escenario host | p50 inicial → actual (ms) | p95 inicial → actual (ms) |
|---|---|---|
| Guardar recuerdo durante HTTP retenido | 134,96 → 22,30 | 151,22 → 37,72 |
| Guardar gasto durante HTTP retenido | 109,86 → 2,93 | 119,39 → 3,55 |
| Leer seis miniaturas cifradas | 130,57 → 1,23 | 142,75 → 1,72 |

Las seis miniaturas leen 24.213.841 → 97.179 bytes y asignan 189.378.672 → 847.016 bytes (p50); siguen siendo seis lecturas y cero peticiones. El guardado de gastos no reduce asignaciones: 3.767.520 → 3.770.072 bytes. No hubo regresión superior al 10 % contra la versión inicial en los escenarios medidos. El p95 de Journal varía entre los runs actuales ya documentados; esta medición no determina la causa de esa variación ni acredita latencia Android, HTTP real o arranque. La corrección posterior de expiración sólo afecta publicación de sesiones; no cambia las siete fuentes de stores/caché fotografiadas por el ejecutor host.

En ese corte se mantuvo la implementación de los cinco recorridos nuevos: Buscar en mi viaje, Mañana, Tengo un rato libre, Desglose offline y Comparar propuestas. La segunda revisión no encontró otra función ausente. La verificación física de esas correcciones seguía pendiente porque el usuario había desconectado el teléfono. TalkBack quedó excluido por petición explícita del usuario, también para revisiones futuras.

Artefacto definitivo de ese corte histórico: **QA Android Release v141, ARM64**, compilación y verificación de firma con salida **0**. [Yuku-QA-v141.apk](../artifacts/plan-recheck-20261006/Yuku-QA-v141.apk), **24.954.910 bytes**, SHA-256 **`0B64699E7C210E61B0624B564CD916BCC6BD59EBBF5707329A7A496DFCF4FE32`**. Aapt confirma package `com.yuku.travelcompanion.plannerpaidreview`, versionCode 141, minSdk 23 y targetSdk 36. Incluye la corrección final de expiración; el primer corte de v141 anterior a esa corrección se conserva con sufijo `before-expiry` y no es el artefacto definitivo de aquel corte. **QA141 no se instaló ni se revisó físicamente**. El SDK no tenía emuladores configurados (`emulator -list-avds`, salida 0 sin dispositivos). QA139 era entonces la última versión físicamente revisada; QA147 es la versión instalada en la revisión actual.

[Manifiesto definitivo](../artifacts/plan-recheck-20261006/final-validation-summary.json): suites, PostgreSQL, APK/hash, medición host y límites nativos. No hubo push, despliegue ni cambios en producción. Esta revisión no requiere nuevas migraciones PostgreSQL.

## Validación histórica: 6 de octubre de 2026 (QA140)

- Mobile final8: **568/568**, sin fallos, omisiones ni warnings, salida 0. Evidencia definitiva: `artifacts/daily-ux-20261006/mobile-final-8.log` y `mobile-final-results/mobile-final-8.trx` dentro del mismo directorio. Incluye las regresiones posteriores a QA139 y los tres casos finales de página/cola del Asistente. Los cortes anteriores fueron 545/545 en QA139 y 565/565 en final7; no sustituyen a final8. Pasaron también el focal de Documentos **11/11** (dos casos de token tardío) y el focal de stores **80/80**; sus casos ya forman parte de la suite completa y no se suman como pruebas distintas.
- API: **557/557**, sin fallos ni pruebas omitidas, incluidas **34 sobre PostgreSQL real**. Evidencia: `artifacts/daily-ux-20261006/api-final-validation/api-final.log` y `test-results/api-final.trx` dentro de ese directorio. El restore informó dos advertencias NU1900 al no poder consultar el feed de vulnerabilidades de NuGet; las pruebas se ejecutaron y pasaron.
- Shared: **75/75**, sin fallos ni pruebas omitidas. Evidencia: `artifacts/daily-ux-20261006/shared-full.log` y `shared-full/shared-full.trx` dentro del mismo directorio.
- API y worker compilan con salida **0**, sin errores, en `artifacts/daily-ux-20261006/api-final-validation/api-build.log` y `worker-build-final.log`. Informaron una y dos advertencias NU1900 respectivamente; el feed de auditoría de paquetes no estuvo disponible. `validation-summary.json` confirma PostgreSQL real 17.10, base de pruebas temporal eliminada y cero schemas de fixture restantes; la base QA permaneció intacta durante esa validación.

La revisión focal final de planificación aprobó **156/156** pruebas móviles, sin omisiones ni warnings, y **16/16** de API Rato libre, sin omisiones, con salida 0. Evidencia: `artifacts/daily-ux-20261006/planner-final-review/mobile-focused-save-final.log`, `results/mobile-focused-save-final.trx`, `api-free-time.log` y `results/api-free-time.trx` dentro del mismo directorio. El focal móvil de 156 reemplaza al corte anterior de 153 tras añadir tres regresiones de página/cola. Son ejecuciones focales; no se suman a los totales de suites como si fueran casos adicionales distintos.

Las suites prueban lógica, permisos, concurrencia y compatibilidad. No sustituyen la compilación Android ni las comprobaciones de navegación, teclado, fuentes ampliadas y controles nativos. La revisión documental no encontró enlaces rotos en README, TECHNICAL, FUNCTIONAL ni los documentos del ejecutor de mediciones.

Los recursos ES/EN contienen **956 claves únicas y el mismo conjunto de nombres**, según el manifiesto final y la validación XML. Las comprobaciones de idioma/recursos se distinguen de la cobertura física de pantallas inglesas enumerada abajo.

El artefacto final compilado es **QA Android Release v140, ARM64**, con salida **0**, en `artifacts/daily-ux-20261006/android-release-140.log`. APK: [Yuku-QA-v140.apk](../artifacts/daily-ux-20261006/Yuku-QA-v140.apk), **24.922.142 bytes**, SHA-256 verificado **`1616A036ACCA421802D1A602522D46E2209A1F492DFEE142E228E402B6272895`**. Aapt comprobó package `com.yuku.travelcompanion.plannerpaidreview`, versionCode 140, versionName 1.0, minSdk 23, targetSdk 36 y ABI arm64-v8a; `apksigner verify` terminó con salida 0 en [apk-signature-140.log](../artifacts/daily-ux-20261006/apk-signature-140.log). El primer intento informó NETSDK1047 por assets de Shared sin el RID; se restauró Shared con `-r android-arm64` y el reintento terminó correctamente. El intento inicial se conserva en `android-release-140-initial.log`. **QA140 no se instaló ni se revisó físicamente**, porque el usuario había desconectado el Android. El [manifiesto final de validación](../artifacts/daily-ux-20261006/final-validation-summary.json) consolida suites, PostgreSQL, artefacto/hash, dos runs host y exclusión de TalkBack.

La publicación e instalación **local QA Android Release v139, ARM64**, terminaron con código de salida 0. La APK separada es `artifacts/daily-ux-20261006/Yuku-QA-v139.apk`, de **24.574.347 bytes** (aproximadamente 24,6 MB decimales), y conserva la instalación habitual. SHA-256 verificado: `B01245212E7BD3694F99F71D65E419E64BA51FE649166EA58D8D3AF470810A8D`. El log de publicación es `artifacts/daily-ux-20261006/android-release-139.log`. El arranque en frío v139 terminó con cinco calentamientos y treinta repeticiones; sus resultados y alcance se registran por separado más abajo.

La revisión final corrigió dos bordes de contexto: el menú de propuesta captura la aparición de la página, `ContextVersion`, la identidad del ViewModel y `Shell.Current.CurrentPage`, y comprueba que sigan vigentes tras el action sheet antes de ejecutar una acción. La carga de Mapa comprueba contexto y cancelación tras recuperar el token, antes de limpiar la sesión por un token vacío. Son protecciones ante respuestas tardías; no amplían permisos ni alteran las propuestas elegidas.

La revisión local posterior a desconectar el teléfono encontró y corrigió nuevos bordes: Refresh de Documentos no inicia sincronización con un token tardío de una cuenta/viaje anterior; Journal conserva como conflicto una edición offline pendiente frente al borrado remoto; un ACK de presupuesto no confirma una mutación posterior del propio dispositivo; Rato libre valida fecha y contexto al continuar una operación; Mañana respeta los permisos de documentos incluidos aunque el archivo descargado siga en el dispositivo. Las dos regresiones de Refresh se comprobaron fallando antes de la corrección y pasando después, junto con el focal de Documentos. Final8 aprueba esos cambios y la última corrección de fecha/contexto al guardar una tarjeta a través de la cola; la compilación final QA140 incorpora el conjunto.

La revisión de stores terminó con 80 casos focales aprobados en `artifacts/daily-ux-20261006/store-final-review`, con `tests-final.log` y `results/stores-final.trx`. En Journal, los borrados remotos recibidos por GET o respuesta de Save conservan escritura, fotos y portada pendientes, muestran conflicto y no resucitan la entrada eliminada al resolver. La interfaz localizada ofrece Aceptar eliminación o Cancelar y explica que texto/fotos se conservan localmente hasta aceptar; el usuario puede conservar una copia antes de eliminar. No ofrece Conservar mi versión para un borrado remoto, porque esa acción no puede restaurar el registro eliminado. Las fotos sólo se limpian tras resolución explícita o ACK de borrado. Las regresiones `RemoteTombstoneKeepsPreviouslySavedOfflineWritingAndPhotosUntilResolution`, `DeletionReturnedBySaveKeepsWritingAndPhotosAndCannotResurrectOnResolution` y `PhotoAttachedDuringTombstoneFetchRemainsVisibleUntilExplicitResolution`, en `JournalStoreTests.cs`, cubren esos caminos. También se añadieron `CancelledLegacyMigrationRetainsReadablePhotoAndRetryCompletesSplit` y `FailedAccountLinkCopyKeepsSourceWritingAndPhotosAndCanRetry` para cancelación de migración y copia de vinculación fallida. En Gastos, `OlderBudgetAcknowledgementCannotClearNewBudgetMutation` alcanza una segunda sincronización/ACK: sólo el sucesor local del ACK propio con la misma revisión de origen se rebasa, conserva MutationId y evita un conflicto artificial; los snapshots obsoletos del editor y los cambios de otro dispositivo siguen conservando ambas versiones como conflicto.

Las seis regresiones de `AssistantFreeTimeViewModelTests.cs` ejecutan el partial de producción ante cambios de fecha/cuenta/viaje durante caché, fecha/pedido durante lectura de ciudad y el flujo válido. `TripDayOverview.CanOpenDocument` gobierna mostrar/abrir documentos en Mañana; `TripDocumentStore.OpenAsync` comprueba permiso de incluidos y vigencia si `SourceUrl` identifica un documento curado, también después de leer metadata/payload y escribir la copia para Launcher. Cinco regresiones del store cubren adjunto personal gratuito, curado bloqueado/autorizado y revocación/expiración durante payload. Estos guards posteriores a QA139 tienen pruebas locales; no se afirma revisión de ellos en el teléfono desconectado.

En el guardado de tarjetas, `AssistantRequestScope` admite una versión de página opcional. La operación captura fecha, cuenta, viaje y página antes de caché/cola; `SaveItineraryItemCoreAsync` revalida tras las esperas y pasa la fecha capturada al editor o pase. Un fallo de caché conserva la tarjeta y muestra `AssistantSaveError`; los diagnósticos usan etiquetas constantes y no trasladan `ex.Message` técnico a la interfaz. Las regresiones finales de `AssistantOperationTests.cs` cubren respuesta de página anterior y fecha/página cambiadas durante cola.

**QA140** es el artefacto final compilado con las correcciones posteriores. QA139 continúa siendo el último artefacto instalado y conserva la cobertura física descrita, con su versión de origen; no se traslada esa cobertura a QA140.

QA138 fue el artefacto de la revisión funcional final de comparación y búsqueda detallada abajo; su publicación terminó con salida 0 y se conserva `android-release-138.log`. QA137 también se publicó con salida 0 y es la versión de las mediciones nativas documentadas abajo. Se conserva `artifacts/daily-ux-20261006/Yuku-QA-v137.apk`, de 24.570.251 bytes, y `android-release-137.log` dentro del mismo directorio. Los logs Release contienen advertencias XC0025 para bindings con `Source`: `Directory.Build.props` desactiva explícitamente `MauiEnableXamlCBindingWithSourceCompilation` por la localización mediante indexer sobre un Source estático. Esas advertencias no son errores de compilación. El tamaño no se usa como evidencia de una mejora: otros artefactos incluyeron más de una ABI.

## Medición reproducible histórica: 6 de octubre de 2026

Host Release: `docs/tools/measure-journal-host.ps1`, fuente baseline extraída mediante `git show 51c9962` y fuente optimizada capturada en el mismo ejecutor. Los dos runs finales limpios están en `artifacts/journal-performance-host-definitive-20261006` y `artifacts/journal-performance-host-confirmation-20261006`: cada uno conserva `comparison.json`, `baseline.json`, `current.json` y `run-manifest.json`, con muestras y huellas de las fuentes. Los manifiestos identifican la baseline `51c996264bf45d7562b45bc8658d0f51d5cc6af9` y el snapshot optimizado con cambios sin commit; las **14 huellas de fuentes baseline/optimizada son idénticas entre ambos runs**. Incluyen las correcciones finales de stores. Se ejecutaron en **Windows, .NET 10.0.12, Release**, con cinco calentamientos y treinta repeticiones por escenario. El primer run con compilación concurrente y las mediciones de snapshots anteriores son exploratorios y no sustituyen estos resultados. Se conservan ambas comparaciones limpias, no sólo la más favorable; ninguna se atribuye retroactivamente a las mediciones Android anteriores.

La caché usa cifrado AES-GCM y escritura atómica de producción. Los datos son sintéticos: 250 recuerdos de 1.000 caracteres, 250 gastos y seis fotos con 2 MiB completos/8 KiB de miniatura. En los escenarios de guardado se retiene una lectura HTTP sintética durante 100 ms y luego falla; la comprobación exige que la mutación concurrente conserve su contenido y siga pendiente de sincronización. No se confunde ese fallo con una sincronización exitosa.

| Primer run final limpio Windows Release | p50 inicial → optimizado (ms) | p95 inicial → optimizado (ms) | Reducción p95 |
|---|---:|---:|---:|
| Guardar recuerdo mientras sincroniza | 136,1073 → 24,4141 | 154,2449 → 63,4582 | 58,86 % |
| Guardar gasto mientras sincroniza | 109,5694 → 3,3334 | 112,9634 → 4,5090 | 96,01 % |
| Leer seis miniaturas cifradas | 152,3121 → 1,4994 | 163,0458 → 1,9395 | 98,81 % |

| Primer run: métricas p50 | Asignaciones inicial → optimizado (bytes) | Lecturas | Bytes leídos inicial → optimizado | Peticiones |
|---|---:|---:|---:|---:|
| Guardar recuerdo mientras sincroniza | 25.449.584 → 20.383.480 | 2 → 2 | 1.908.008 → 1.906.988 | 1 → 1 |
| Guardar gasto mientras sincroniza | 3.767.680 → 3.770.000 | 2 → 2 | 330.660 → 330.650 | 1 → 1 |
| Leer seis miniaturas cifradas | 189.380.984 → 847.256 | 6 → 6 | 24.214.866 → 97.099 | 0 → 0 |

| Segundo run final limpio: confirmación | p50 inicial → optimizado (ms) | p95 inicial → optimizado (ms) | Reducción p95 |
|---|---:|---:|---:|
| Guardar recuerdo mientras sincroniza | 136,8836 → 20,5009 | 148,6808 → 27,4028 | 81,57 % |
| Guardar gasto mientras sincroniza | 110,0472 → 3,2073 | 115,8526 → 3,8992 | 96,63 % |
| Leer seis miniaturas cifradas | 152,5804 → 1,3248 | 162,3924 → 1,6911 | 98,96 % |

| Confirmación: métricas p50 | Asignaciones inicial → optimizado (bytes) | Lecturas | Bytes leídos inicial → optimizado | Peticiones |
|---|---:|---:|---:|---:|
| Guardar recuerdo mientras sincroniza | 25.449.672 → 20.382.496 | 2 → 2 | 1.908.093 → 1.906.403 | 1 → 1 |
| Guardar gasto mientras sincroniza | 3.767.792 → 3.770.416 | 2 → 2 | 330.560 → 330.740 | 1 → 1 |
| Leer seis miniaturas cifradas | 189.383.440 → 847.096 | 6 → 6 | 24.216.081 → 96.984 | 0 → 0 |

La duración de guardado mide la escritura local mientras hay sincronización en vuelo; sus asignaciones, lecturas y peticiones abarcan **toda la sincronización superpuesta más el guardado**. Para las miniaturas, todas las métricas abarcan las seis lecturas. Las mejoras observadas eliminan contención y evitan cargar fotos completas; no reducen la cantidad de peticiones en estos escenarios. En ambos runs, los tres p95 mejoraron frente a su baseline. Se reducen asignaciones de Journal y miniaturas; **Gastos no demuestra reducción de asignaciones**: presenta un pequeño incremento inferior al 0,1 % en ambos runs. No se reutilizan los valores de asignaciones de snapshots anteriores para describir la fuente final.

El p95 optimizado de Journal varió de **63,4582 a 27,4028 ms** entre runs con la misma fuente, frente a baselines de 154,2449 y 148,6808 ms. El primer run contiene dos muestras superiores a 50 ms; la confirmación no contiene ninguna. El aumento superior al 10 % respecto del p95 35,2088 ms de un snapshot anterior no se reprodujo en la confirmación. Las lecturas y peticiones se mantienen; el p50 sigue claramente por debajo de la baseline en ambos runs. La causa exacta de la variación no está demostrada: no se atribuye a una regresión de fuente, GC, antivirus, CPU o disco sin evidencia. Esta comparación tampoco determina regresiones en otros flujos.

Esto mide lógica y disco del host, **no Android, frames, memoria nativa ni latencia de producción**. SecureStorage y tratamiento de fotos usan adaptadores controlados; la decodificación nativa de imágenes queda fuera. La primera muestra previa al calentamiento se conserva por separado, pero no equivale a un arranque en frío de la app. No se incorporan tiempos absolutos como aserciones de CI.

Android Release: `scripts/measure-android-startup.ps1` mide arranque de proceso por separado en una instalación QA, sin borrar datos. Conserva WaitTime y TotalTime cuando Android lo informa; una muestra ausente no se convierte en cero. Las métricas cubren lanzamiento/primer dibujo, no login completo ni carga HTTP.

La limpieza de previews conserva su ejecución y seguridad actuales. Instrumentación estructurada mide duración/asignaciones antes de decidir moverla. El timer de Viaje conserva su intervalo; la medición sólo se activa con diagnósticos de revisión. No se atribuye a estos componentes una causa de lentitud sin evidencia.

## Android: medición histórica registrada QA137 (6 de octubre de 2026)

Capturas Release en el Android conectado, modelo **SM_S948B**, build **137**. Cada escenario usa cinco calentamientos excluidos y treinta muestras válidas: guardado offline de Journal, guardado offline de Gastos y lectura de una miniatura en un diario con 250 recuerdos. Los dos guardados capturaron 35 operaciones; miniaturas capturó 39, de las que se excluyeron cinco calentamientos y cuatro muestras adicionales. Los tres análisis indican muestras completas, cero líneas malformadas y cero campos numéricos inválidos.

| Operación Android QA137 | Duración p50 / p95 (ms) | Asignaciones p50 / p95 (bytes) | HTTP p50 / p95 | Lecturas p50 / p95 | Bytes leídos p50 / p95 |
|---|---:|---:|---:|---:|---:|
| Guardar recuerdo offline | 72,782 / 126,931 | 403.512 / 661.872 | 0 / 0 | 2 / 2 | 25.650 / 26.229 |
| Guardar gasto offline | 52,091 / 111,212 | 146.368 / 148.760 | 0 / 0 | 1 / 1 | 6.443 / 6.516 |
| Leer una miniatura cifrada | 31,614 / 40,677 | 132.296 / 139.736 | 0 / 0 | 1 / 1 | 12.266 / 12.596 |

Evidencia: `summary.json` en `artifacts/daily-ux-20261006/native-journal-137-analysis`, `native-expense-137-analysis` y `native-thumbnail-137-analysis`; los JSONL y manifiestos de captura permanecen en su directorio padre. En v137, inicio y fin usan el contador global preciso `GC.GetTotalAllocatedBytes(true)`. Las asignaciones corresponden al intervalo global del proceso y pueden incluir trabajo simultáneo; no son memoria retenida ni uso de CPU. Un contador de asignaciones ausente o sin avance no se convierte en cero. Los runs 135/136 se excluyen de esta performance definitiva; en v136 el contador no preciso devolvió ceros que no permiten afirmar ausencia de asignaciones.

En QA137, el p95 de operación es **126,93 ms** al guardar un recuerdo offline, **111,21 ms** al guardar un gasto offline y **40,68 ms** al leer una miniatura. Son intervalos instrumentados del store, con cifrado y lecturas cuando corresponden; no representan todo el tiempo percibido entre el botón y el repintado ni la decodificación/renderizado completo. **No existe una baseline Android comparable de `51c9962` para estos escenarios**, por lo que no se afirma una mejora antes/después en el teléfono ni se comparan estas cifras con los resultados Windows. Son las últimas mediciones completas registradas de estas operaciones; no se etiquetan como nuevas mediciones de QA138/139.

## Android: arranque, limpieza y timer históricos QA139 (6 de octubre de 2026)

El arranque de proceso frío de QA139 se midió en el mismo Android mediante cinco calentamientos y treinta repeticiones. Las treinta muestras incluyen el tiempo de primer dibujo informado por Android. Evidencia: `artifacts/daily-ux-20261006/native-cold-139/final-release-v139-startup-summary.json`, junto con CSV y salida original del ejecutor.

| Métrica de lanzamiento QA139 | p50 (ms) | p95 (ms) |
|---|---:|---:|
| Espera de lanzamiento (WaitTime) | 352 | 392 |
| Primer dibujo informado | 352 | 391 |

Estos tiempos llegan a la primera actividad y al dibujo que informa Android; no incluyen desbloqueo/login completado, red ni disponibilidad de todo el contenido. No existe una baseline comparable para afirmar una mejora de arranque.

| Operación instrumentada QA139 | Duración p50 / p95 (ms) | Asignaciones p50 / p95 (bytes) | HTTP | Lecturas | Bytes leídos |
|---|---:|---:|---|---|---|
| Limpieza de previews al arrancar | 1,490 / 1,523 | 14.576 / 14.576 | Desconocido | Desconocido | Desconocido |
| Callback del timer de Viaje | 0,322 / 0,548 | 1.728 / 1.728 | Desconocido | Desconocido | Desconocido |

Los análisis `native-cleanup-139-analysis/summary.json` y `native-timer-139-analysis/summary.json`, bajo el mismo directorio de artefactos, tienen treinta muestras válidas, cero líneas malformadas y cero campos numéricos inválidos. Limpieza capturó 35 operaciones: cinco calentamientos excluidos y treinta medidas. Timer capturó 46: cinco calentamientos, treinta medidas y once adicionales excluidas. En ambos casos las asignaciones pertenecen al **hilo actual**, a diferencia de los intervalos globales QA137; no representan memoria retenida ni uso de CPU. HTTP, lecturas y bytes carecen de contador en estas operaciones y permanecen desconocidos, sin sustituirlos por cero. Tampoco se atribuye un resultado de cancelación o éxito a las muestras que no lo registran.

`native-runtime-139-manifest.json` conserva el conjunto sintético y la alineación temporal: el reloj del dispositivo estaba aproximadamente 1,8 segundos detrás del host; el filtro de limpieza empezó tres segundos antes del inicio del ejecutor y recuperó exactamente las 35 operaciones de los 35 lanzamientos. La limpieza usó el directorio de previews vacío/conocido de QA, no una prueba de acumulación masiva. Estos resultados no justifican mover la limpieza ni cambiar el intervalo del timer; ambos conservan su comportamiento. Las mediciones QA139 no sustituyen los escenarios de guardado y miniaturas QA137.

## Cobertura nativa histórica: 6 de octubre de 2026 (QA137–QA141)

Los recorridos y límites siguientes conservan la cobertura disponible al terminar
el 6 de octubre. Las interrupciones y versiones sin instalar corresponden a ese
corte; el estado actual, las pruebas posteriores y QA147 se describen al comienzo
del informe. Las capturas históricas no se recertifican como pruebas de QA147.

La revisión funcional documentada incluye:

- Journal QA137 con 250 recuerdos, desplazamiento y regreso conservando la posición. Evidencia: `qa137-journal-250`, `qa137-journal-scroll` y `qa137-journal-scroll-return`, en PNG/XML bajo `artifacts/daily-ux-20261006`.
- Galería de tres fotos, acceso a la tercera, detalle y editor durante la revisión. Las capturas `qa136-gallery`, `qa136-gallery-third` y `qa135-journal-editor` son evidencia funcional de esas versiones; no se usan para performance definitiva de v137.
- Guardado offline de recuerdos y gastos, y desglose de gastos sin conexión: `qa137-journal-offline-saved`, `qa137-expenses-offline-saved` y `qa137-expense-breakdown-offline`.
- Cierre de gasto modificado con confirmación; continuar conserva el formulario y descartar cierra sin guardar. Captura `qa135-expense-discard-confirm` y comprobación funcional informada durante QA.
- Conflicto de gasto resuelto mediante mantener la versión local: `qa136-expense-conflict-resolved`.
- Comparación QA138: `qa138-comparison-choice` confirma que no aparece Añadir mientras se comparan alternativas; `qa138-comparison-selected` muestra las 12 ideas después de elegir la propuesta actual. Capturas PNG/XML en el mismo directorio.
- Búsqueda QA138 a 320 dp y texto 200 %: con el teclado abierto se conservan búsqueda y cierre; la acción Buscar del IME cierra el teclado y restaura la cabecera. Se desplaza el resultado hasta Abrir, se abre el detalle y Atrás conserva la consulta «museo» y la posición. Evidencia: `qa138-search-keyboard-320-200`, `qa138-search-results-320-200`, `qa138-search-scrolled-320-200`, `qa138-search-open-320-200` y `qa138-search-normal`, en PNG/XML.
- Viaje QA138 en tamaño normal: `qa138-viaje`, en PNG/XML. La captura acredita esa presentación concreta y no todos los estados de la pantalla.
- Preparación/Documentos QA139 con PDF sintético `YukuSyntheticHotel`: cancelar el selector conserva 0/4 (`qa139-folder-picker-cancel`); guardar en Alojamiento muestra 1/4 y el mensaje Guardado (`qa139-folder-document-saved`). Se abrió sin conexión en el visor PDF local de Drive y se leyó el contenido ficticio, sin añadir el archivo a Drive (`qa139-document-offline-open`).
- Mover ese documento a Transporte vacía Alojamiento y deja Transporte organizada, Alojamiento pendiente y resumen 1/4 (`qa139-documents-moved`, `qa139-folder-moved`). Eliminar la última copia tras confirmar deja ambas categorías pendientes y 0/4 (`qa139-folder-last-document-deleted`). Las capturas son PNG/XML del mismo directorio; al terminar se restauraron Wi-Fi y datos móviles.
- Asistente QA139: búsqueda guiada rápida en tamaño normal, desplazamiento por los filtros y acción Ver ideas revisados (`qa139-assistant-guided`).
- Rato libre QA139 completo: se retiraron únicamente los permisos de ubicación de la app QA, se volvió a ingresar con PIN y se denegó la ubicación opcional. Una ventana futura de 120 minutos desde las 14:00 de Tokio produjo dos alternativas de 60 minutos con estimaciones y advertencias (`qa139-freetime-response`). Otra opción reemplazó la primera sugerencia sintética Tokyo 16 por Tokyo 4 y conservó Tokyo 28 (`qa139-freetime-replacement`). Guardar abrió el editor de agregado existente (`qa139-freetime-add-editor`); Agregar mostró la advertencia de planes flexibles en el mismo bloque y Agregar igualmente confirmó el cambio en Viaje (`qa139-freetime-added-confirmed`). Los permisos de ubicación precisa/aproximada de QA se restauraron; no se cambió el GPS global.
- Revisión en inglés QA139 a 600 dp y fuente 150 %: PIN, Viaje, filtros de búsqueda rápida del Asistente, Journal con 250 recuerdos y tres fotos, Pase con funciones gratuitas explícitas y CTA Restaurar, y Mapa con preview breve, View details, Show less e iconos fijos. Evidencia PNG/XML: `qa139-viaje-english-600-150`, `qa139-guided-english-600-150`, `qa139-journal-english-600-150`, `qa139-paywall-free-benefits-english`, `qa139-map-preview-english-600-150` y `qa139-map-detail-english-600-150`. La venta estaba deshabilitada en el backend local; no se ejecutó una compra real.
- Asistente conversacional inglés QA139: el pedido nearby produjo tarjetas y help mostró la ayuda de fallback; con el teclado abierto, entrada y Send permanecieron accesibles. Capturas `qa139-chat-guided-english-600-150` y `qa139-chat-guide-response-english`. No se certificó una pregunta guiada de múltiples opciones generada por el servidor ni la lectura completa de una descripción larga.

Para la revisión inglesa se cambió sólo el idioma por app de QA a en-US, sin alterar el idioma global. Al terminar se restauraron el locale por app vacío, fuente 100 %, densidad original 600, Wi-Fi y datos activos y `stay_on_while_plugged_in=2`; el idioma del sistema sigue siendo es-ES. La revisión no dejó los ajustes temporales de idioma, fuente o densidad aplicados al teléfono.

**Las pruebas con TalkBack quedan excluidas por petición expresa del usuario, ahora y en futuras sesiones.** Se activó temporalmente durante un intento inicial, se denegó el permiso de llamadas y no se completó ningún recorrido con el lector. Al recibir la indicación se detuvo inmediatamente y se restauraron los servicios de accesibilidad originales, incluido GameBooster+, y `accessibility_enabled=1`. No se certifica cobertura con TalkBack ni se deja como trabajo pendiente para después. Esta preferencia está registrada en [AGENTS.md](../AGENTS.md), archivo versionado del repositorio. VoiceOver no se ejecutó: el host de revisión dispone de Android, no de un dispositivo iOS.

La revisión punto por punto registra implementación y pruebas lógicas para las funciones del plan. El corte QA140 registró **568 Mobile / 557 API / 75 Shared**; la revisión posterior eleva Mobile a **642** y compila **QA141**, como se detalla arriba. La aceptación distingue esa implementación de la cobertura física, con la excepción de lector de pantalla indicada por el usuario. Arranque, limpieza, timer, documentos/preparación, Rato libre y las pantallas inglesas QA139 tienen la evidencia descrita; la revisión funcional QA138 y los intervalos de operación QA137 conservan sus versiones de origen. **No se afirma haber completado toda la matriz de pruebas nativas.** No se replicaron físicamente todas las combinaciones de viaje gratuito, borradores y permisos, ni una pregunta guiada del servidor con múltiples opciones ni la descripción larga completa; la lógica correspondiente cuenta con las pruebas automáticas enumeradas. La comprobación de teclado y texto al 200 % corresponde a Búsqueda; el 150 % en inglés corresponde únicamente a las pantallas y estados enumerados. Estas limitaciones quedan explícitas, sin certificar escenarios a partir de instrumentación disponible o capturas parciales. Las optimizaciones del timer o de la limpieza no se consideran demostradas por el benchmark de Journal/Gastos ni por una medición actual sin baseline equivalente.

En ese corte, el usuario desconectó el teléfono y pidió continuar con código y pruebas locales. ADB dejó de detectarlo al intentar revisar Mañana QA139 y una comprobación táctil final de los ajustes restaurados. **No se ejecutaron más pruebas nativas durante ese cierre del 6 de octubre.** La restauración ya había sido confirmada antes de desconectar: servicios originales GameBooster+, fuente 100 %, densidad física 600, `stay_on_while_plugged_in=2`, locale QA vacío y sistema es-ES; los comandos para activar Wi-Fi y datos también habían finalizado. La ausencia del teléfono no invalidó las comprobaciones y mediciones ya completadas ni convirtió una pantalla implementada en trabajo de código pendiente. La revisión física se retomó el 7 de octubre, como registra el estado actual de QA147.

Al cierre histórico del 6 de octubre se distinguían estos estados de verificación:

- **Interrumpidos por falta de teléfono:** recorrido final de Mañana en QA139 y comprobación táctil adicional de los ajustes ya restaurados. No se certifican a partir de capturas de versiones anteriores.
- **Sin instalación ni revisión física por falta de teléfono:** QA140 y QA141 con las correcciones posteriores. Su compilación y las pruebas lógicas están completadas; no se traslada a estas versiones la cobertura física de QA139. No hay emuladores configurados en el SDK local.
- **Cobertura nativa parcial:** no se reprodujo toda la matriz de viajes gratuitos, borradores/permisos, pregunta guiada del servidor con múltiples opciones y descripción larga completa. Son límites de comprobación física; las pruebas lógicas existentes se conservan y pueden seguir ejecutándose sin Android.
- **Excluido por decisión permanente del usuario:** TalkBack. No es un pendiente para reconectar después. VoiceOver también permanece sin ejecutar por falta de un dispositivo iOS en este host.
- **Completados antes de desconectar:** los recorridos y mediciones concretos enumerados arriba, suites automáticas y compilación/publicación/instalación QA139. No se declara una compra real, un recorrido de lector ni una baseline Android que no se hayan ejecutado.

## Publicación posterior

La sesión nueva conserva lectura del token antiguo, pero publica mediante una referencia a un token provisional cifrado. Una APK anterior no conoce esa referencia y deberá volver a autenticar al usuario. Las intenciones pendientes se conservan en formato v2 por propietario; una APK anterior sólo entiende el slot v1. Para revertir, conservar los datos cifrados y preferir una corrección posterior con lectura compatible: no borrar ni convertir manualmente recibos pendientes. La migración gradual de fotos también exige mantener soporte de los archivos separados para que todos los originales sigan visibles. Reautenticar no requiere eliminar documentos, fotos, borradores ni libros de gastos.

1. Conservar los resultados definitivos actuales y el artefacto QA147 instalado: [Yuku-QA-v147.apk](../artifacts/native-acceptance-20261007/Yuku-QA-v147.apk), SHA-256 `9AF8E8E278B485F8F4B7677A62241111F0D91FAFC31D277A72B341ACC2F342B3`, junto a la [evidencia final del snapshot](../artifacts/native-acceptance-20261007/closure-evidence-147.json). Completar los checks nativos pendientes antes de publicar. El corte QA147 previo al admin y QA139/141 se conservan como historia, con sus versiones y límites; no sustituyen la evidencia actual. QA147 usa la identidad separada de revisión; no reemplaza una APK de producción firmada con la identidad habitual. Mantener la exclusión permanente de TalkBack. Investigar regresiones reproducibles superiores al 10 % sólo con mediciones comparables; los escenarios Android registrados carecen de baseline equivalente de `51c9962`.
2. Publicar primero la API con los campos opcionales de ventana; móviles anteriores siguen enviando el contrato previo. Los criterios sin ventana conservan el ranking actual.
3. Publicar APK/AAB con versión incrementada y firma habitual; instalar sobre la app existente conserva datos. La APK QA usa otro package y backend local.
4. Comprobar login/PIN, guardar offline y con HTTP lento, sincronización/replay, propuestas y permisos de documentos/pase.
5. Revertir API a versión anterior conserva tablas y datos, pero el servidor debe seguir validando las ventanas de los clientes activos. No basta con que una versión anterior ignore esos campos: podría proponer planes fuera del tiempo elegido. Si el cliente con Rato libre ya fue publicado, conservar el backend compatible o aplicar una corrección hacia delante; sólo volver al backend anterior cuando ningún cliente activo pueda enviar ventanas sin validar. No se presupone un interruptor remoto para desactivar esta función.
6. Para revertir móvil conservar datos locales: no desinstalar ni limpiar almacenamiento. La división de fotos conserva los originales; una versión anterior que sólo conoce payload combinado no puede leer nuevos payloads divididos. Preferir una corrección hacia delante o exportar/respaldar desde la versión compatible antes de una reversión de cliente. No borrar contenido personal.

## Límites conocidos

Los índices locales de Journal/Gastos siguen siendo snapshots JSON cifrados por viaje; el coste de escritura aumenta con una colección muy grande. Esta entrega elimina esperas de red y lecturas de fotos completas en listas, sin sustituir el almacenamiento local. Fotografías y borradores siguen sólo en el dispositivo. Las duraciones y traslados son estimaciones, no garantías de apertura o llegada. No se miden conversiones ni latencia de producción con datos personales.

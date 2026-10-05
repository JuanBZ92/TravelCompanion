# Validación del planificador de días

La revisión posterior del selector fecha/hotel, los controles de duración y el reemplazo individual de tarjetas se documenta en [Ajustes de planificación y selector del viaje](day-planner-refinement.md). Ese corte tiene 506 pruebas API, 350 móviles y 69 Shared aprobadas, y APK de revisión v117. Los resultados y artefactos v116 de este documento corresponden al corte anterior; sus recorridos nativos pendientes siguen registrados.

Revisión inicial: 4 de octubre de 2026. Revisión complementaria: 5 de octubre de 2026. Datos sintéticos exclusivamente. PostgreSQL 17.10 local, .NET 10, Windows. Los resultados locales de esta revisión no implican push, migraciones ni publicación en producción.

## Pruebas automatizadas

- API: suite completa con PostgreSQL habilitado, **476 pruebas aprobadas, cero fallos y ninguna omitida**. Incluye 26 casos sobre PostgreSQL real, trece específicos del planificador y ocho casos de singular/plural de días e ideas en español e inglés. Compilación de API y worker sin advertencias ni errores.
- Shared: **66 pruebas aprobadas y ninguna omitida**, incluidas compatibilidad de opciones y propuestas antiguas, fechas, ciudades de traslado, lugares, identificadores de selección y preferencias opcionales.
- Móvil: **321 pruebas aprobadas, cero fallos y ninguna omitida**, incluido el ajuste de virtualización de listas largas y los textos finales ES/EN. Resultado: `artifacts/day-planning-tests/mobile-final/mobile-final.trx`. Son pruebas de lógica; la interacción y las métricas nativas de las listas nuevas permanecen pendientes en la auditoría de pantallas.

El planificador se valida para 1, 3, 5 y 7 días; ritmo tranquilo, equilibrado e intenso; cambio de ciudad; catálogo insuficiente; permisos y radio gratuitos; dieta; ausencia de perfil; muchos planes existentes; selección parcial; cancelación; revisión desactualizada y aislamiento entre cuentas y viajes.

Las trece pruebas específicas sobre PostgreSQL verifican reintentos simultáneos de generación, tres usos gratuitos máximos, dos generaciones pagadas concurrentes y su contador diario, aplicación concurrente de un lote —también cuando todos los lugares ya existen—, revisiones en conflicto, reintento después de eliminar un elemento, rechazo de selecciones inválidas, rollback ante un fallo SQL inducido y fallos de analítica que no revierten datos confirmados. La revisión complementaria añade cambios de acceso durante una generación o replay, conservación del resultado canónico después de cambiar la revisión y proyección del contexto de ciudad sin materializar notas/confirmaciones de reservas. La prueba de migración comprueba `jsonb`, los índices de recibos y que la reversión conserva los planes ya confirmados.

Los reintentos de guardado usan recibos persistidos. Repetir la misma mutación devuelve el resultado anterior; cambiar la selección con esa mutación provoca conflicto. El fallo SQL de prueba no deja reservas parciales, cambios de revisión ni recibos de una aplicación fallida.

Los nuevos casos móviles cubren recuperación de propuesta, selección, preferencias y mutación pendiente tras reinicio; caché antigua; cuentas y viajes separados; cambio de cuenta durante lectura; eliminación y escritura tardía; vinculación de una cuenta anónima, incluso si cambia el viaje; autorización HTTP; errores estructurados, respuestas inválidas y cancelación. El ViewModel de producción se enlaza al proyecto de pruebas; sus casos cubren timeout, identidad de reintentos, fechas y preferencias restauradas, selección parcial, nuevas revisiones, errores de guardado local y cambio de contexto. Las pruebas usan I/O controlado y stubs de dependencias nativas; no verifican renderizado, TalkBack ni el cifrado real del dispositivo.

La validación detectó y permitió corregir una carrera real: una transacción que esperaba el bloqueo del grant/viaje mantenía un snapshot anterior y no veía el lease o recibo recién confirmado. Las operaciones nuevas usan `ReadCommitted` con bloqueo explícito, y las pruebas concurrentes confirman un único resultado, contador y lote. También se comprueba que archivar/despublicar el viaje o eliminar la cuenta impide recuperar resultados persistidos desde una sesión antigua. Los eventos existentes de conversión se registran una vez y respetan consentimiento y exclusión de cuentas internas/demo.

En la revisión complementaria se reprodujo otro fallo con PostgreSQL: una despublicación durante la generación podía permitir su finalización. La finalización y el replay concurrente ahora revalidan el grant, el propietario no eliminado y el viaje publicado/no archivado dentro de la transacción. Se conserva la respuesta canónica después de un cambio de revisión legítimo; una pérdida de acceso impide confirmarla o recuperarla. La prueba anterior a la corrección queda en `artifacts/day-planning-audit/planner-completion-before.trx`; las suites finales aprobadas están en `api-full-audit.trx` y `shared-full-audit.trx` del mismo directorio.

El formulario muestra ciudades y fechas antes de generar, incluso cuando no hay reservas; `CityDays` comparte la resolución de segmentos con la propuesta, incluidos los días de traslado y el fallback de reservas de varios días. Las tarjetas muestran `Place` como lugar, sin reutilizar el subtítulo temporal. Los nuevos campos son aditivos y los datos locales antiguos tienen fallback. Las pruebas móviles cubren recuperación y selección pendiente después de una respuesta perdida, revisión desactualizada, contexto del rango y lugar explícito/antiguo. Las listas, textos ampliados, foco y TalkBack requieren evidencia Android propia.

El recorrido nativo de un día detectó el resumen inglés «1 days». El mensaje ahora distingue singular/plural de días e ideas en ambos idiomas, incluidos resultados escasos con una sola idea. Ocho casos nuevos verifican esas combinaciones; el contenido y orden de las propuestas no cambian. El corte focal final aprobó 66 pruebas del planificador, incluidas las trece PostgreSQL, antes de repetir la suite API completa.

La presentación móvil conserva el rango y las preferencias durante la recuperación, informa los eventos que ya existen y actualiza su contador después de guardar. Los errores quedan visibles fuera del scroll y reciben foco; la cabecera extensa de resultados se desplaza con la lista para dejar espacio a la selección y a la acción principal. La fase final convierte las listas extensas de Viaje y Documentos a una presentación virtualizada. Estos cambios de estructura tienen validación de lógica y compilación; su renderizado final con texto ampliado y TalkBack sigue requiriendo un recorrido nativo.

## Medición reproducible

Ejecutar con una base PostgreSQL **local y desechable**. El ejecutor rechaza hosts remotos y crea un esquema aislado que elimina al terminar. No lee la cadena de conexión de la aplicación.

```powershell
$env:TRAVELCOMPANION_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Pooling=true'
dotnet run --project tools/DayPlanPerformance/DayPlanPerformance.csproj --configuration Release -- artifacts/day-planning-audit/performance-final-repeat.json
```

Cada escenario contiene 1.000 recomendaciones, dos ciudades y 100 planes existentes. Se ejecuta una primera llamada, cinco calentamientos y treinta repeticiones; se mantiene el mismo proceso y la misma configuración. Cada repetición utiliza un contexto nuevo y una operación nueva. El tiempo incluye la lectura, la generación y la persistencia del resultado con su consumo de cuota, además del servicio de analítica con consentimiento desactivado. La primera llamada se registra separadamente, pero no representa un arranque frío de proceso para cada escenario. Las tablas y grants del benchmark permanecen aislados en su propio esquema desechable. La primera serie complementaria compartió el equipo con la revisión Android y su API local; la repetición final se coordinó sin builds ni pruebas concurrentes.

Resultados de la repetición final con pooling de conexiones activo:

| Días | p50 | p95 | Comandos SQL | Consultas de catálogo | Asignaciones de memoria p50 |
|---|---:|---:|---:|---:|---:|
| 1 | 66,9 ms | 85,4 ms | 29 | 2 | 10,9 MB |
| 3 | 95,0 ms | 121,5 ms | 29 | 2 | 21,9 MB |
| 7 | 56,9 ms | 75,8 ms | 29 | 2 | 42,5 MB |

Las primeras llamadas de la repetición fueron 785,6, 165,2 y 173,2 ms respectivamente. La primera serie complementaria tenía p50 de 72,0/72,1/49,3 ms y p95 de 84,9/93,3/103,8 ms; la serie inicial del 4 de octubre tenía p50 de 76,6/84,6/66,1 ms y p95 de 100,1/103,4/88,4 ms. El aumento aproximado del 17 % del p95 de siete días no se repitió: bajó a 75,8 ms. En cambio, el p95 de tres días subió a 121,5 ms, cuando en la primera serie complementaria había bajado a 93,3 ms. Se registra toda esa variación; no demuestra una regresión reproducible, una mejora causal ni que una regresión haya quedado resuelta. Una comparación controlada debe ejecutar ambas versiones en el mismo entorno aislado y repetir cualquier diferencia superior al 10 %; estas series del nuevo flujo no sustituyen esa comparación.

En la revisión inicial, el mismo ejecutor también se midió con `Pooling=false`, heredado del entorno aislado de las pruebas PostgreSQL:

| Días | p50 sin pooling | p95 sin pooling | Comandos SQL |
|---|---:|---:|---:|
| 1 | 1.355,3 ms | 1.529,7 ms | 29 |
| 3 | 1.385,0 ms | 1.450,7 ms | 29 |
| 7 | 1.349,7 ms | 1.567,7 ms | 29 |

Los dos comandos de catálogo recuperan candidatos y detalles una vez por propuesta. El número de comandos permanece constante al añadir días; el ranking y las asignaciones de memoria crecen con los días procesados. Las asignaciones se estiman mediante `GC.GetTotalAllocatedBytes`, se expresan en MB decimales y no son memoria retenida. Las diferencias entre duraciones también incluyen el calentamiento progresivo y la variación del equipo: siete días no son intrínsecamente más rápidos que uno. En este runner, desactivar pooling introduce un coste considerable asociado a conexiones nuevas; esto no demuestra el origen de la latencia de producción. No se modificó la configuración de la aplicación o de infraestructura. La serie inicial ya corresponde al planificador múltiple; no es una comparación contra una función anterior equivalente de planificación múltiple, porque ese flujo no existía.

Los informes complementarios completos, incluidas todas las muestras, primeras llamadas y configuración de pooling, quedan en `artifacts/day-planning-audit/performance-final.json` y `performance-final-repeat.json`, con sus logs del mismo nombre. Al repetir, usar otra ruta de salida si se deben conservar las muestras anteriores. La serie inicial con pooling se conserva en `artifacts/day-plan-performance.json` y la serie sin pooling en `artifacts/day-plan-performance-no-pooling.json`. Los tiempos locales no se usan como umbrales de CI ni demuestran la latencia de la red o del backend de producción.

## Repetir la validación crítica

```powershell
$env:TRAVELCOMPANION_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Pooling=false'
dotnet test tests/TravelCompanion.Api.Tests/TravelCompanion.Api.Tests.csproj --verbosity minimal --filter 'FullyQualifiedName~DayPlanServiceTests|FullyQualifiedName~DayPlanEndpointTests|FullyQualifiedName~PostgresDayPlanTests'
dotnet test tests/TravelCompanion.Shared.Tests/TravelCompanion.Shared.Tests.csproj --verbosity minimal
dotnet test tests/TravelCompanion.Mobile.Tests/TravelCompanion.Mobile.Tests.csproj --verbosity minimal --filter 'FullyQualifiedName~DayPlannerStoreTests|FullyQualifiedName~DayPlanClientTests|FullyQualifiedName~DayPlannerViewModelTests'
```

Las pruebas PostgreSQL conservan la convención del repositorio y aparecen omitidas si falta la variable. Para una entrega válida debe estar configurada, todas las pruebas específicas PostgreSQL deben ejecutarse y su informe no debe contener omisiones. La validación de esta revisión se ejecutó con la variable configurada. Los logs y resultados TRX complementarios de API/Shared están en `artifacts/day-planning-audit/`; los de la entrega inicial se conservan en `artifacts/day-planning-tests/`. El corte móvil final posterior a la virtualización y los textos está en `artifacts/day-planning-tests/mobile-final.log` y `artifacts/day-planning-tests/mobile-final/mobile-final.trx`.

La publicación y reversión se describen en el [procedimiento de publicación del planificador](day-planner-rollout.md). No se ejecutaron cambios en producción durante estas pruebas.

## Revisión del cumplimiento

| Punto | Evidencia y estado |
|---|---|
| Propuesta de 1/3/5/7 días y preferencias unificadas | Implementado; API, contratos y ViewModel probados. Perfil guardado y preferencias temporales se mantienen; gratis conserva tres generaciones y la ventana de los tres primeros días; 5/7 requieren pase. |
| Ciudades, lugares, catálogo escaso, reglas de acceso y planes existentes | Implementado; `CityDays` muestra el contexto antes de generar y comparte la resolución de la propuesta. `Place` muestra el lugar del catálogo con fallback de ciudad. El ranking utiliza catálogo autorizado, informa momentos sin cobertura, excluye lugares ya guardados y no introduce un máximo de planes del viaje. Generar no escribe reservas. |
| Revisión, selección y guardado transaccional | Implementado; identidad persistida, reintentos, cambios de revisión, selección parcial, concurrencia, rollback y elementos eliminados verificados. Finalización y replay revalidan el acceso vigente. |
| Recuperación local y protección del contexto | Implementado; propuestas, selección, preferencias y operaciones pendientes recuperables; aislamiento, cancelación, cambio de sesión, vinculación y eliminación probados. |
| Compatibilidad y demo | Endpoints y reglas legacy conservados; suite API completa aprobada. Las cuentas demo/internas conservan su exclusión de analítica. El nuevo flujo no reintroduce endpoints antiguos retirados. |
| Migración y publicación posterior | Migración aditiva probada en PostgreSQL local, incluidos índices, `jsonb` y reversión sin borrar planes. SQL preparado en `artifacts/day-planner-up.sql`; orden de publicación, límites de bloqueo y reversión documentados. |
| Medición de 1/3/7 días | Ejecutada con PostgreSQL local, cinco calentamientos y treinta repeticiones. Resultados y límites de comparabilidad descritos arriba. |
| Auditoría completa de pantallas y mejoras prioritarias | Inventario de 33 páginas concretas, dos paneles y estados internos; revisión estática y cambios registrados en la [auditoría móvil](mobile-ux-audit-2026-10.md). No equivale a revisar todas las pantallas en Android. |
| Español, inglés y accesibilidad | Recursos modificados en ES/EN, descripciones semánticas y controles de al menos 48 dp revisados estáticamente. Las pruebas de lógica incluyen localización de períodos y presentación. El alcance nativo de texto ampliado, TalkBack, foco y teclado se registra por recorrido en la auditoría móvil; no se afirma cobertura completa. |
| Builds y APK separada | API y worker compilados sin advertencias ni errores; Shared y lógica móvil aprobados. La compilación Android final, incluidas las nuevas listas, aprobó con cero advertencias/errores. APK de revisión v116 separada y firma verificada; esta recompilación no se instaló. |
| Revisión visual en Android | El bloqueo inicial de ADB se resolvió y hubo recorridos parciales con datos sintéticos. Posteriormente, el usuario pidió continuar sin teléfono; la virtualización y los períodos del corte final quedan pendientes de inspección nativa. Capturas, recorridos y pendientes figuran en la auditoría móvil. |
| Listas extensas de planes/documentos | `--large-lists` preparó cien planes manuales y cien documentos incluidos sintéticos, sin duplicados al repetir. Viaje y Documentos ahora virtualizan filas individuales, sin máximos. Pruebas de orden/identidad, cien reservas, quinientas filas y permisos aprobadas; el ahorro y reciclado nativos del ajuste final están pendientes. |
| Push, migración y despliegue en producción | Fuera del alcance de esta revisión complementaria; no realizados durante ella. |

## APK y comprobación HTTP final

La APK final es `artifacts/mobile/DayPlannerReview-v116.apk`, paquete `com.yuku.travelcompanion.plannerreview`, versión 116 y nombre visible `Yuku Planner`, con ARM64 y x86_64. La compilación final, incluidas las listas y textos nuevos, aprobó con cero advertencias/errores en 1:27,87; log `artifacts/mobile/planner-completion-build-final.log`. La firma se verificó correctamente, incluidos los esquemas v2/v3. Tamaño: 93.492.512 bytes; SHA-256: `F2CFA2B1E1C157A11BB38D6A35BA87615EDC361A0F184750EF0D74FCE4553814`.

El backend configurado es exclusivamente `http://127.0.0.1:5188`. La app habitual `com.yuku.travelcompanion.app`, versión 115, conserva su instalación y datos: la revisión utiliza otra identidad. Esta recompilación final no se instaló ni revisó nativamente porque el usuario pidió continuar sin teléfono. El corte v116 anterior sí se instaló y se revisó parcialmente; su SHA-256 era `0C7A852AEF09E43FFE626AF6AF056E6174A84271573F4BF25CD55105763A2069`. Compartir número de versión no implica que ambos artefactos sean idénticos. La auditoría distingue expresamente los recorridos anteriores de los pendientes.

Esta revisión complementaria no modifica el esquema y no requiere una migración PostgreSQL adicional. La migración del planificador y su procedimiento se conservan de la fase anterior. Se detuvo el backend local al cerrar las pruebas; deberá arrancarse otra vez para la revisión Android posterior.

El backend de revisión utiliza la base sintética `tc_dayplanner_review` en `127.0.0.1:55439`. En el corte inicial, `tools/DayPlanReview/Smoke.ps1` comprobó `/health/ready`, autenticación de la cuenta sintética con pase y propuestas de 1/3/5/7 días, con 4/12/20/28 ideas distintas respectivamente. Añadió dos ideas y confirmó que el reintento de la misma mutación devuelve los mismos elementos y revisión. No consumió generaciones de la cuenta gratuita. Evidencia histórica: `artifacts/day-planning-tests/local-api-smoke.log`.

Para las listas extensas, `dotnet run --project tools/DayPlanReview/Seed.csproj -- --large-lists` añade cien planes manuales al viaje Builder y cien documentos incluidos en un viaje curado separado. El ejecutor verifica host, puerto y base antes de escribir; rechaza destinos distintos y no reinicia usuarios, grants ni cuotas. La repetición añadió cero elementos y conservó los snapshots de grants/cuotas. El viaje Builder se abre con el PIN sintético local `700701`; el curado con `600702`, mediante el inicio de sesión por PIN habitual. No se amplían permisos para permitir documentos en Builder. La comprobación HTTP obtuvo 140 planes totales —108 en el día seleccionado—, cien documentos y PDF local válido con HTTP 200. Evidencia: `artifacts/day-planning-audit/large-lists-http.json` y logs `seed-large-lists-*` del mismo directorio. El [procedimiento de revisión](day-planner-rollout.md#revisión-visual-local-aislada) detalla el acceso.

Las comprobaciones iniciales de ADB sin dispositivo se conservan como un bloqueo histórico. La revisión complementaria tuvo acceso al Android y a una APK separada v116, con cobertura parcial real. El usuario pidió después continuar sin teléfono: las nuevas listas virtualizadas y los períodos finales no se consideran inspeccionados en el dispositivo, ni se afirma una nueva instalación de la recompilación final. Las pruebas visuales completadas, los límites y las métricas de listas se documentan en la [auditoría móvil](mobile-ux-audit-2026-10.md), sin sustituirlos por el resultado de pruebas de lógica o builds.

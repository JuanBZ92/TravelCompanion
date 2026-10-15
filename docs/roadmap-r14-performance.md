# R14 — Rendimiento medido

## Alcance y criterio

Medir antes de optimizar. La aceptación vive en [r14-performance.md](plans/r14-performance.md). Esta entrega amplía los diagnósticos existentes y aprovecha el benchmark de [consultas PostgreSQL](query-performance.md); no incorpora un proveedor de telemetría, una caché, índices ni recursos nuevos.

La APK habitual deja desactivadas estas mediciones. Una APK QA debe compilarse en `Release` con `TravelCompanionDiagnosticsEnabled=true`, identificador independiente terminado en `review` y backend local. El ejecutor no cambia la sesión, autenticación, permisos o URL. Primero se abre la app con PIN. Nunca se activa TalkBack ni biometría ni se modifican densidad, fuente, tema o rotación del teléfono.

## Qué mide cada intervalo

| Evento | Incluye | No acredita |
|---|---|---|
| `bootstrap_refresh_measured` | Petición, deserialización y persistencia cifrada de bootstrap | Arranque frío o primer frame |
| `schedule_load_measured` | Carga de Viaje desde caché y actualización que requiera el flujo | Final de renderizado ni un día sin caché si la fixture ya la tiene |
| `journal_local_open_measured` | Bootstrap disponible, lectura local y construcción/reconciliación de filas | Sincronización remota posterior ni decodificación/renderizado completo de fotos |
| `day_planning_measured` | Generación solicitada al backend y lectura del resultado JSON | Aplicación de la propuesta al itinerario ni coste de IA si está deshabilitada en QA |

Duración mediante `Stopwatch`, asignaciones globales precisas, peticiones lógicas del handler HTTP, lecturas de caché y bytes JSON leídos. Los reintentos internos pueden producir más intercambios de red que solicitudes lógicas. Estos contadores cubren el proceso durante el intervalo y pueden incluir trabajo simultáneo. No son memoria retenida ni CPU. El timer y la limpieza de documentos mantienen sus mediciones anteriores de hilo actual.

`KnownResponseBytes` suma los `Content-Length` disponibles. `UnknownLengthResponses` cuenta las respuestas sin esa cabecera. Un tamaño parcial de cero con respuestas desconocidas **no significa que la respuesta esté vacía**. No se leen ni se vuelven a serializar cuerpos para medirlos. Tampoco se registran URLs, parámetros, tokens, notas, títulos, identificadores ni rutas de archivos. iOS conserva estos eventos en el diario de diagnóstico exportable; el ejecutor automático de esta entrega es Android.

## Reproducción Android

Conservar la APK realmente instalada y un manifiesto de huellas de las fuentes antes de compilar. Usar el mismo dispositivo, configuración, fixture, permisos y backend local para ambas variantes. Cada operación ejecuta cinco calentamientos y treinta repeticiones; el ejecutor espera las sincronizaciones de Journal fuera de su intervalo local para evitar solapar sus propias iteraciones.

```powershell
./scripts/measure-android-operations.ps1 `
  -Device <serial> -Package com.yuku.travelcompanion.plannerpaidreview `
  -Operation journal -ArtifactPath <apk-baseline> -SourceSnapshotPath <fuentes-baseline.json> `
  -Fixture daily-ux-20261016-journal250 -Label baseline `
  -OutputDirectory artifacts/roadmap-r12-r14-20261015/r14/native-baseline
```

Repetir con `bootstrap`, `trip` y `planning`. Instalar después la APK optimizada encima de la misma instalación QA, conservando sus datos, y repetir con una etiqueta y directorio distintos. No mezclar mediciones con compilaciones o cargas intensivas. El ejecutor mantiene despierta únicamente la ventana QA durante el lote y restaura ese indicador al finalizar; `OnStop`, cambio de cuenta/viaje y plazo máximo cancelan el lote.

Los eventos `review_batch_started_measured` y `review_batch_completed_measured` delimitan las 35 operaciones. El analizador rechaza seleccionar silenciosamente entre varios lotes del mismo tipo. Un lote con error, cancelación o menos iteraciones no se acepta en la comparación aunque haya 35 eventos de duración.

```powershell
./docs/tools/compare-native-performance.ps1 `
  -Baseline <baseline-journal-analysis/summary.json> `
  -Current <current-journal-analysis/summary.json> `
  -Operation journal_local_open_measured `
  -OutputDirectory artifacts/roadmap-r12-r14-20261015/r14/native-comparison-journal
```

El comparador exige mismo escenario, fixture, plataforma, modelo, configuración Release y cantidad de muestras, además de hashes de ambas APK y manifiestos. Las APK pueden diferir para comparar el cambio de código; el informe identifica ambas. Si los hashes son iguales, es repetibilidad del mismo artefacto, no demostración de una optimización. Un cambio observado superior al 10 % obliga a repetir e investigar; no hace fallar CI por tiempos absolutos.

Arranque frío se mide aparte con [measure-android-startup.ps1](../scripts/measure-android-startup.ps1). Sus tiempos de lanzamiento/primer dibujo no son la finalización de login, red o pantalla. La revisión visual del flujo se registra aparte del benchmark.

## Conexiones, SQL, payloads y tablas

La API ya mide `Database operation`: comandos, tiempo de comandos, apertura de conexión, filas y edad del proceso. [analyze-database-operations.ps1](tools/analyze-database-operations.ps1) extrae únicamente operaciones constantes y valores numéricos del log local. Los scopes anidados se solapan; no se suman padre e hijo. Un comando EF termina al obtener el lector, antes de toda la materialización. El analizador mantiene nulos los percentiles sin 30 muestras tras los cinco calentamientos.

[measure-local-database-health.ps1](../scripts/measure-local-database-health.ps1) consulta catálogos y estadísticas, en transacción de solo lectura y con límite de cinco segundos. Solo permite PostgreSQL local y bases sintéticas `tc_*`. Registra conexiones, esperas, transacciones abiertas, tamaño de tablas/índices, filas estimadas y estadísticas de lecturas; admite una instantánea anterior para mostrar cambios de tamaño. No hace `COUNT(*)` de todas las tablas ni extrae SQL, contenido o datos de conexiones.

```powershell
./scripts/measure-local-database-health.ps1 -Database tc_dayplanner_review `
  -Psql 'C:/Program Files/PostgreSQL/17/bin/psql.exe' `
  -OutputDirectory artifacts/roadmap-r12-r14-20261015/r14/database-final `
  -PreviousSnapshot artifacts/roadmap-r12-r14-20261015/r14/database-baseline/snapshot.json
```

Las filas de `pg_stat_user_tables` son estimaciones desde `ANALYZE`, no recuentos exactos. El crecimiento entre dos instantes de una carga sintética no permite inferir una tasa mensual de producción. Tampoco un `seq_scan` aislado justifica un índice. Para planes y buffers se conserva el ejecutor local con `EXPLAIN (ANALYZE, BUFFERS)` de la revisión PostgreSQL anterior; sus resultados históricos no acreditan esta APK ni producción.

## Cuello identificado y evidencia

Antes del cambio, `JournalStore.LoadAsync(sync: false)` lee y descifra dos veces el mismo índice cuando hay actividades: una vez para compatibilidad con notas antiguas y otra para devolver el resultado. Una fixture con 250 recuerdos y 30 actividades confirma dos lecturas, aproximadamente 1,93 MB JSON y 25,97 MB asignados por apertura en el host. No requiere notas antiguas nuevas para pagar ese coste. Las primeras ejecuciones coincidieron con compilaciones y quedan como exploratorias; las series definitivas en host y Android se detallan abajo.

La corrección devuelve la instantánea validada bajo el mismo semáforo al abrir localmente y la ordena después de liberarlo. El recorrido con sincronización conserva su relectura posterior para incluir confirmaciones, conflictos y ediciones concurrentes. Las pruebas conservan importación antigua, aislamiento por cuenta/viaje, cancelación, eliminación, portadas, borradores y escritura local independiente de HTTP.

## Resultados del 15 de octubre de 2026

Android físico **SM-S948B**, `Release` ARM64, backend local por USB, catálogo de 128 recomendaciones y **250 recuerdos confirmados**. Misma cuenta, viaje, itinerario y permisos; cinco calentamientos y treinta muestras por operación. No hubo importaciones de calendario ni ediciones del viaje entre las capturas comparables. La planificación generó propuestas de un día sin aplicarlas, con proveedores externos de IA/rutas desactivados. Esto no mide la latencia de producción ni un catálogo de 10.000 lugares.

### Construcción y comparación limpias

La primera revisión nativa detectó un defecto de empaquetado: las primeras QA155/QA156 incluían un ensamblado Shared antiguo procedente de una salida RID reutilizada. Sus datos se conservan, pero **no se usan como evidencia final de esta entrega**. Se reconstruyeron ambas variantes con un único `-r android-arm64` y raíces de salida nuevas. La compilación optimizada final es **QA160**. La baseline limpia conserva el número 156 y se identifica por nombre y hash diferentes a la antigua QA156.

La baseline limpia usa todas las fuentes finales, más un override temporal de `Compile` que sustituye únicamente `JournalStore.LoadAsync` por su recorrido anterior de doble lectura. Conserva la instrumentación y todos los demás cambios. Los manifiestos coinciden en los **681 archivos runtime**; el reemplazo efectivo está declarado por separado en `qa156-clean-baseline-source.json`, junto con la receta y las huellas del archivo compilado y del target MSBuild. `r14/clean-source-equivalence.json` registra esa comparación. No se modificó el archivo de producción para fabricar la baseline.

| Operación | p50 baseline limpia → QA160, ms | p95 baseline limpia → QA160, ms | Trabajo observado |
|---|---:|---:|---|
| Journal local | 154,27 → 76,08 | 184,52 → 123,76 | Lecturas 2 → 1; bytes 2.283.060 → 1.141.530; asignaciones p50 18.725.240 → 9.574.072 B; HTTP 0 → 0 |
| Bootstrap | 117,64 → 122,22 | 149,28 → 156,08 | Una solicitud; asignaciones p50 2.516.832 → 2.517.032 B |
| Viaje desde caché | 17,93 → 18,28 | 53,92 → 47,08 | HTTP/lecturas 0 → 0; asignaciones p50 545.912 B en ambas |
| Generar propuesta de un día | 27,15 → 27,37 | 34,87 → 41,29 | Una solicitud; asignaciones p50 46.336 B en ambas |

**Mejora comprobada de Journal:** p50 −50,7 %, p95 −32,9 %, la mitad de lecturas y bytes, y aproximadamente −48,9 % de asignaciones. No se afirma una mejora de renderizado o CPU a partir de esos intervalos. Bootstrap varió +4,6 % en p95 y Viaje −12,7 %; ambos mantienen el trabajo observado, sin atribuir esas variaciones a una optimización de esos flujos.

### Investigación de planificación

El primer lote QA160 elevó p95 de planificación un **18,43 %**. Tuvo dos intervalos medidos altos, **64,27 y 41,29 ms**; el tercero más alto fue 30,35 ms. Se conservó el lote completo y se repitió la serie de cinco calentamientos y treinta muestras sobre la misma QA160, sin cambiar itinerario, recuerdos o permisos:

| Repetición QA160 | p50, ms | p95, ms | Frente a baseline limpia |
|---|---:|---:|---|
| Generar propuesta de un día | 27,48 | 30,12 | p50 +1,24 %; p95 −13,61 % |

La repetición **no reprodujo el aumento superior al 10 %**. Ambas series conservaron una petición y 46.336 B asignados en p50. El backend mantuvo 30 comandos por generación y cuatro paradas; p95 total 20,85 → 20,25 ms y p95 de comandos 11,54 → 11,01 ms. **La causa del episodio inicial sigue sin demostrarse.** No se atribuye a USB, al sistema operativo, a SQL o a infraestructura. No se usan tiempos absolutos como condición de CI.

Las comparaciones limpias y los lotes de repetición usan únicamente la cuenta sintética local. Para no agotar su cuota QA diaria de 100, se restableció su contador `SuccessfulRequests` antes de los lotes correspondientes. El SQL comprueba base, cuenta, viaje, fecha UTC, una sola fila y cero solicitudes reservadas. No elimina leases ni cambia permisos, itinerario o recuerdos. Receta y resultados quedan en `r14/reset-synthetic-planning-quota.sql` y los archivos de cuota de esta revisión.

### Confirmación independiente en host

El benchmark host definitivo, ejecutado con CPU libre después de compilar, confirma el mismo trabajo evitado sobre 250 notas y 30 actividades, con cinco calentamientos y treinta repeticiones: **p50 20,78 → 11,30 ms**, **p95 25,94 → 14,68 ms**, asignaciones **25.969.208 → 13.007.904 B**, lecturas **2 → 1**, bytes **1.924.176 → 962.338** y cero peticiones. Compila las fuentes baseline `e364fe4` y las optimizadas con los mismos adaptadores de caché AES-GCM; no representa Android, SecureStorage nativo ni renderizado. Las diferencias de bytes frente al dispositivo corresponden a fixtures diferentes y no se comparan como un mismo experimento.

### SQL, conexiones y volumen

La API ya incorpora telemetría de SQL y conexión. Se extrajeron los últimos 35 eventos por operación tras el lote limpio QA160 y tras la repetición, conservando exclusivamente nombres constantes y valores numéricos. No se guardó una separación inequívoca del log SQL por variante limpia: **no se afirma una comparación SQL baseline/optimizada** a partir de ese log acumulado.

Bootstrap observa cuatro comandos y 128 filas de recomendaciones; p50/p95 total de API **4,74/5,64 ms**, comandos p95 **3,94 ms** y apertura de conexión p95 **0,0049 ms**. Planificación observa 30 comandos y cuatro paradas; p50/p95 total **17,89/20,85 ms**, comandos p95 **11,54 ms** y conexión p95 **0,0108 ms**. La repetición conserva ese trabajo y observa p95 total 20,25 ms. Son datos de un proceso PostgreSQL local ya caliente, sin cambio de SQL; no acreditan conexiones remotas, pool saturado ni velocidad de producción. Los scopes anidados no se suman.

Los `Content-Length` de bootstrap y planificación eran desconocidos, por lo que sus contadores de tamaño son parciales. Una lectura HTTP local independiente, con `Accept-Encoding: identity`, observó **132.491 B** de JSON en bootstrap, **316.624 B** para 250 entradas libres de Journal, **2 B** en notas de actividad (`[]`) y **846 B** en opciones del planificador. Solo se persistieron tamaños y estados 200, sin cuerpos ni tokens. No son tamaños por radio, una comparación de compresión ni una medición de latencia.

PostgreSQL local 17.10: tamaño total **22.828.723 → 23.778.995 B** durante la siembra y sucesivas ejecuciones sintéticas; cuatro conexiones de un límite de 100, cero esperas de bloqueo y cero conexiones `idle in transaction` en la instantánea final. Los tres índices parciales anteriores seguían válidos y listos. Ese crecimiento incluye recuerdos y leases de generación; no demuestra una tasa mensual ni capacidad de producción. No se añadieron índices, cachés, recursos ni configuración del servidor.

**Riesgos de crecimiento:** Journal todavía carga su índice completo, por lo que la lectura sigue creciendo con los recuerdos aunque se haya eliminado la duplicación. Bootstrap mantiene el catálogo completo requerido por offline y sus contratos. Los comandos de planificación y el crecimiento de leases merecen nuevas muestras con cargas mayores antes de decidir otra optimización. Estas observaciones no justifican por sí solas paginar, añadir índices o cambiar infraestructura.

### Arranque frío y trazabilidad

QA160, proceso nuevo, cinco calentamientos y treinta lanzamientos: espera p50/p95 **357/383 ms** y primer dibujo **356/382 ms**. Es una referencia separada; no mide autenticación, bootstrap completo o pantalla utilizable. No se compara causalmente con las series históricas, cuya configuración previa de desbloqueo y empaquetado era distinta. No se usó huella ni se modificaron ajustes globales. Evidencia `cold-start-clean160/clean160-startup-summary.json`.

Raíz de evidencia: `artifacts/roadmap-r12-r14-20261015`, excluida de Git. Los informes numéricos contienen SHA-256 de las APK y manifiestos:

| APK final comparada | SHA-256 del artefacto | SHA-256 del manifiesto de fuentes |
|---|---|---|
| QA156 baseline limpia | `119292E6802E8F7C9E0E1F8DBE29EBC81C03A0D0504B4B61630E8CF2E397B1EF` | `32C32CDCC88938F40EA4850E2412584CC5FEE8694F755F4BD5901B543CBCC98D` |
| QA160 optimizada | `DCA51E1298C3F38F7F7E13B40402C0351D27AE2FFEAB4EC0BC436E9410418C37` | `FE2D20091A5CACF2BEF8C9C7B0917DB76CDC6B028EEE22ABCAD5998DB7641557` |

APK `Yuku-QA156-clean-baseline.apk`/`Yuku-QA160.apk`; fuentes `qa156-clean-baseline-source.json`/`qa160-source.json`. Bajo `r14`: `native-clean-baseline/clean156-*-analysis`, `native-clean-current/clean160-*-analysis`, `native-clean-repeat/clean160-repeat-planning-analysis`, comparaciones `native-clean-comparison-*`; receta baseline `clean-baseline-build-recipe.txt`, override `artifacts/r14-base/{JournalStore.cs,baseline.targets}` e inputs efectivos `compile-inputs.txt`; host `host-final/{baseline,current,comparison,run-manifest}.json`; metadatos `database-{baseline,seeded,clean-final}`, `existing-indexes.json`; tamaños `local-json-payloads.json`; SQL sanitizado `database-operations-clean-{current,repeat}`.

Las carpetas anteriores `native-baseline`, `native-current`, `native-current-repeat` y sus comparaciones se conservan como **históricas, sustituidas por las capturas limpias**. `shared-packaging-mismatch.json` registra el problema encontrado. Los intentos incompletos y APK intermedias tampoco se usan para acreditar el resultado final.

## Estado de validación

**82 pruebas focales** de Journal, compatibilidad, navegación, política QA y diagnóstico sin fallos: `r14/tests/r14-journal-final.trx`. Incluyen una lectura local con semilla de actividades, importación antigua, cancelación/cambio de cuenta durante escritura y relectura de resultados remotos, además de las pruebas existentes de fotos, borradores, conflictos y guardado concurrente. Los tres autoejecutables PowerShell validan análisis nativo, comparabilidad/regresiones y extracción SQL sanitizada. Sus huellas y ejecuciones están en `r14/script-verification.json`. La compilación Android Release baseline limpia/QA160 y las suites completas se registran en la entrega conjunta R12–R14.

Criterios R14-01 a R14-06 contrastados con el recorrido real y la evidencia final. Se completaron los cuatro lotes nativos antes/después y la investigación del aumento inicial de planificación. No se certifican frames, velocidad de producción, iOS ni todas las combinaciones de pantalla a partir de estas pruebas. El arranque frío se registra aparte y no se atribuye a esta optimización de Journal. Las pruebas funcionales de calendario y rutas pertenecen a sus matrices respectivas.

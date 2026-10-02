# Rendimiento de consultas PostgreSQL

Esta entrega reduce consultas repetidas y materialización sin cambiar contratos móviles, permisos, ranking ni infraestructura. La migración es aditiva. El código y los índices se han validado en local. El 2 de octubre de 2026 se aplicó en producción la migración aditiva, después de exportar y restaurar un backup nuevo en una base local aislada (sin propietarios ni ACL del servidor remoto).

## Implementación

- Planificación: permisos equivalentes a `ContentAccessPolicy` en SQL; proyección ligera para ciudad/radio; detalles y paquetes solo de los candidatos seleccionados. Mantiene comparación `OrdinalIgnoreCase`, fallback y orden de empates por identificador que EF aplicaba al incluir paquetes.
- Hoy: todos los candidatos elegibles siguen entrando al ranking. Solo se recuperan los campos de puntuación inicialmente, se reutilizan detalles de reservas y se hidratan las tarjetas mostradas. El viaje se lee sin seguimiento; las nuevas asignaciones se siguen guardando.
- Analítica: consulta de propiedad limitada a viajes presentes en el lote y una consulta de permisos compartida entre eventos. Mantiene consentimiento, deduplicación y fallback al viaje de sesión.
- Retención: una sentencia PostgreSQL atómica por lote de 5.000, ordenado por fecha/identificador y protegido por `FOR UPDATE SKIP LOCKED`. Borrado y agregación mediante `ON CONFLICT` se confirman juntos. Conserva fechas UTC, dimensiones normalizadas y eventos de negocio. Máximo diez lotes por ciclo; siguiente intento al minuto si hay backlog o error, seis horas cuando termina.
- Índices parciales para retención, compras pendientes y transacciones sin confirmar. No se eliminan índices existentes.

## Reproducción

Se necesita el SDK .NET 10, Git y PostgreSQL local. No se instala ninguna herramienta ni se usa la conexión de la aplicación. El ejecutor acepta únicamente localhost, crea un esquema `tc_perf_<identificador>` y lo elimina al terminar. No apuntar a túneles de producción.

```powershell
$env:TRAVELCOMPANION_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres'
./scripts/measure-query-performance.ps1
```

El script extrae cuatro servicios originales del commit `cb44e65f9f84d5cd430daaf7e5381004bbccc49e` a `artifacts/query-baseline`. Los compila junto al ejecutor con nombres separados; no mantiene copias de producción duplicadas en Git. Se puede cambiar `-BaselineRef` para comparar otra revisión compatible. Las pruebas de rendimiento no ejecutan verificadores externos de compras: miden sus consultas de selección reales.

Datos deterministas: 10.000 recomendaciones con textos largos y títulos empatados; 100.000 eventos; 100.000 intenciones y 100.000 transacciones, con 100 pendientes/sin confirmar. La comparación se ejecuta en el mismo proceso, primero baseline y después optimizado, con cinco calentamientos y treinta muestras por escenario. Solo los tres índices nuevos se retiran/reponen en el esquema sintético. Las escrituras de cada muestra se revierten.

`artifacts/query-performance.json` contiene muestras, p50/p95, comandos, filas devueltas/procesadas, bytes asignados por .NET, huellas de resultados y planes `EXPLAIN (ANALYZE, BUFFERS)` del SQL real de EF con sus parámetros. Las huellas verifican identidad y orden en planificación y los DTO completos de Hoy, salvo su fecha de generación. Los planes de compras y retención deben seleccionar sus índices parciales nuevos; el ejecutor falla si no lo hacen. También ejecuta cinco procesos nuevos con pooling desactivado para separar apertura de conexión e inicialización/primera consulta. Ese sondeo frío no representa un arranque completo de la API.

La memoria es asignación total del proceso durante cada muestra, no memoria residente. Las filas del resumen son resultados lógicos; los planes contienen filas recorridas y buffers. Los parámetros se usan únicamente para ejecutar los planes locales y no se serializan en el informe. No ejecutar otras cargas intensivas durante la medición.

## Resultados locales

Medición del 2 de octubre de 2026, .NET 10 Release sobre Windows y PostgreSQL 17 local. Producción utiliza PostgreSQL 16; estos números no son una medición de producción ni de HTTP/red móvil. La comparación usa loggers nulos y no mide el coste del proveedor de logs de producción.

| Escenario | p50 antes/después ms | p95 antes/después ms | Comandos antes/después | MiB asignados antes/después |
|---|---:|---:|---:|---:|
| Planificación por ciudad | 211.01 / 27.76 | 312.42 / 39.59 | 1 / 2 | 93.67 / 15.05 |
| Planificación con fallback | 209.13 / 200.74 | 245.46 / 230.24 | 1 / 2 | 102.74 / 93.38 |
| Hoy, asignaciones nuevas | 191.93 / 95.58 | 253.64 / 134.79 | 9 / 10 | 119.01 / 66.33 |
| Hoy, asignaciones existentes | 130.45 / 39.06 | 183.20 / 85.76 | 8 / 9 | 71.75 / 19.10 |
| Ingesta de 100 eventos | 39.89 / 7.77 | 45.47 / 27.41 | 104 / 5 | 4.69 / 2.62 |
| Retención de 5.000 eventos | 231.63 / 23.31 | 262.54 / 29.15 | 107 / 1 | 33.71 / 0.02 |
| Compras pendientes | 0.78 / 0.71 | 0.91 / 0.81 | 1 / 1 | 0.08 / 0.08 |
| Compras sin confirmar | 7.65 / 0.40 | 11.12 / 0.47 | 1 / 1 | 0.06 / 0.06 |

No hubo regresiones superiores al 10 % en p50/p95. Planificación y Hoy añaden una consulta de detalle a cambio de transferir y materializar menos datos; por eso deben volver a medirse con la latencia real API–PostgreSQL después del despliegue. El fallback conserva todos los candidatos permitidos y obtiene una mejora menor. Los resultados funcionales comparados mantienen las mismas huellas.

- Compras pendientes: buffers compartidos encontrados en caché 208 antes y 51 después; el plan nuevo usa el índice parcial.
- Compras sin confirmar: buffers compartidos encontrados en caché 1725 antes y 26 después; el plan nuevo usa el índice parcial.
- Retención: el plan real selecciona IX_AnalyticsEvents_BehaviorRetention; la sentencia completa se explicó dentro de una transacción descartada.

Los cinco procesos nuevos tardaron 71–81 ms en abrir una conexión sin pooling y 1042–1133 ms en inicializar EF y ejecutar la primera consulta. Este sondeo muestra el coste local de arranque; no demuestra la causa de los picos de producción.

Validación: 403 pruebas de API y 60 de Shared, cero fallos y cero omisiones; las pruebas afectadas se repitieron después de los ajustes finales. API y worker compilan sin advertencias. La migración de ida/vuelta se ejecutó únicamente en esquemas sintéticos.

## Validación funcional

```powershell
dotnet test tests/TravelCompanion.Api.Tests/TravelCompanion.Api.Tests.csproj --verbosity minimal
dotnet test tests/TravelCompanion.Shared.Tests/TravelCompanion.Shared.Tests.csproj --verbosity minimal
dotnet build src/TravelCompanion.Api/TravelCompanion.Api.csproj --verbosity minimal
dotnet build src/TravelCompanion.Notifications.Worker/TravelCompanion.Notifications.Worker.csproj --verbosity minimal
```

Mantener `TRAVELCOMPANION_TEST_POSTGRES` configurada durante estas pruebas. Verificar cero fallos y cero pruebas omitidas; un resultado verde sin PostgreSQL no completa la validación de esta entrega.

Cobertura añadida: 55.001 eventos vencidos y agregados existentes, dos trabajadores, rollback explícito, fallo de restricción durante agregación, cancelación y repetición sin duplicados; preservación de negocio/recientes; 100 eventos con un único lookup de permisos; viajes inválidos; combinaciones de suscripción/paquete/acceso; ciudad sin coincidencias, comparación sin distinguir mayúsculas, empates y radio gratuito; migración de ida/vuelta sin pérdida de filas; aislamiento de telemetría entre peticiones y scopes anidados.

## Publicación y reversión

1. Seguir el backup y comprobación de restauración del runbook. Revisar la migración `20261002142816_OptimizeQueryWorkloads` antes de aplicarla.
2. Generar y revisar el SQL únicamente de esta migración:

   ```powershell
   dotnet ef migrations script 20261002115937_AddLaunchPreparationAndDurableKeys 20261002142816_OptimizeQueryWorkloads --project src/TravelCompanion.Api --output artifacts/query-performance-up.sql
   dotnet ef migrations script 20261002142816_OptimizeQueryWorkloads 20261002115937_AddLaunchPreparationAndDurableKeys --project src/TravelCompanion.Api --output artifacts/query-performance-down.sql
   ```

3. Aplicar mediante el procedimiento de migración existente, dentro de su transacción. La migración fija `lock_timeout=5s` y `statement_timeout=60s` locales a esa transacción. Si expira el tiempo, investigar bloqueos y reprogramar; no subir los límites a ciegas. La creación normal de índices puede bloquear escrituras brevemente; los volúmenes actuales son pequeños. No usar el benchmark contra producción.
4. Publicar la API y revisar health/readiness, bootstrap, Hoy, planificación y compras. Los índices son compatibles con la versión anterior; no cambia ningún DTO ni se requiere actualización móvil.
5. Observar un periodo representativo y al menos un ciclo de retención. Comparar latencias de operaciones con antigüedad del proceso y volumen equivalente. Investigar cualquier regresión reproducible superior al 10 % antes de dar la publicación por validada.
6. Para revertir código, volver al binario anterior; es seguro conservar los índices aditivos. Si también se decide revertir esquema, aplicar el SQL descendente revisado, que solo elimina estos tres índices y revierte la entrada de migración. No restaura eventos ya agregados por la retención normal.

## Interpretación de telemetría

`Database operation` registra nombres fijos (`bootstrap`, `discover`, `today`, `planning`, `analytics.ingest`, `analytics.retention`, `analytics.retention.batch`, `purchases.reconcile`), duración total, comandos, tiempo acumulado de comandos, apertura de conexión, filas y edad del proceso. No incorpora identificadores de usuario, SQL, parámetros ni tokens. El medidor `TravelCompanion.Database` expone la duración con etiqueta `operation`.

El tiempo de comando de EF mide ejecución hasta obtener el lector; no equivale a materialización completa. La duración de la operación también puede incluir CPU o llamadas externas, especialmente reconciliación. Los scopes anidados acumulan comandos en el padre: no sumar padre e hijo como si fueran trabajo independiente. `Rows` representa candidatos, tarjetas o filas del lote según la operación, no siempre filas leídas en PostgreSQL.

Los picos observados en producción de 600–896 ms siguen sin una causa demostrada. La instrumentación permitirá contrastar conexión, arranque y trabajo SQL después de una publicación autorizada. No se ha habilitado `pg_stat_statements`, cambiado recursos, añadido costes ni ejecutado carga sobre producción.

## Preparación de la publicación del 2 de octubre de 2026

La base remota confirma la migración 20261002142816_OptimizeQueryWorkloads y los tres índices válidos. El backup recuperable y sus metadatos permanecen en artifacts, excluidos de Git; contienen datos reales y no forman parte del informe público. La APK instalada en el Android conectado es 1.0 (105), coincide con el cliente vigente y no necesita actualización para estos cambios de backend. La confirmación del commit desplegado y los smoke tests posteriores se registra en la entrega de la sesión.

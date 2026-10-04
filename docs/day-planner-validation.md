# Validación del planificador de días

Fecha: 4 de octubre de 2026. Datos sintéticos exclusivamente. PostgreSQL 17.10 local, .NET 10, Windows.

## Pruebas automatizadas

- API: suite completa con PostgreSQL habilitado, **461 pruebas aprobadas y ninguna omitida**. Incluye 23 casos sobre PostgreSQL real, diez nuevos del planificador; compilación sin advertencias.
- Shared: 63 pruebas aprobadas, incluidas compatibilidad del contrato, fechas, identificadores de selección y preferencias opcionales.
- Móvil: **273 pruebas aprobadas y ninguna omitida**, incluidas 31 nuevas: ocho del almacén, siete del cliente HTTP y 16 del ViewModel de producción. La revisión visual se distingue en la auditoría de pantallas.

El planificador se valida para 1, 3, 5 y 7 días; ritmo tranquilo, equilibrado e intenso; cambio de ciudad; catálogo insuficiente; permisos y radio gratuitos; dieta; ausencia de perfil; muchos planes existentes; selección parcial; cancelación; revisión desactualizada y aislamiento entre cuentas y viajes.

Las diez pruebas específicas sobre PostgreSQL verifican reintentos simultáneos de generación, tres usos gratuitos máximos, dos generaciones pagadas concurrentes y su contador diario, aplicación concurrente de un lote —también cuando todos los lugares ya existen—, revisiones en conflicto, reintento después de eliminar un elemento, rechazo de selecciones inválidas, rollback ante un fallo SQL inducido y fallos de analítica que no revierten datos confirmados. La prueba de migración comprueba `jsonb`, los índices de recibos y que la reversión conserva los planes ya confirmados.

Los reintentos de guardado usan recibos persistidos. Repetir la misma mutación devuelve el resultado anterior; cambiar la selección con esa mutación provoca conflicto. El fallo SQL de prueba no deja reservas parciales, cambios de revisión ni recibos de una aplicación fallida.

Los nuevos casos móviles cubren recuperación de propuesta, selección, preferencias y mutación pendiente tras reinicio; caché antigua; cuentas y viajes separados; cambio de cuenta durante lectura; eliminación y escritura tardía; vinculación de una cuenta anónima, incluso si cambia el viaje; autorización HTTP; errores estructurados, respuestas inválidas y cancelación. El ViewModel de producción se enlaza al proyecto de pruebas; sus casos cubren timeout, identidad de reintentos, fechas y preferencias restauradas, selección parcial, nuevas revisiones, errores de guardado local y cambio de contexto. Las pruebas usan I/O controlado y stubs de dependencias nativas; no verifican renderizado, TalkBack ni el cifrado real del dispositivo.

La validación detectó y permitió corregir una carrera real: una transacción que esperaba el bloqueo del grant/viaje mantenía un snapshot anterior y no veía el lease o recibo recién confirmado. Las operaciones nuevas usan `ReadCommitted` con bloqueo explícito, y las pruebas concurrentes confirman un único resultado, contador y lote. También se comprueba que archivar/despublicar el viaje o eliminar la cuenta impide recuperar resultados persistidos desde una sesión antigua. Los eventos existentes de conversión se registran una vez y respetan consentimiento y exclusión de cuentas internas/demo.

## Medición reproducible

Ejecutar con una base PostgreSQL **local y desechable**. El ejecutor rechaza hosts remotos y crea un esquema aislado que elimina al terminar. No lee la cadena de conexión de la aplicación.

```powershell
$env:TRAVELCOMPANION_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Pooling=true'
dotnet run --project tools/DayPlanPerformance/DayPlanPerformance.csproj --configuration Release -- artifacts/day-plan-performance.json
```

Cada escenario contiene 1.000 recomendaciones, dos ciudades y 100 planes existentes. Se ejecuta una primera llamada, cinco calentamientos y treinta repeticiones; se mantiene el mismo proceso y la misma configuración. Cada repetición utiliza un contexto nuevo y una operación nueva. El tiempo incluye la lectura, la generación y la persistencia del resultado con su consumo de cuota, además del servicio de analítica con consentimiento desactivado. La primera llamada se registra separadamente, pero no representa un arranque frío de proceso para cada escenario. La compilación Android compartía el equipo durante estas mediciones; no hubo otra suite o worker usando PostgreSQL en paralelo.

Resultados con pooling de conexiones activo:

| Días | p50 | p95 | Comandos SQL | Consultas de catálogo | Asignaciones de memoria p50 |
|---|---:|---:|---:|---:|---:|
| 1 | 76,6 ms | 100,1 ms | 29 | 2 | 10,9 MB |
| 3 | 84,6 ms | 103,4 ms | 29 | 2 | 21,7 MB |
| 7 | 66,1 ms | 88,4 ms | 29 | 2 | 41,8 MB |

El mismo ejecutor también se midió con `Pooling=false`, heredado del entorno aislado de las pruebas PostgreSQL:

| Días | p50 sin pooling | p95 sin pooling | Comandos SQL |
|---|---:|---:|---:|
| 1 | 1.355,3 ms | 1.529,7 ms | 29 |
| 3 | 1.385,0 ms | 1.450,7 ms | 29 |
| 7 | 1.349,7 ms | 1.567,7 ms | 29 |

Los dos comandos de catálogo recuperan candidatos y detalles una vez por propuesta. El número de comandos permanece constante al añadir días; el ranking y las asignaciones de memoria crecen con los días procesados. Las asignaciones se estiman mediante `GC.GetTotalAllocatedBytes` y no son memoria retenida. Las diferencias entre duraciones también incluyen el calentamiento progresivo y la variación del equipo: siete días no son intrínsecamente más rápidos que uno. En este runner, desactivar pooling introduce un coste considerable asociado a conexiones nuevas; esto no demuestra el origen de la latencia de producción. No se modificó la configuración de la aplicación o de infraestructura. No se ha establecido una comparación contra una versión anterior equivalente de planificación múltiple, porque ese flujo no existía.

El informe completo, incluidas todas las muestras, primeras llamadas y configuración de pooling, queda en `artifacts/day-plan-performance.json`. La serie sin pooling se conserva en `artifacts/day-plan-performance-no-pooling.json`. Los tiempos locales no se usan como umbrales de CI ni demuestran la latencia de la red o del backend de producción.

## Repetir la validación crítica

```powershell
$env:TRAVELCOMPANION_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Pooling=false'
dotnet test tests/TravelCompanion.Api.Tests/TravelCompanion.Api.Tests.csproj --verbosity minimal --filter 'FullyQualifiedName~DayPlanServiceTests|FullyQualifiedName~DayPlanEndpointTests|FullyQualifiedName~PostgresDayPlanTests'
dotnet test tests/TravelCompanion.Shared.Tests/TravelCompanion.Shared.Tests.csproj --verbosity minimal
dotnet test tests/TravelCompanion.Mobile.Tests/TravelCompanion.Mobile.Tests.csproj --verbosity minimal --filter 'FullyQualifiedName~DayPlannerStoreTests|FullyQualifiedName~DayPlanClientTests|FullyQualifiedName~DayPlannerViewModelTests'
```

Las pruebas PostgreSQL conservan la convención del repositorio y aparecen omitidas si falta la variable. Para una entrega válida debe estar configurada, todas las pruebas específicas PostgreSQL deben ejecutarse y su informe no debe contener omisiones. La validación de esta entrega se ejecutó con la variable configurada. Los logs y resultados TRX están en `artifacts/day-planning-tests/`.

La publicación y reversión se describen en el [procedimiento de publicación del planificador](day-planner-rollout.md). No se ejecutaron cambios en producción durante estas pruebas.

## Revisión del cumplimiento

| Punto | Evidencia y estado |
|---|---|
| Propuesta de 1/3/5/7 días y preferencias unificadas | Implementado; API, contratos y ViewModel probados. Perfil guardado y preferencias temporales se mantienen; gratis conserva tres generaciones y la ventana de los tres primeros días; 5/7 requieren pase. |
| Ciudades, catálogo escaso, reglas de acceso y planes existentes | Implementado; ranking utiliza catálogo autorizado, informa momentos sin cobertura, excluye lugares ya guardados y no introduce un máximo de planes del viaje. Generar no escribe reservas. |
| Revisión, selección y guardado transaccional | Implementado; identidad persistida, reintentos, cambios de revisión, selección parcial, concurrencia, rollback y elementos eliminados verificados. |
| Recuperación local y protección del contexto | Implementado; propuestas, selección, preferencias y operaciones pendientes recuperables; aislamiento, cancelación, cambio de sesión, vinculación y eliminación probados. |
| Compatibilidad y demo | Endpoints y reglas legacy conservados; suite API completa aprobada. Las cuentas demo/internas conservan su exclusión de analítica. El nuevo flujo no reintroduce endpoints antiguos retirados. |
| Migración y publicación posterior | Migración aditiva probada en PostgreSQL local, incluidos índices, `jsonb` y reversión sin borrar planes. SQL preparado en `artifacts/day-planner-up.sql`; orden de publicación, límites de bloqueo y reversión documentados. |
| Medición de 1/3/7 días | Ejecutada con PostgreSQL local, cinco calentamientos y treinta repeticiones. Resultados y límites de comparabilidad descritos arriba. |
| Auditoría completa de pantallas y mejoras prioritarias | Inventario de 33 páginas concretas, dos paneles y estados internos; revisión estática y cambios registrados en la [auditoría móvil](mobile-ux-audit-2026-10.md). No equivale a revisar todas las pantallas en Android. |
| Español, inglés y accesibilidad | Recursos modificados en ES/EN, descripciones semánticas y controles de al menos 48 dp revisados estáticamente. Texto al 150 %, TalkBack, foco y teclado siguen pendientes de verificación en Android. |
| Builds y APK separada | API, worker y Android compilados sin advertencias ni errores; Shared y lógica móvil aprobados. APK separada v115 preparada y metadatos comprobados; detalles debajo. |
| Revisión visual en Android | **Pendiente por bloqueo real: ADB no detecta el teléfono conectado**. No se obtuvieron capturas ni se afirma validación visual en dispositivo. Tampoco se consideran completadas las verificaciones al 150 % y con TalkBack. |
| Push, migración y despliegue en producción | Fuera del alcance de esta fase; no realizados. |

## APK y comprobación HTTP final

La compilación Android final pasó sin advertencias ni errores. La APK de revisión está en `artifacts/mobile/DayPlannerReview-v115.apk` (93.488.416 bytes). `aapt dump badging` confirmó el paquete `com.yuku.travelcompanion.plannerreview`, versión 115 y nombre visible `Yuku Planner`, con ARM64 y x86_64. Su identidad es distinta de la app habitual; el backend configurado es exclusivamente `http://127.0.0.1:5188`.

SHA-256: `F81720F5ED0BBEB9560FB4487429D1591BBD95914183D6AA3BE32C484AE68D8F`.

El backend de revisión arrancó en ese puerto con la base sintética local. `tools/DayPlanReview/Smoke.ps1` comprobó `/health/ready`, autenticación de la cuenta sintética con pase y propuestas de 1/3/5/7 días, con 4/12/20/28 ideas distintas respectivamente. Añadió dos ideas y confirmó que el reintento de la misma mutación devuelve los mismos elementos y revisión. No consumió generaciones de la cuenta gratuita. Evidencia: `artifacts/day-planning-tests/local-api-smoke.log`.

Tres comprobaciones de ADB terminaron con la lista de dispositivos vacía. No se instaló la APK ni se tomaron capturas. La revisión visual, el texto ampliado y TalkBack continúan pendientes; este bloqueo no se presenta como una prueba aprobada.

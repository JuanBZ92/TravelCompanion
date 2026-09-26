# Plan incremental: valor del pase y conversión

Implementación: 26 de septiembre de 2026. Este documento describe el alcance actual y el orden de publicación. No implica que se haya desplegado ni activado el experimento.

## Entregas implementadas

| Parte | Cambio | Cómo comprobarlo |
| --- | --- | --- |
| 1. Medición | Activación al guardar la primera actividad; embudo ordenado; cohortes maduras de 7/30 días; política Free separada de la variante del paywall. | Admin → Conversión. Excluir sandbox, usuarios internos y demo. Los eventos de comportamiento requieren consentimiento. |
| 2. Inicio | Crear viaje sin introducir PIN; ciudad y fechas; hoteles y otras ciudades opcionales; aterrizaje en el primer día. | Crear una cuenta nueva en ES/EN y guardar su primera actividad. El acceso por PIN sigue disponible. |
| 3. Free backend | `TimedTrial` y `PersistentFree`, asignación estable solo a cuentas nuevas compatibles. Tres primeros días editables, tres respuestas de chat y tres mejoras de día independientes. | Probar cada límite, reiniciar sesión y verificar que se conserva la política. Free persistente no caduca ni entra en la limpieza de pruebas temporales. |
| 4. Free mobile | Soporte explícito de la política, edición sin reloj y resumen de cuotas. | No mostrar cuenta atrás ni borrado de borrador al usuario persistente. Las cuentas anteriores conservan su prueba temporal. |
| 5. Paywall | Beneficios contextuales para mapa, asistente, itinerario y descarga offline; cuota tomada de configuración; variante única `contextual-v2`. | Ver precio localizado de la tienda y condiciones reales. Sin precio de tienda no inventar uno. |
| 6. Revisión | Vista de todos los días, conflictos, apertura de las actividades implicadas y enlace a la mejora de día existente. | Comprobar días sin horarios, solapes y traslados ajustados. La revisión explica su cobertura; no verifica cierres ni condiciones en tiempo real. |
| 7. Offline | Preparación explícita de itinerario completo, lugares y documentos curados compatibles; manifiesto por usuario/viaje; estados pendiente, completo y actualización de itinerario. | Descargar, activar modo avión, cambiar de día y abrir archivos. Interrumpir una descarga y comprobar que permanece la copia previa. |
| 8. Documentos | Adjuntos PDF/JPEG/PNG de hasta 20 MiB para Builder de pago, cifrados localmente; abrir, renombrar y eliminar; aislamiento por usuario/viaje. | Verificar permisos, archivo inválido, cuota de tamaño, falta de espacio, visor no instalado y cambio de cuenta. Cerrar sesión conserva adjuntos y limpia vistas previas temporales. |

## Orden de publicación

1. Aplicar primero la migración `20260926003206_AddPersistentFreeAndConversionCohorts` al entorno de prueba. Validar copia de seguridad y despliegue API/worker. La migración mantiene las cuentas existentes en `TimedTrial`.
2. Publicar API con `FreePreview:PersistentFreePercent = 0`. Mantener la configuración de compras vigente; este trabajo no habilita compras de producción.
3. Distribuir el cliente compatible a testers. Validar creación, pago pendiente, compra/activación, restauración, cancelación, revocación y vencimiento en sandbox de Apple/Google. Verificar ES/EN y lectores de pantalla en dispositivos.
4. Completar la prueba de modo avión y documentos: cierre forzado durante descarga, reinicio, archivo de 20 MiB, falta de espacio, cambio de idioma/usuario/viaje y conservación tras logout. Los archivos personales usan un almacenamiento independiente del idioma. No hay dispositivos conectados en el entorno de desarrollo usado para esta implementación.
5. Solo después de esa validación, cambiar el porcentaje a 50 para cuentas nuevas compatibles. Mantener `contextual-v2` fijo para no mezclar dos experimentos. Volver a 0 detiene nuevas asignaciones; no revoca el Free ya asignado.
6. Comparar activación, límites alcanzados, conversión madura a 7/30 días, fallos de checkout/activación y uso posterior al pago. No inferir mejora a partir de cohortes incompletas o muestras pequeñas.

## Límites deliberados

- La preparación offline no descarga mapas, navegación ni servicios en tiempo real. Los enlaces externos se identifican como recursos que requieren conexión. La detección de actualización compara revisión del itinerario y versiones de catálogo/documentos conocidas mediante sincronización. Sin conexión no se pueden descubrir versiones nuevas. La copia anterior de documentos permanece disponible hasta guardar su reemplazo.
- Los adjuntos personales no se suben al servidor ni se restauran desde la nube. Desinstalar la app o perder el dispositivo puede perderlos. Eliminar un adjunto, viaje o cuenta borra las copias correspondientes de la app, no los originales elegidos con el selector. Las operaciones de adjuntar en curso no pueden recrear archivos después de borrar su viaje o cuenta.
- No se reactivan las propuestas, rutas temáticas ni deshacer retirados. La revisión abre el editor y el flujo de mejora de día actual.
- La analítica individual se conserva 90 días. Cohortes históricas fuera de esa ventana no ofrecen reconstrucción completa. Los filtros del embudo seleccionan usuarios con eventos coincidentes en el periodo y recuperan sus etapas completas, incluidos eventos del servidor sin metadatos del cliente. Los ingresos mantienen filtros por transacción.
- No se ha ejecutado una migración en una base de datos remota ni publicado una versión. Para revertir la activación usar el porcentaje, no borrar las políticas ya asignadas. Una reversión de esquema necesita consolidar previamente agregados que difieran solo por política Free.

## Validación de desarrollo

- Compilación Android del código final y worker: sin errores ni advertencias. Suite Mobile: 140 aprobadas; Shared: 44 aprobadas.
- API: suite completa con 335 aprobadas, 4 omitidas y el fallo preexistente descrito abajo. Incluye conservación de política al canjear PIN y al vincular/recuperar por email. Pendiente de validar proveedores reales en sus sandboxes.
- Prueba aislada con el `OfflineCacheService` real y almacenamiento seguro simulado: contenido cifrado en disco, lectura del mismo índice/archivo tras cambiar ES/EN, conservación tras limpiar cachés de sesión y borrado explícito. Esto no sustituye probar Keychain/Keystore y visores nativos.
- El test `TripWorkbookImportServiceTests.Import_creates_trip_user_pin_lodging_and_recommendation_reservations_idempotently` falla también en una extracción limpia de `HEAD` anterior a estos cambios, en su aserción de recomendaciones dentro de reservas. No se modificó para ocultar el fallo.
- EF confirma que el modelo y la nueva migración coinciden. La compilación no sustituye la prueba visual ni la prueba de archivos/visores en dispositivo.

## Revisión iterativa de cierre

La revisión contrasta el código y las pruebas con cada entrega; los pasos de publicación permanecen separados.

- **Pasada 1:** añadido el contexto Offline al paywall, atribución completa de eventos al filtrar el embudo, detección de versiones de catálogo/documentos y limpieza de archivos por viaje/cuenta. Las copias de documentos se conservan durante una actualización fallida.
- **Pasada 2:** corregida la respuesta de sesión al verificar email y seleccionar un viaje Free: conserva política, cuotas y capacidad de edición. Completada la localización de mensajes del formulario. Bloqueada la recreación de adjuntos por lecturas que finalizan después de eliminar su propietario.
- **Pasada 3:** revisados los ocho puntos contra sus servicios, pantallas y pruebas. No quedan funcionalidades de estos puntos deliberadamente aplazadas en código. La migración remota, validación en dispositivos/tiendas y activación del experimento siguen pendientes como pasos de publicación.

| Punto | Evidencia principal de implementación | Verificación |
| --- | --- | --- |
| 1 | `ProductAnalyticsService`, `ConversionModel`, servicio de compras existente | `CommercePlanningTests`: filtros, orden de etapas, cohortes, consentimiento y compras |
| 2 | `LoginViewModel`, `BuilderSetupViewModel`, pantallas y recursos ES/EN | Compilación Android; formulario y teclado requieren dispositivo |
| 3 | `FreePreviewAccountService`, `FreeTrialAccessService`, `AccessGrantPolicy`, `EmailAccountService` | Asignación compatible, cuotas, cleanup, transferencia por email y PIN |
| 4 | `AuthSessionService`, banner en `ScheduleViewModel` | `AuthSessionLogoutTests`; estados persistente, temporal y revocado |
| 5 | `PaywallOfferService`, `PaywallViewModel`, `PaywallPricePresentation` | Beneficios/contextos, límites y presentación de precio |
| 6 | `TripReviewPage`, `TripReviewViewModel`, `ScheduleReviewAnalyzer` | Pruebas Shared de horarios/conflictos; navegación compilada |
| 7 | `OfflineTripPreparationService`, manifiesto y sincronización de versiones | `OfflinePreparationTests`; copias pendientes/interrumpidas y versiones |
| 8 | `TripDocumentStore`, `LocalDocumentPolicy`, `DocsViewModel` | Tamaño/formato, aislamiento, logout, vencimiento, borrado y concurrencia |

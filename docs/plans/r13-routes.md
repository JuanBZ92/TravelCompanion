# R13 — Llegar al próximo compromiso

Fecha: 15 octubre 2026. Alcance: mejorar rutas existentes, sin nuevo proveedor ni infraestructura.

| ID | Criterio y flujo de producción | Comprobación focalizada | Implementación | Automática | Nativa |
|---|---|---|---|---|---|
| R13-01 | Desde A continuación y las reservas se abre una pantalla editorial con destino, horario y zona horaria. | Enlaces de Schedule; compilación Android; revisión visual. | Implementado | Android QA160/iOS C# compilan; enlace revisado en fuente | Android: pantalla ES/EN y detalle de actividad abiertos. iOS pendiente |
| R13-02 | Elegir origen actual, hotel o actividad anterior disponible; modo a pie, transporte o coche y margen de llegada. | Policy/VM y API: origen válido, orden temporal, margen 0–120. | Implementado | Mobile/Shared/API aprobadas en TRX de cierre | Android: opciones hotel/actual, a pie/transporte/coche y margen 10→15 revisados; origen anterior y permiso GPS/cancelación nativos pendientes. iOS pendiente |
| R13-03 | Estimación muestra origen, duración, salida y margen; avisa de horario estimado y salida ya pasada; no garantiza llegada. | VM ES/EN y API. | Implementado | Mobile/API aprobadas en TRX de cierre | Android: destino, horario/zona y fallback ES/EN revisados con proveedor desactivado; resultado positivo del proveedor real no ejecutado. iOS pendiente |
| R13-04 | Proveedor fallido, falta de coordenadas u offline conservan destino/reserva y navegación externa Maps con origen y modo elegidos. | URI/VM, respuestas vacías y fallo. | Implementado | Mobile/API aprobadas; proveedor real no acreditado por fake | Android: proveedor desactivado conserva recuperación; Maps abrió hotel→Tokyo en transporte y volvió. Offline real no acreditado; iOS pendiente |
| R13-05 | Permisos vigentes y propietario/viaje se verifican; cambio de contexto, edición de opciones o cierre cancelan e ignoran respuestas anteriores. | VM cancelación/contexto y endpoint autorización/origen ajeno. | Implementado | Mobile/API aprobadas en TRX de cierre | Cambios de opciones revisados; GPS/cancelación, cambio de cuenta/viaje durante HTTP y UI revocada no ejecutados nativamente. iOS pendiente |
| R13-06 | Nuevos campos opcionales conservan requests antiguos; no se modifican reservas ni se consultan matrices del catálogo. | API request antiguo y suite afectada. | Implementado | Request JSON antiguo y compatibilidad aprobados en API/Mobile | N/A |

Las pruebas nativas pendientes no se acreditarán desde pruebas de lógica. No se cambiarán ajustes globales del teléfono, ni se usará TalkBack o biometría.

## Implementación y compatibilidad

- Viaje → A continuación → **Llegar a tiempo** y el mismo acceso en las reservas con horario abren `RoutePlannerPage`. La reserva visible se pasa directamente, aunque Today sea más reciente que bootstrap.
- El cálculo es explícito: origen actual, hotel/base o actividad anterior cuando existe; a pie, transporte público o coche; margen de 0, 5, 10, 15, 30, 45, 60, 90 o 120 minutos. La ubicación se solicita únicamente al calcular desde el origen actual.
- `ItineraryRoutePolicy` compara instantes UTC usando la zona de cada actividad. Omite ideas sin horario y actividades superpuestas; vuelos que cruzan medianoche mantienen su duración y hoteles bloquean solo el check-in. Horas DST inexistentes o ambiguas no se adivinan. Los empates se resuelven por ID. La preselección SQL de fechas abarca -3/+2 días para incluir los offsets -12/+14 de candidatos en las últimas 24 horas reales; el criterio final es compartido.
- No hay cálculos automáticos sobre el catálogo ni persistencia de coordenadas GPS. No se modifican reservas. El endpoint conserva permisos y limita candidatos previos al viaje del usuario autenticado.
- Cerrar, modificar opciones o cambiar cuenta/viaje/fecha cancela la operación e ignora continuaciones tardías, incluso si la dependencia no atiende la cancelación. Un permiso conocido revocado o expirado invalida el cálculo y el resultado visible. Un HTTP 401/403 se convierte en `AccessDenied` local y bloquea nuevos cálculos en esa pantalla aunque la capacidad cacheada siga siendo positiva; cambiar opciones no elimina ese bloqueo.
- El detalle de la actividad y **Continuar en Maps** permanecen disponibles tras fallo, falta de ubicación u offline. El enlace Maps conserva origen disponible, Place IDs y modo; si no pudo obtenerse GPS, Maps puede pedir el origen. Las direcciones largas se acotan conservando Unicode para respetar el límite de 2.048 caracteres de [Maps URLs](https://developers.google.com/maps/documentation/urls/get-started).

### Contrato aditivo

Se conserva `POST /api/mobile/itinerary/{id}/route`. El request antiguo `{ "mode": "WALK" }` sigue usando el hotel y 10 minutos de margen; el antiguo par `latitude`/`longitude` sigue indicando GPS.

```json
{
  "mode": "TRANSIT",
  "originKind": "PreviousActivity",
  "previousActivityId": "00000000-0000-0000-0000-000000000001",
  "arrivalMarginMinutes": 15
}
```

`originKind` admite `Hotel`, `CurrentLocation` y `PreviousActivity`; el último requiere que el ID corresponda a la actividad anterior calculada en el servidor. `CurrentLocation` requiere latitud y longitud válidas; no se mezclan coordenadas con hotel/actividad previa. El margen API admite enteros 0–120. La respuesta añade `arrivalMarginMinutes` y `previousActivityOverlapsDeparture`, sin eliminar campos ni introducir migraciones.

Los horarios y duraciones son estimaciones. Si la salida estimada cae antes del fin de la actividad anterior se avisa de la incompatibilidad; el usuario conserva el control de Maps y del itinerario.

## Evidencia

- Mobile de cierre: **721/721**, sin fallos ni omitidas, en `artifacts/roadmap-r12-r14-20261015/mobile-results/mobile-closure159.trx`. Incluye **13** pruebas de `RoutePlannerViewModelTests`: request/opciones, fallback/URI, cancelación desde ubicación, respuesta tardía, contexto/permisos, denegación del servidor con capacidad cacheada, servidor antiguo, respuesta JSON inválida, duplicación y direcciones japonesas largas.
- Shared de cierre: **109/109**, sin fallos ni omitidas, en `artifacts/roadmap-r12-r14-20261015/shared-results/shared-closure.trx`. Incluye **3** pruebas de `ItineraryRoutePolicyTests`: orden/empates, UTC/medianoche, check-in y DST.
- API focal de cierre R12/R13/proveedor: **27/27**, sin fallos ni omitidas, en `artifacts/roadmap-r12-r14-20261015/api-results/api-closure.trx`. Incluye **14** casos de `ItineraryRouteEndpointTests`, **4** del servicio Google Routes y **9** del itinerario. Se prueban request JSON antiguo, tres orígenes, validación del margen, permisos gratuitos/vencidos/revocados, cuenta/viaje ajenos, fallo del proveedor, solapamiento y cruce de línea de fecha. No es la suite API completa ni acredita al proveedor real.
- Android Release ARM64 **QA160**: compilación limpia con `RuntimeIdentifier` singular aprobada, **cero errores y 207 advertencias XamlC existentes**; log `artifacts/roadmap-r12-r14-20261015/android160-build.log`, código de salida `android160-build.exit`. Los avisos de bindings `Source` reflejan la configuración deliberada de localización del proyecto.
- iOS ARM64 del cierre: compilación C# aprobada con **cero errores/cero advertencias**, en `artifacts/roadmap-r12-r14-20261015/ios-closure160.log`. No acredita archivo, firma ni ejecución iOS.
- Los TRX focales previos de `artifacts/roadmap-r12-r14-20261015/r13` son antecedentes. QA160 es la APK final compilada y comprobada con el alcance nativo siguiente; no se certifica todo el criterio a partir de esa revisión parcial.

La matriz central conserva sus IDs iniciales: R13-01 agrupa subcriterios 01/02 de esta tabla; R13-02 corresponde a 03/06; R13-03 al 04; R13-04 al 05. La comprobación de cada subcriterio se mantiene explícita aquí para no reducir el recorrido completo a la existencia de un helper.

## Comprobación Android QA160

El coordinador revisó la pantalla de rutas en **ES/EN**, la elección entre hotel y ubicación actual, los modos **a pie/transporte/coche** y el cambio de margen **10→15 minutos**. Con el proveedor de estimaciones desactivado se mostró un estado recuperable; el destino y el detalle de la actividad siguieron disponibles. **Continuar en Maps** abrió la aplicación externa con origen en el hotel, destino en Tokyo y transporte público, y se pudo volver a Yuku. El detalle de la actividad también abrió correctamente.

Capturas: `qa160-route-{es,en}.png`, `qa160-route-fallback-en.png` y `qa160-route-provider-fallback-es.png`, bajo `artifacts/roadmap-r12-r14-20261015/`. La evidencia corresponde a `Yuku-QA160.apk`, con fuentes en `qa160-source.json` y metadatos en `qa160-apk-metadata.txt`; la instalación QA conservó sus datos y fecha inicial del 5 de octubre.

Esta comprobación **no acredita** una ruta positiva del proveedor real, presentación del origen «actividad anterior», permiso GPS y su cancelación, revocación de acceso en la interfaz, cambios de cuenta/viaje durante ubicación/HTTP, fuentes 150/200 % ni ejecución iOS. La lógica correspondiente conserva sus pruebas automáticas. Tampoco se acredita offline real: al quitar el túnel ADB siguió viva una conexión HTTP del pool; se registra como pendiente.

Se conservaron densidad **600**, escala de fuente **1**, modo oscuro del sistema **yes** y rotación **1**; el idioma por aplicación quedó restaurado a `[]`. No se usaron TalkBack ni biometría ni se cambiaron ajustes globales.

## Publicación posterior y reversión

1. Publicar primero API/Shared con los campos opcionales. Los clientes antiguos siguen usando el hotel y margen 10 o el par GPS existente.
2. Comprobar con un viaje sintético propio los tres orígenes disponibles, los modos y distintos márgenes; confirmar que el horario usa la zona del compromiso. El proveedor real y sus horarios no quedan verificados por el fake de los tests.
3. Publicar después el móvil, tras la compilación y QA nativa integrada. Si se conecta a un backend anterior que ignora origen/margen, el cliente descarta la estimación incoherente y mantiene Maps; no presenta la ruta del hotel como si fuera de la actividad anterior.
4. Revisar errores/429 del endpoint y el consumo del proveedor con las herramientas existentes. No se añaden recursos, claves ni infraestructuras para este cambio.
5. Revertir el móvil permite conservar la API nueva compatible. Si se revierte primero la API, el cliente nuevo cae al fallback cuando no puede confirmar origen/modo/margen. No hay migraciones ni datos que borrar.

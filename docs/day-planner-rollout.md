# Publicación del planificador de uno o varios días

## Comportamiento y contratos

El flujo nuevo usa el catálogo autorizado y el perfil guardado. No llama a un modelo remoto ni necesita infraestructura adicional. Las fechas son consecutivas desde la elegida, con duraciones de 1, 3, 5 o 7 días; nunca se recorta silenciosamente un rango que supera el viaje. Las ciudades se resuelven por los segmentos existentes, incluidos días de traslado. La propuesta excluye recomendaciones guardadas en el viaje y evita repetirlas entre sus días.

Los contratos están en `TravelCompanion.Shared/Dtos/DayPlansDtos.cs`:

| Endpoint | Entrada y resultado |
|---|---|
| `GET /api/ai/day-plans/options` | Viaje activo, revisión, fechas completas, duraciones disponibles, perfil, estado gratuito y `CityDays` con fecha/ciudades para mostrar el contexto antes de generar. |
| `POST /api/ai/day-plans` | `TripId`, `ExpectedRevision`, `StartDate`, `DayCount`, `OperationId`, preferencias opcionales y locale. Devuelve días, ciudades, propuestas, momentos sin cobertura y revisión de origen. |
| `POST /api/ai/day-plans/replace` | Operación, viaje, revisión del itinerario, `ExpectedProposalRevision`, `StopId`, `MutationId` y locale. Devuelve `Replaced`, código, mensaje y la propuesta canónica. `OriginalRequest` opcional permite validar las preferencias de una propuesta antigua. |
| `POST /api/ai/day-plans/apply` | Operación de generación, viaje, revisión actual, `MutationId`, IDs seleccionados y locale opcional. Devuelve elementos guardados y revisión resultante. |

Cada propuesta tiene identidad estable y pertenece a una generación, fecha y recomendación. El guardado valida esa selección contra el resultado persistido y el catálogo disponible en ese momento. Añade planes flexibles por momento del día; no altera reservas existentes ni limita el total de planes del usuario. La cantidad de sugerencias por propuesta depende del ritmo y del catálogo; un resultado escaso informa lo que falta.

En «Tus próximos días», las flechas de cada tarjeta buscan otra idea para el mismo momento, conservando las demás tarjetas y la selección del usuario. `ProposalRevision` distingue cambios de la propuesta de cambios del itinerario. Se excluyen las recomendaciones de toda la propuesta, incluso las desmarcadas, el historial de alternativas y los planes guardados. Una tarjeta guardada ya no se reemplaza desde la propuesta. Si no hay alternativa compatible, se mantiene la tarjeta y se informa al usuario; no se generan duplicados para rellenar el resultado.

El formulario muestra fechas, ciudades y cantidad de eventos existentes del rango elegido. La lectura y el cálculo de ciudades usan los mismos segmentos que la generación, incluidos los días de traslado. `DayPlanOptionsDto.CityDays` es aditivo y tiene un valor vacío compatible con respuestas antiguas. Las opciones proyectan únicamente fecha, fecha final y ciudad de las reservas para el fallback; no cargan sus notas ni confirmaciones.

Cada `DayPlanStopDto` incorpora `Place` opcional, obtenido de `Neighborhood` del catálogo ya cargado y, si falta, de la ciudad del día. Los constructores anteriores permanecen intactos; las propuestas locales antiguas muestran la ciudad del grupo. `TravelCardDto.Subtitle` conserva su significado de momento del día. No cambia el ranking ni se añade una consulta de catálogo por tarjeta.

Errores estables: `400 selection`, `403 access/upgrade/quota` y `409 stale/operation`, con `{ code, message }`. Un cambio de revisión exige refrescar el viaje y revisar la selección. Los endpoints antiguos de chat y guardado individual conservan sus contratos; los endpoints retirados de propuestas/rutas no se reactivan.

## Cuota, reintentos y datos

- Gratis: tres generaciones totales, con preferencias en todas, restringidas a los tres primeros días del viaje. Una propuesta de tres días consume una generación. Cinco/siete días requieren pase.
- Con pase: una propuesta completa consume una consulta de la cuota diaria existente, configurada por `StorePurchases:DailyAssistantLimit` —30 por defecto—, que se renueva a las 00:00 UTC.
- El nuevo flujo utiliza `full-day:plan-days:<OperationId>`; no aplica la limitación adicional de una personalización del flujo legacy. Los contadores de mejoras y las cuotas siguen siendo compartidos.
- Reintentar una generación con el mismo UUID y la misma entrada devuelve el resultado canónico y no vuelve a consumir cuota. Cambiar su entrada conservando UUID devuelve `409 operation`. Una generación deliberadamente nueva debe usar otro UUID.
- Reemplazar una tarjeta no consume otra generación ni escribe reservas. Requiere acceso vigente y la revisión actual; gratis conserva la ventana de los tres primeros días. Cada reemplazo tiene mutación idempotente. Una respuesta perdida se recupera con la misma mutación y devuelve el resultado original; recuperar la generación original devuelve la propuesta más reciente. Las alternativas y sus recibos se guardan en el JSON existente del lease, sin migración adicional. Las propuestas nuevas conservan sus preferencias efectivas; una propuesta antigua solo puede reemplazarse si se recupera y valida la solicitud original contra su hash.
- Resultado y consumo se confirman juntos. Fallos y cancelaciones anteriores a esa confirmación no consumen cuota; una propuesta vacía se persiste para replay sin consumir. Una respuesta perdida o una cancelación recibida después de confirmar se recupera reintentando la misma operación, cuyo uso ya quedó registrado.
- El guardado de toda la selección y su recibo se confirman en una sola transacción. Reintentar la misma mutación devuelve el recibo original y no repone elementos que el usuario haya eliminado después. Otra selección necesita otro UUID de mutación. Las colisiones de revisión no guardan parcialmente.
- Las respuestas permanecen vinculadas al propietario por su grant; los recibos tienen propietario y viaje explícitos. Vincular una cuenta anónima conserva estas relaciones. Eliminar cuenta/viaje o purgar un borrador gratuito limpia los resultados y recibos correspondientes, conservando los contadores de negocio cuando corresponde.

Las transacciones nuevas usan `ReadCommitted` con bloqueo explícito del grant para reservar/confirmar cuota y del viaje para validar revisión y aplicar el lote. Así un trabajador que espera ve el recibo o lease confirmado por el anterior, incluso si aquel no cambió la fila del viaje/grant. La API comprueba el grant vigente además del modo de sesión; una sesión antigua con modo incompatible debe seleccionar otra vez el viaje. Los eventos del funnel se registran después de confirmar, con identidad única por generación o primer guardado, y un fallo de analítica no deshace el plan.

La finalización y el replay concurrente revalidan el grant, el propietario no eliminado y el viaje publicado/no archivado dentro de la transacción. Una revocación o despublicación durante la generación impide persistir el resultado y consumir cuota. El replay de un resultado confirmado conserva su identidad aunque después cambie la revisión del itinerario, siempre que el acceso siga vigente. El móvil conserva las operaciones pendientes y la selección al recuperar una respuesta perdida; refrescar por una revisión desactualizada no implica guardar ni generar automáticamente otra propuesta.

El móvil muestra el contexto del rango, los eventos existentes y las preferencias resumidas antes de generar. La propuesta mantiene lugar, momento y selección visibles; los errores se presentan fuera del scroll y reciben foco. La revisión complementaria también prepara listas virtualizadas de Viaje y Documentos para viajes extensos. Consultar los cortes de pruebas, artefactos y límites nativos en la [validación del planificador](day-planner-validation.md) y la auditoría móvil antes de distribuir una recompilación.

Los cambios pendientes de una tarjeta se cifran antes de enviar la petición. No se permite guardar ni generar otra propuesta mientras el resultado de ese reemplazo sea incierto. Las flechas permiten reintentar; un conflicto de revisión recupera el resultado canónico para revisarlo antes de guardar. Un fallo de almacenamiento o conexión conserva las tarjetas. Los controles de duración se muestran en una fila de cuatro opciones. En Viaje, el selector integra fecha y hotel, con días adyacentes y deslizamiento horizontal; el nombre del hotel abre la misma acción de Maps cuando está disponible. Sin hotel se muestra la ciudad y no se inventa una ubicación.

## Migración y orden de publicación

La migración aditiva `20261004202020_AddDayPlanReplayReceipts` añade `RequestHash` y `ResponseJson` opcionales a `AssistantUsageLeases` y crea `PlanningApplicationReceipts`, con clave única `(TripId, MutationId)` e índice `(AppUserId, TripId)`. Los recibos se eliminan en cascada al borrar físicamente el viaje. La migración fija `lock_timeout=5s` y `statement_timeout=60s` dentro de su transacción.

La revisión complementaria del 5 de octubre añade contexto de ciudades/lugares y refuerza la revalidación final; no añade tablas, columnas ni migraciones. Antes de una publicación posterior, consultar el historial real de migraciones del entorno y aplicar únicamente las pendientes. Estas instrucciones son un procedimiento, no una afirmación de despliegue.

1. Verificar commit/artifactos exactos y los resultados locales de API, Shared, móvil y PostgreSQL real. Confirmar backup/restauración y ensayar la migración en staging.
2. Generar y revisar el SQL específico, sin ejecutarlo contra producción durante la revisión:

   ```powershell
   dotnet ef migrations script 20261002171350_AddJournalFreeEntries 20261004202020_AddDayPlanReplayReceipts --project src/TravelCompanion.Api --output artifacts/day-planner-up.sql
   ```

3. Aplicar la migración como paso de publicación separado, desde el entorno autorizado y con el secreto inyectado; usar el procedimiento `--migrate` del [runbook de producción](production-runbook.md#migration-policy). No imprimir la conexión. Si vence el límite de bloqueo, investigar y reprogramar la aplicación.
4. Publicar el backend y worker que pasaron validación; comprobar `/health`, `/health/ready` y las rutas de opciones, generación, reemplazo y aplicación antes de distribuir la APK. El arranque normal de producción mantiene desactivada la migración automática.
5. Preparar y validar una APK de publicación con el identificador habitual, su firma, la URL del backend publicado y una versión superior a la instalada. Las APK separadas de revisión apuntan a localhost y no deben distribuirse como producción. Completar los recorridos nativos pendientes antes de distribuir. El cliente requiere la API nueva; una APK nueva contra el backend anterior no habilita el planificador.

## Comprobación y reversión

En staging, generar 1/3 días con acceso gratuito y 5/7 con pase usando cuentas sintéticas. Comprobar diversidad, días de traslado, catálogo escaso, selección, cancelación, cambios de cuenta/viaje y reintentos de generación/guardado. Generar no debe escribir reservas. Guardar debe aumentar la revisión una sola vez por selección y conservar todos los eventos anteriores.

Ejecutar las pruebas de PostgreSQL real con `TRAVELCOMPANION_TEST_POSTGRES` apuntando exclusivamente a una base local desechable: concurrencia de generación y cuota, replay, concurrencia de aplicación, fallo de escritura con rollback y borrado posterior sin resurrección. Un resultado con estas pruebas omitidas no completa la validación. Consultar el informe local de mediciones; no ejecutar la carga sintética contra producción.

Tras publicación, revisar errores `stale`, `quota` y `operation`, salud del worker y telemetría `day_planning`/`day_plan_replace`/`day_plan_apply`. Las operaciones registran duración, comandos SQL, tiempo de comandos/conexión y cantidad de propuestas o elementos procesados; no registran preferencias ni contenido de documentos. Comparar tiempos de backend caliente y frío por separado.

La reversión preferida consiste en volver al backend/APK anteriores y conservar el esquema aditivo y las reservas creadas. El backend anterior ignora las columnas y la tabla nuevas. Mantener la migración evita perder las identidades de replay si después se vuelve a publicar el planificador.

El `Down` elimina únicamente las columnas de replay y los recibos; no elimina reservas, notas, fotos ni documentos. Solo usarlo tras retirar todos los clientes/servidores dependientes y exportar los resultados/recibos si deben conservarse. No ejecutar `Down` como respuesta automática a un fallo de la interfaz.

Esta entrega prepara código, migración, APK y validación local. Las pruebas o builds locales no demuestran una publicación en producción; registrar por separado cualquier push, aplicación real de migraciones, despliegue y distribución de APK autorizados posteriormente.

## Revisión visual local aislada

`tools/DayPlanReview/Seed.csproj` migra y prepara datos sintéticos exclusivamente en `127.0.0.1:55439/tc_dayplanner_review`. Rechaza otros hosts, puertos, bases y un `SearchPath` personalizado. No lee las conexiones, secretos ni usuarios de producción; tampoco elimina datos de revisiones previas ni reinicia las cuotas al ejecutarse de nuevo.

```powershell
dotnet run --project tools/DayPlanReview/Seed.csproj
```

Si el PostgreSQL local requiere credenciales distintas, inyectar `TRAVELCOMPANION_REVIEW_POSTGRES` con exactamente ese host, puerto y base; no imprimir la cadena. El ejecutor crea 128 recomendaciones legibles, dos cuentas (`planner-free@example.test`, `planner-pass@example.test`), viajes de siete/diez días Tokyo→Kyoto, un compromiso fijo y recuerdos de prueba. La contraseña sintética para autenticación HTTP es `JournalReview2026!`; el pase tiene PIN local `700701`. En Android, la cuenta gratuita entra mediante «Recuperar con email», el código de verificación y «Abrir viaje» en Cuenta; no se asigna un PIN trial personalizado. Con entorno `Development` y SMTP desactivado, el emisor local escribe ese código sintético en el log y no envía correo externo.

Arrancar el backend con esta base y puerto local 5188, sin configuración ni claves de producción. Instalar la APK separada `com.yuku.travelcompanion.plannerreview` y dirigirla al backend local mediante el procedimiento de revisión móvil existente. La app habitual y sus datos quedan separados. La disponibilidad de ADB y cualquier desbloqueo biométrico deben verificarse antes de dar por terminada la prueba visual.

La revisión complementaria utiliza `artifacts/mobile/DayPlannerReview-v116.apk`, compilada con `ApplicationId=com.yuku.travelcompanion.plannerreview`, `ApplicationVersion=116`, `ApplicationTitle=Yuku Planner` y `TravelCompanionApiBaseUrl=http://127.0.0.1:5188`. Su paquete es distinto de la app habitual `com.yuku.travelcompanion.app`, versión 115, que conserva su instalación y datos. Para revisar con USB, arrancar la API local con `ASPNETCORE_URLS=http://127.0.0.1:5188` y `DATABASE_URL` apuntando únicamente a la base sintética. Después comprobar un solo Android autorizado, ejecutar `adb reverse tcp:5188 tcp:5188` e instalar la APK con `adb install -r artifacts/mobile/DayPlannerReview-v116.apk`. No desinstalar la app habitual ni borrar sus datos. En una sesión con varios dispositivos, seleccionar explícitamente el serial autorizado.

La revisión en dispositivo realizada antes de los últimos ajustes fue parcial. Por indicación posterior del usuario, la fase final continúa sin teléfono: compilar la APK no completa la inspección visual de la nueva virtualización y los períodos, ni implica haber instalado esa recompilación. Los recorridos pendientes se conservan para una revisión posterior; no se presentan como aprobados.

`powershell -NoProfile -File tools/DayPlanReview/Smoke.ps1` comprueba por HTTP el rango y el guardado idempotente. Está restringido al backend local 5188 y usa exclusivamente la cuenta sintética con pase. Cada ejecución consume cuatro generaciones pagadas de esa cuenta y añade dos planes sintéticos; no reinicia cuotas ni toca la cuenta gratuita.

Para revisar listas largas, añadir el flag explícito:

```powershell
dotnet run --project tools/DayPlanReview/Seed.csproj -- --large-lists
```

El flag añade cien planes manuales al 20 de octubre del viaje Builder de `planner-pass@example.test`; incrementa su revisión una sola vez si añade elementos. También crea un segundo viaje curado sintético en esa cuenta con cien documentos en «Otros», enlazados al PDF local existente `/demo-documents/guide.pdf` mediante URLs diferenciadas. No crea grants ni amplía permisos: el viaje Builder se abre con el PIN sintético `700701`; el viaje curado se abre mediante salir e iniciar sesión con su PIN sintético `600702`. El selector de cuenta orientado a grants Builder no sustituye este acceso curado por PIN.

Los identificadores externos permiten repetir el flag sin duplicar elementos ni reiniciar usuarios, planes, grants o cuotas. La prueba local comprobó cero adiciones en la repetición y snapshots iguales de grants/cuotas diarias. Se verificaron 140 planes totales —108 el 20 de octubre—, cien documentos por `/api/mobile/docs` y PDF HTTP 200. Estos números reflejan el estado de esa revisión, no un reinicio determinista de datos que borre acciones previas. Los PINs, cuentas y enlaces descritos pertenecen exclusivamente al entorno sintético local. Los resultados de renderizado y métricas Android se registran por separado en la [auditoría móvil](mobile-ux-audit-2026-10.md).

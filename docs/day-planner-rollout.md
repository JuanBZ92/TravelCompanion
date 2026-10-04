# Publicación del planificador de uno o varios días

## Comportamiento y contratos

El flujo nuevo usa el catálogo autorizado y el perfil guardado. No llama a un modelo remoto ni necesita infraestructura adicional. Las fechas son consecutivas desde la elegida, con duraciones de 1, 3, 5 o 7 días; nunca se recorta silenciosamente un rango que supera el viaje. Las ciudades se resuelven por los segmentos existentes, incluidos días de traslado. La propuesta excluye recomendaciones guardadas en el viaje y evita repetirlas entre sus días.

Los contratos están en `TravelCompanion.Shared/Dtos/DayPlansDtos.cs`:

| Endpoint | Entrada y resultado |
|---|---|
| `GET /api/ai/day-plans/options` | Viaje activo, revisión, fechas completas, duraciones disponibles, perfil y estado gratuito. |
| `POST /api/ai/day-plans` | `TripId`, `ExpectedRevision`, `StartDate`, `DayCount`, `OperationId`, preferencias opcionales y locale. Devuelve días, ciudades, propuestas, momentos sin cobertura y revisión de origen. |
| `POST /api/ai/day-plans/apply` | Operación de generación, viaje, revisión actual, `MutationId`, IDs seleccionados y locale opcional. Devuelve elementos guardados y revisión resultante. |

Cada propuesta tiene identidad estable y pertenece a una generación, fecha y recomendación. El guardado valida esa selección contra el resultado persistido y el catálogo disponible en ese momento. Añade planes flexibles por momento del día; no altera reservas existentes ni limita el total de planes del usuario. La cantidad de sugerencias por propuesta depende del ritmo y del catálogo; un resultado escaso informa lo que falta.

Errores estables: `400 selection`, `403 access/upgrade/quota` y `409 stale/operation`, con `{ code, message }`. Un cambio de revisión exige refrescar el viaje y revisar la selección. Los endpoints antiguos de chat y guardado individual conservan sus contratos; los endpoints retirados de propuestas/rutas no se reactivan.

## Cuota, reintentos y datos

- Gratis: tres generaciones totales, con preferencias en todas, restringidas a los tres primeros días del viaje. Una propuesta de tres días consume una generación. Cinco/siete días requieren pase.
- Con pase: una propuesta completa consume una consulta de la cuota diaria existente, configurada por `StorePurchases:DailyAssistantLimit` —30 por defecto—, que se renueva a las 00:00 UTC.
- El nuevo flujo utiliza `full-day:plan-days:<OperationId>`; no aplica la limitación adicional de una personalización del flujo legacy. Los contadores de mejoras y las cuotas siguen siendo compartidos.
- Reintentar una generación con el mismo UUID y la misma entrada devuelve el resultado canónico y no vuelve a consumir cuota. Cambiar su entrada conservando UUID devuelve `409 operation`. Una generación deliberadamente nueva debe usar otro UUID.
- Resultado y consumo se confirman juntos. Fallos y cancelaciones anteriores a esa confirmación no consumen cuota; una propuesta vacía se persiste para replay sin consumir. Una respuesta perdida o una cancelación recibida después de confirmar se recupera reintentando la misma operación, cuyo uso ya quedó registrado.
- El guardado de toda la selección y su recibo se confirman en una sola transacción. Reintentar la misma mutación devuelve el recibo original y no repone elementos que el usuario haya eliminado después. Otra selección necesita otro UUID de mutación. Las colisiones de revisión no guardan parcialmente.
- Las respuestas permanecen vinculadas al propietario por su grant; los recibos tienen propietario y viaje explícitos. Vincular una cuenta anónima conserva estas relaciones. Eliminar cuenta/viaje o purgar un borrador gratuito limpia los resultados y recibos correspondientes, conservando los contadores de negocio cuando corresponde.

Las transacciones nuevas usan `ReadCommitted` con bloqueo explícito del grant para reservar/confirmar cuota y del viaje para validar revisión y aplicar el lote. Así un trabajador que espera ve el recibo o lease confirmado por el anterior, incluso si aquel no cambió la fila del viaje/grant. La API comprueba el grant vigente además del modo de sesión; una sesión antigua con modo incompatible debe seleccionar otra vez el viaje. Los eventos del funnel se registran después de confirmar, con identidad única por generación o primer guardado, y un fallo de analítica no deshace el plan.

## Migración y orden de publicación

La migración aditiva `20261004202020_AddDayPlanReplayReceipts` añade `RequestHash` y `ResponseJson` opcionales a `AssistantUsageLeases` y crea `PlanningApplicationReceipts`, con clave única `(TripId, MutationId)` e índice `(AppUserId, TripId)`. Los recibos se eliminan en cascada al borrar físicamente el viaje. La migración fija `lock_timeout=5s` y `statement_timeout=60s` dentro de su transacción.

1. Verificar commit/artifactos exactos y los resultados locales de API, Shared, móvil y PostgreSQL real. Confirmar backup/restauración y ensayar la migración en staging.
2. Generar y revisar el SQL específico, sin ejecutarlo contra producción durante la revisión:

   ```powershell
   dotnet ef migrations script 20261002171350_AddJournalFreeEntries 20261004202020_AddDayPlanReplayReceipts --project src/TravelCompanion.Api --output artifacts/day-planner-up.sql
   ```

3. Aplicar la migración como paso de publicación separado, desde el entorno autorizado y con el secreto inyectado; usar el procedimiento `--migrate` del [runbook de producción](production-runbook.md#migration-policy). No imprimir la conexión. Si vence el límite de bloqueo, investigar y reprogramar la aplicación.
4. Publicar el backend y worker que pasaron validación; comprobar `/health`, `/health/ready` y las tres rutas nuevas antes de distribuir la APK. El arranque normal de producción mantiene desactivada la migración automática.
5. Distribuir la APK validada. El cliente requiere la API nueva; una APK nueva contra el backend anterior no habilita el planificador.

## Comprobación y reversión

En staging, generar 1/3 días con acceso gratuito y 5/7 con pase usando cuentas sintéticas. Comprobar diversidad, días de traslado, catálogo escaso, selección, cancelación, cambios de cuenta/viaje y reintentos de generación/guardado. Generar no debe escribir reservas. Guardar debe aumentar la revisión una sola vez por selección y conservar todos los eventos anteriores.

Ejecutar las pruebas de PostgreSQL real con `TRAVELCOMPANION_TEST_POSTGRES` apuntando exclusivamente a una base local desechable: concurrencia de generación y cuota, replay, concurrencia de aplicación, fallo de escritura con rollback y borrado posterior sin resurrección. Un resultado con estas pruebas omitidas no completa la validación. Consultar el informe local de mediciones; no ejecutar la carga sintética contra producción.

Tras publicación, revisar errores `stale`, `quota` y `operation`, salud del worker y telemetría `day_planning`/`day_plan_apply`. Las operaciones registran duración, comandos SQL, tiempo de comandos/conexión y cantidad de propuestas o elementos procesados; no registran preferencias ni contenido de documentos. Comparar tiempos de backend caliente y frío por separado.

La reversión preferida consiste en volver al backend/APK anteriores y conservar el esquema aditivo y las reservas creadas. El backend anterior ignora las columnas y la tabla nuevas. Mantener la migración evita perder las identidades de replay si después se vuelve a publicar el planificador.

El `Down` elimina únicamente las columnas de replay y los recibos; no elimina reservas, notas, fotos ni documentos. Solo usarlo tras retirar todos los clientes/servidores dependientes y exportar los resultados/recibos si deben conservarse. No ejecutar `Down` como respuesta automática a un fallo de la interfaz.

Esta entrega prepara código, migración, APK y validación local. Las pruebas o builds locales no demuestran una publicación en producción; registrar por separado cualquier push, aplicación real de migraciones, despliegue y distribución de APK autorizados posteriormente.

## Revisión visual local aislada

`tools/DayPlanReview/Seed.csproj` migra y prepara datos sintéticos exclusivamente en `127.0.0.1:55439/tc_dayplanner_review`. Rechaza otros hosts, puertos, bases y un `SearchPath` personalizado. No lee las conexiones, secretos ni usuarios de producción; tampoco elimina datos de revisiones previas ni reinicia las cuotas al ejecutarse de nuevo.

```powershell
dotnet run --project tools/DayPlanReview/Seed.csproj
```

Si el PostgreSQL local requiere credenciales distintas, inyectar `TRAVELCOMPANION_REVIEW_POSTGRES` con exactamente ese host, puerto y base; no imprimir la cadena. El ejecutor crea 128 recomendaciones legibles, dos cuentas (`planner-free@example.test`, `planner-pass@example.test`), viajes de siete/diez días Tokyo→Kyoto, un compromiso fijo y recuerdos de prueba. La contraseña sintética es `JournalReview2026!`; el pase tiene PIN local `700701`. La cuenta gratuita entra por email/contraseña y selecciona su viaje; no se asigna un PIN trial personalizado.

Arrancar el backend con esta base y puerto local 5188, sin configuración ni claves de producción. Instalar la APK separada `com.yuku.travelcompanion.plannerreview` y dirigirla al backend local mediante el procedimiento de revisión móvil existente. La app habitual y sus datos quedan separados. La disponibilidad de ADB y cualquier desbloqueo biométrico deben verificarse antes de dar por terminada la prueba visual.

La entrega incluye `artifacts/mobile/DayPlannerReview-v115.apk`, compilada con `ApplicationId=com.yuku.travelcompanion.plannerreview`, `ApplicationVersion=115`, `ApplicationTitle=Yuku Planner` y `TravelCompanionApiBaseUrl=http://127.0.0.1:5188`. Para revisar con USB, arrancar la API local con `ASPNETCORE_URLS=http://127.0.0.1:5188` y `DATABASE_URL` apuntando únicamente a la base sintética. Después comprobar un solo Android autorizado, ejecutar `adb reverse tcp:5188 tcp:5188` e instalar la APK con `adb install -r artifacts/mobile/DayPlannerReview-v115.apk`. No desinstalar la app habitual ni borrar sus datos. En una sesión con varios dispositivos, seleccionar explícitamente el serial autorizado.

`powershell -NoProfile -File tools/DayPlanReview/Smoke.ps1` comprueba por HTTP el rango y el guardado idempotente. Está restringido al backend local 5188 y usa exclusivamente la cuenta sintética con pase. Cada ejecución consume cuatro generaciones pagadas de esa cuenta y añade dos planes sintéticos; no reinicia cuotas ni toca la cuenta gratuita.

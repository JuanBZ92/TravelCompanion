# Notifications Worker

`TravelCompanion.Notifications.Worker` es el primer corte del servicio de notificaciones. Vive en la misma solucion, pero esta aislado para poder separarlo luego como microservicio o Azure Container App/WebJob/Function.

## Recordatorios locales de reservas (Android/iOS)

La entrega al teléfono usa notificaciones **locales**, no el emisor de logs del worker. El backend publica `GET /api/notifications/reminders?locale=es|en`, autenticado y permitido para Free. Devuelve exclusivamente el viaje seleccionado que pertenece al usuario, publicado y no archivado. No expone códigos de confirmación, notas ni direcciones. No requiere credenciales FCM/APNs.

- Reservas confirmadas, vuelos y check-in de hospedajes con hora exacta: **180 y 45 minutos antes**. Una reserva a las 19:00 en Tokio avisa a las 16:00 y 18:15 de Tokio.
- Check-in se calcula una vez, con la fecha/hora de inicio del hospedaje. Un hotel guardado sin reserva/horario no permite inferir el check-in. No se programan eventos flexibles por mañana/tarde/noche.
- La zona de la reserva tiene prioridad sobre la del viaje. Zonas desconocidas y horas ambiguas/inexistentes por horario de verano se omiten; no se inventa una hora.
- Se programan los próximos 60 avisos del viaje (margen para el límite de iOS); al abrir/reanudar, cambiar de cuenta/viaje, recuperar conexión o actualizar la agenda se repone la ventana. Guardar/editar/eliminar una reserva espera la sincronización de recordatorios; un fallo de red conserva la última programación conocida. No se garantiza incorporar cambios de otro dispositivo mientras la app permanece cerrada y sin sincronizar.
- IDs estables por reserva/antelación evitan duplicados. Editar reemplaza la programación, borrar/archivar elimina sus avisos tras sincronizar. Salir de la cuenta cancela las alarmas antes de limpiar la sesión.
- Android persiste la programación y la restaura tras reinicio o actualización de APK. Solicita permiso de notificaciones (Android 13+) y ofrece alarmas exactas (Android 12+); sin este último usa alarmas aproximadas del SO. Forzar detención, restricciones del fabricante o revocar permisos pueden impedir/demorar la entrega.
- iOS usa `UNUserNotificationCenter` y presenta banner/sonido también en primer plano. Windows y Mac Catalyst no programan estos avisos en esta versión.
- En **Today → menú del itinerario → Recordatorios de reservas** se pueden solicitar permisos nuevamente o abrir los ajustes del teléfono.

### Validación pendiente en dispositivo

Comprobar Android/iOS con app abierta, cerrada, permiso denegado, reinicio Android, edición y borrado de reserva, logout y cambio de cuenta. En staging crear una reserva con inicio en 45 minutos más dos minutos y comprobar que el aviso llega en dos minutos. La compilación y los tests de horarios no sustituyen esta prueba. Publicar primero el backend con el endpoint y después la APK/app.

## Flujo del worker (legado, emisor de logs)

1. La app mobile registra su device token en `POST /api/notifications/devices`.
2. La API guarda el dispositivo en `NotificationDeviceRegistrations`.
3. El worker escanea reservas futuras.
4. El worker crea recordatorios en `NotificationOutboxItems` usando `DeduplicationKey` para no duplicar.
5. El worker despacha notificaciones vencidas mediante `INotificationSender`.

Por ahora `INotificationSender` usa `LoggingNotificationSender`, o sea, hace dry-run en logs. La integracion real con Azure Notification Hubs, FCM v1 y APNS queda detras de esa interfaz.

## Endpoint Mobile

```http
POST /api/notifications/devices
Authorization: Bearer <session-token>
```

```json
{
  "installationId": "stable-device-installation-id",
  "platform": "fcmv1",
  "pushToken": "device-push-token",
  "locale": "es-ES",
  "timeZoneId": "Asia/Tokyo",
  "scheduleRemindersEnabled": true,
  "recommendationNotificationsEnabled": true
}
```

Platforms aceptadas:

- `fcmv1` o `android`
- `apns` o `ios`

Para desactivar un dispositivo:

```http
DELETE /api/notifications/devices/{installationId}
Authorization: Bearer <session-token>
```

## Worker

Proyecto:

```text
src/TravelCompanion.Notifications.Worker
```

Config principal:

```json
{
  "Notifications": {
    "Enabled": true,
    "PollIntervalSeconds": 60,
    "LookAheadHours": 48,
    "SendBatchSize": 50,
    "StaleNotificationGraceMinutes": 30,
    "ScheduleTimeZoneId": "UTC",
    "ReservationReminderLeadMinutes": [1440, 180, 45]
  }
}
```

Ejecutar local:

```powershell
dotnet run --project .\src\TravelCompanion.Notifications.Worker\TravelCompanion.Notifications.Worker.csproj
```

## Deploy Como WebJob

Para el MVP se despliega como **Continuous WebJob** dentro del mismo App Service Linux de la API. El deploy publica un ZIP combinado que contiene:

- API en la raiz del paquete.
- Worker en `App_Data/jobs/continuous/TravelCompanion.Notifications.Worker`.
- `run.sh` para ejecutar `dotnet TravelCompanion.Notifications.Worker.dll`.
- `settings.job` con `is_singleton=true`.

Script:

```powershell
.\scripts\Publish-NotificationsWorker.ps1
```

Tambien queda incluido por defecto al usar `Publish-Api.ps1`. Si queres desplegar la API sin el WebJob, usa `-SkipNotificationsWorker`.

```powershell
.\scripts\Publish-Api.ps1
.\scripts\Publish-Api.ps1 -SkipNotificationsWorker
```

Importante: `Publish-Api.ps1` usa deploy limpio. Antes de este ajuste, correrlo despues de `Publish-NotificationsWorker.ps1` podia reemplazar el paquete de la API y quitar `App_Data/jobs/...`.

Los scripts leen `resource_group_name`, `api_app_name` y `api_url` desde Terraform si no se pasan por parametro. Tambien configuran:

- `WEBSITE_SKIP_RUNNING_KUDUAGENT=false`
- `Notifications__Enabled=true`
- `always-on=true`

Los scripts deshabilitan `WEBSITE_RUN_FROM_PACKAGE` por defecto cuando incluyen el WebJob, porque en App Service Linux ese modo monta `wwwroot` como read-only y puede impedir que el portal/comandos de WebJobs funcionen correctamente. Si queres forzarlo para un deploy API-only, usa `-EnableRunFromPackage`.

Terraform tambien declara estos app settings para evitar drift si se vuelve a ejecutar `terraform apply`:

- `WEBSITE_SKIP_RUNNING_KUDUAGENT`
- `Notifications__Enabled`
- `Notifications__PollIntervalSeconds`
- `Notifications__LookAheadHours`
- `Notifications__SendBatchSize`
- `Notifications__StaleNotificationGraceMinutes`
- `Notifications__ScheduleTimeZoneId` como fallback. El calculo normal usa `Reservation.TimeZoneId`, luego `Trip.TimeZoneId`, luego `Destination.TimeZoneId`, y recien despues este valor.
- `Notifications__ReservationReminderLeadMinutes__0`, `Notifications__ReservationReminderLeadMinutes__1`, etc.

Parametros utiles:

```powershell
.\scripts\Publish-NotificationsWorker.ps1 `
  -ResourceGroupName "<resource-group>" `
  -AppName "<app-service-name>" `
  -ApiUrl "https://<app>.azurewebsites.net" `
  -TrackDeploymentStatus
```

Si queres que el deploy no reescriba app settings porque los maneja Terraform/CI, usa `-SkipAppSettings`.

Nota: `Always On` requiere un tier Basic, Standard o Premium. Con el tier `F1` el WebJob puede detenerse cuando la app queda idle; es aceptable para demos de bajo costo, pero no para notificaciones confiables.

## Migraciones

La infraestructura agrega:

- `NotificationDeviceRegistrations`
- `NotificationOutboxItems`

El deploy de API debe aplicar migraciones antes de levantar el worker en un ambiente compartido.

## Siguiente Corte

Para enviar push real al celular:

1. Agregar `AzureNotificationHubSender : INotificationSender`.
2. Configurar Azure Notification Hub con FCM v1 y APNS.
3. Implementar registro de token en MAUI Android/iOS.
4. Pedir permisos de notificacion en Android 13+ e iOS.
5. Crear deeplinks para `travelcompanion://schedule/{id}`.
6. Agregar notificaciones de recomendaciones, por ejemplo resumen diario o sugerencias cuando hay huecos grandes en agenda.

Vuelos y check-in con hora exacta también reciben un aviso 24 horas antes; las reservas comunes mantienen solo 180/45 minutos. Los avisos cuyo momento ya pasó no se envían retroactivamente.

Cambios de ciudad: se comparan días consecutivos del itinerario. Si cambia la ciudad, se programa un aviso de preparación a las 09:00 del día anterior en la zona del viaje, sin inferir hora de salida. El DTO usa `reservationId: null`, `tripDayId` y un ID estable `city-change-{dayId}`. Editar/eliminar el cambio lo reemplaza/cancela en la siguiente sincronización.

### Explicit reminder preference

The itinerary editor offers `ReminderEnabled` when an exact start time is selected, independently of flexibility. Opting in enables the existing 180/45-minute reminders (also 1440 minutes for flights/check-in); opting out suppresses them. Period-only items never schedule reminders. The nullable API/database field preserves the legacy confirmed-reservation policy for older clients and existing rows. Deploy the `AddReservationReminderPreference` migration and backend before distributing the updated mobile app. Editing/removing items refreshes the device notification snapshot; notification permissions remain required.

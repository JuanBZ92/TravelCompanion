# Notifications Worker

`TravelCompanion.Notifications.Worker` es el primer corte del servicio de notificaciones. Vive en la misma solucion, pero esta aislado para poder separarlo luego como microservicio o Azure Container App/WebJob/Function.

## Flujo Actual

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
    "ReservationReminderLeadMinutes": [1440, 180]
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

Nota: `Always On` requiere un tier Basic, Standard o Premium. Si el App Service no lo permite, el WebJob puede detenerse cuando la app queda idle.

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

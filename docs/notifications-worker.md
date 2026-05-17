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
    "ScheduleTimeZoneId": "Asia/Tokyo",
    "ReservationReminderLeadMinutes": [1440, 180]
  }
}
```

Ejecutar local:

```powershell
dotnet run --project .\src\TravelCompanion.Notifications.Worker\TravelCompanion.Notifications.Worker.csproj
```

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

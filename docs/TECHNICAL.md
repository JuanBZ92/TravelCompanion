# Travel Companion - Documentacion tecnica

## Estado vigente del lanzamiento — octubre de 2026

El [runbook de lanzamiento](japan-launch.md) documenta email HTTPS, claves Data Protection en PostgreSQL, configuración sin secretos, perfil gratuito separado y migración aditiva de preparación/revisión editorial. Las migraciones de producción se ejecutan mediante `--migrate`; no se aplican durante el arranque normal. API, Shared, lógica móvil, PostgreSQL, Android e iOS ya tienen etapas de validación en Azure Pipelines. Las listas históricas de migraciones y comandos siguientes no sustituyen el estado del código.

Este documento describe como esta construido el proyecto y debe mantenerse actualizado cada vez que se agregue codigo que cambie arquitectura, dependencias, endpoints, persistencia, autenticacion, configuracion local o flujos de desarrollo.

## Stack

- .NET 10
- .NET MAUI 10
- ASP.NET Core 10
- Entity Framework Core 10
- PostgreSQL 16 para desarrollo local
- Docker Desktop con contenedores Linux

## Solucion

- `TravelCompanion.sln`: solucion principal.
- `TravelCompanion.slnLaunch`: perfil compartido para ejecutar varios proyectos con F5.
- `docker-compose.yml`: PostgreSQL local.
- `dotnet-tools.json`: herramientas locales, incluyendo `dotnet-ef`.
- `azure-pipelines.yml`: pipeline Azure DevOps para build de API, validacion Terraform y plan manual.

## Proyectos

- `src/TravelCompanion.Api`: API ASP.NET Core, Razor Pages admin, EF Core y PostgreSQL.
- `src/TravelCompanion.Mobile`: app .NET MAUI para iOS, Android, Windows y otros targets MAUI.
- `src/TravelCompanion.Shared`: DTOs, enums y politicas compartidas entre API y app.
- `tools/TravelCompanion.DevBootstrap`: bootstrap de desarrollo que levanta Docker Compose antes de iniciar API/mobile desde Visual Studio.
- `infra/terraform`: infraestructura Azure declarada con Terraform.

## Infraestructura target

El repositorio incluye una opción Azure declarada con Terraform y procedimientos Render en docs/render-deploy.md. La configuración y el runbook del entorno publicado determinan la infraestructura vigente.

Recursos base:

- Azure Storage Account con container privado para media.
- Azure Key Vault para connection strings y credenciales.
- Application Insights y Log Analytics para observabilidad.

Por control de costos, Terraform usa `allow_paid_resources = false` por defecto. En ese modo no crea App Service ni PostgreSQL Flexible Server. Al cambiarlo a `true`, agrega:

- Azure App Service Linux para `TravelCompanion.Api` y Admin CMS.
- Azure Database for PostgreSQL Flexible Server.
- Secretos productivos en Key Vault.

Log Analytics y Application Insights quedan con cotas bajas de ingesta en dev: `log_analytics_daily_quota_gb = 0.1` y `app_insights_daily_cap_gb = 0.1`.
Tambien se define `app_insights_sampling_percentage = 10` y retention minima operativa (`30` dias) para controlar costo.
La infraestructura agrega budget alerts mensuales por Resource Group con umbrales 50%, 80% y 100%.

El directorio `infra/terraform` contiene:

- `versions.tf`: providers y version minima de Terraform.
- `variables.tf`: parametros configurables por ambiente.
- `main.tf`: recursos Azure.
- `outputs.tf`: valores utiles post-deploy.
- `terraform.tfvars.example`: ejemplo local sin secretos reales.
- `share-mvp.tfvars.example`: ejemplo para levantar el minimo cloud que permite compartir una build mobile sin depender de localhost/USB.

La explicacion practica de Terraform, state y flujo de trabajo esta en `docs/INFRASTRUCTURE.md`.

La primera version mantiene PostgreSQL con endpoint publico y firewall. Para produccion madura, el siguiente hardening sera VNet integration/private endpoint.

Notas operativas del ambiente cloud dev:

- Region principal: `westeurope`.
- Region PostgreSQL dev: configurable con `postgres_location`; puede diferir de la principal si la suscripcion free trial restringe PostgreSQL en una region.
- La password de PostgreSQL no puede contener `;`, porque se embebe en una connection string almacenada en Key Vault.
- La Web App usa Managed Identity para leer secretos de Key Vault.
- El deploy manual de App Service Linux debe usar un ZIP con paths `/`; en Windows evitar `Compress-Archive` para este caso y usar el script de `infra/terraform/README.md`.
- Las migraciones y el seed se ejecutan explícitamente según el runbook; el arranque normal de producción no los aplica.

## Desarrollo local

### Visual Studio F5

1. Abrir Docker Desktop.
2. Abrir `TravelCompanion.sln`.
3. Seleccionar el perfil `Travel Companion Dev`.
4. Elegir target de `TravelCompanion.Mobile`, por ejemplo `Windows Machine` o un emulador Android.
5. Presionar F5.

El perfil ejecuta `TravelCompanion.DevBootstrap`, que levanta PostgreSQL con Docker Compose, luego inicia la API y la app MAUI.

Si no aparece el perfil compartido, habilitar `Tools > Options > Preview Features > Enable Multi-Project Launch Profiles` y reiniciar Visual Studio.

### Manual

```powershell
docker compose up -d
dotnet run --project src\TravelCompanion.Api\TravelCompanion.Api.csproj --launch-profile http
```

La API corre en:

```text
http://localhost:5289
```

## Base de datos

La API usa `TravelCompanionDbContext` con PostgreSQL. En producción las migraciones se aplican con el proceso explícito `--migrate`; `Database:ApplyMigrationsOnStartup` permanece desactivado.

Entidades principales:

- `Destination`: destino vendible, por ejemplo Japon.
- `TravelPackage`: paquete de contenido o suscripcion.
- `Recommendation`: recomendacion geolocalizada.
- `Trip`: viaje contratado o demo.
- `Reservation`: reserva dentro de un viaje (sin nivel de acceso propio), tipada como `Event`, `Flight` o `Lodging`.
- `AppUser`: usuario de la app.
- `AppUserSession`: sesion mobile con token opaco hasheado.
- `UserEntitlement`: acceso concedido a un usuario por compra, paquete, destino o suscripcion.

`Trip` tiene `AppUserId` opcional para asociar viajes/schedules a usuarios creados en el CMS.

`Reservation` guarda `City` para diferenciar reservas de viajes multi-ciudad y habilitar filtros de schedule en mobile/CMS. Tambien guarda campos especificos opcionales para vuelos (`Airline`, `FlightNumber`, origen/destino/aeropuertos) y hospedajes (`EndsOn`, `EndsAt` como check-out o llegada).

`AppUser` guarda `PasswordHash`, `MustChangePassword`, `TemporaryPasswordIssuedAt` y `PasswordChangedAt`. Las passwords se hashean con `PasswordHasher<AppUser>`.

`Recommendation` y `TravelPackage` tienen una relacion muchos-a-muchos mediante `RecommendationTravelPackages`. Si una recomendacion tiene paquetes asociados, el desbloqueo se basa en que el usuario tenga alguno de esos paquetes activos o una suscripcion activa al destino de la recomendacion. Si no tiene paquetes asociados, `Free` queda visible para todos y `Subscription` queda visible solo para usuarios con suscripcion activa al destino; `AdminOnly` queda oculto.

Indices principales orientados a query:

- `Destinations`: `Slug` unico.
- `TravelPackages`: `Slug` unico, compuesto `(DestinationId, Price)`.
- `Recommendations`: compuestos `(DestinationId, Title)` y `(DestinationId, Category, Title)`.
- `Trips`: compuestos `(AppUserId, StartsOn)` y `(DestinationId, StartsOn)`.
- `Reservations`: compuesto `(TripId, Date, StartsAt)` y `(TripId, Type, Date, StartsAt)`.
- `AppUsers`: `Email` unico.
- `AppUserSessions`: `TokenHash` unico y compuesto `(UserId, RevokedAt)`.
- `UserEntitlements`: compuestos `(UserId, ExpiresAt)`, `(TravelPackageId, ExpiresAt)`, `(DestinationId, ExpiresAt)`.

Migraciones iniciales (referencia histórica; listado completo en src/TravelCompanion.Api/Data/Migrations):

- `InitialCreate`
- `AddContentAccessLevels`
- `AddUsersAndEntitlements`
- `AddTripUsers`
- `AddUserPasswordsAndSessions`
- `AddValidationAndQueryIndexes`
- `AddReservationCity`
- `RemoveReservationAccessLevel`
- `AddReservationTypes`

Comandos EF:

```powershell
dotnet tool restore
dotnet dotnet-ef migrations add NombreDeLaMigracion --project src\TravelCompanion.Api\TravelCompanion.Api.csproj --startup-project src\TravelCompanion.Api\TravelCompanion.Api.csproj --output-dir Data\Migrations
dotnet dotnet-ef database update --project src\TravelCompanion.Api\TravelCompanion.Api.csproj --startup-project src\TravelCompanion.Api\TravelCompanion.Api.csproj
```

Si la base local queda en un estado incompatible durante desarrollo:

```powershell
docker compose down -v
docker compose up -d
```

## Seed de desarrollo

`DatabaseSeeder` crea datos demo de Japon si no existen:

- Destino `japon`.
- Paquetes `Japon Essentials` y `Japon Premium Pack`.
- Recomendaciones demo con distintos niveles de acceso. El seeder tambien inserta/actualiza un set ampliado de recomendaciones de Japon aunque la base local ya exista, para probar scroll, filtros, mapa y paginacion.
- Viaje demo con reservas.
- Usuario demo `demo@travelcompanion.local` con entitlement de paquete y suscripcion al destino Japon.
- Usuarios de prueba:
  - `usuariofree@travelcompanion.local` / `PasswordFree`: sin entitlements, solo contenido `Free` desbloqueado.
  - `usuariosub@travelcompanion.local` / `PasswordSub`: entitlement `Subscription`, contenido `Free` y `Subscription` desbloqueado.
  - `usuariopaid@travelcompanion.local` / `PasswordPAid`: entitlement `Paid`, contenido `Free` y `Paid` desbloqueado.
- Cada usuario de prueba tiene un viaje asignado con eventos, vuelos y hospedajes en varias ciudades. Los viajes cubren entre 2 y 3 semanas para validar filtros de schedule, listas largas y scroll mobile.

Tambien normaliza algunos datos demo viejos cuando venian de migraciones anteriores, sin reemplazar contenido general del admin.

## API

Endpoints publicos actuales:

- `GET /api/destinations`
- `GET /api/packages?destinationSlug=japon`
- `GET /api/recommendations?destinationSlug=japon&latitude=35.6762&longitude=139.6503`
- `GET /api/mobile/discover?destinationSlug=japon`
- `GET /api/mobile/bootstrap?destinationSlug=japon`
- `POST /api/auth/login`
- `POST /api/auth/change-password`
- `POST /api/auth/logout`
- `GET /api/me/entitlements`
- `GET /api/me/schedule`
- `GET /api/trips/44444444-4444-4444-4444-444444444401/schedule`
- `GET /api/users/demo/entitlements`
- `GET /api/users/{userId}/entitlements`
- `GET /api/users/{userId}/schedule`

La API serializa enums como strings usando `JsonStringEnumConverter`.

La API tiene response compression habilitada con Brotli/Gzip y `ProblemDetails` registrado como baseline para respuestas de error mas consistentes. Los log levels por defecto son conservadores para reducir ruido/costo en ambientes cloud; desarrollo puede sobreescribirlos desde `appsettings.Development.json`.

Validacion y errores API:

- `ApiController` + DataAnnotations en DTOs de auth (`LoginRequestDto`, `ChangePasswordRequestDto`) para validar formato y longitudes.
- `ApiBehaviorOptions.InvalidModelStateResponseFactory` configurado para devolver `ValidationProblemDetails` uniforme con `traceId`.
- Validaciones manuales puntuales (por ejemplo paginacion y reglas de cambio de password) tambien devuelven `ValidationProblemDetails` mediante helper comun `ApiValidation.ValidationError`.

Observabilidad API:

- `Microsoft.ApplicationInsights.AspNetCore` habilitado con adaptive sampling.
- Middleware `RequestObservabilityMiddleware` para:
  - correlacion (`X-Correlation-ID`);
  - logs de excepciones no manejadas;
  - logs de requests lentas;
  - logs de 4xx/5xx.
- Interceptor EF Core `SlowDbCommandLoggingInterceptor` para:
  - dependencias SQL lentas;
  - errores de dependencias SQL.
- Umbrales configurables en `Observability`:
  - `SlowRequestThresholdMs`;
  - `SlowDependencyThresholdMs`;
  - `CorrelationHeaderName`.
- `GET /api/mobile/discover` agrega header `Server-Timing` con duraciones por fase (`session`, `destination`, `recommendations`, `total`) y un log estructurado con conteos. Se conserva como contrato reducido de catálogo; la antigua tab Ideas fue retirada.
- `GET /api/mobile/bootstrap` agrega header `Server-Timing` con duraciones por fase (`session`, `destination`, `recommendations`, `packages`, `schedule`, `total`) y un log estructurado con conteos. Esto permite separar si una demora mobile viene de autenticacion/sesion, query de recomendaciones, paquetes, schedule o serializacion/respuesta completa.

Los listados `destinations`, `packages` y `recommendations` aceptan paginacion simple:

```text
page=1&pageSize=50
```

`pageSize` debe estar entre `1` y `100`. La respuesta usa `PagedResultDto<T>`:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 50,
  "totalItems": 0,
  "totalPages": 0,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

`GET /api/recommendations` pagina y ordena en base de datos (no en memoria). Cuando recibe `latitude`/`longitude`, ordena por cercania aproximada en SQL y luego calcula la distancia km final solo para los items de la pagina solicitada.

`GET /api/destinations` y `GET /api/recommendations` usan HTTP caching con `ETag` e `If-None-Match`. Destinations permite cache publico; Recommendations usa `Cache-Control: private`, requiere sesion de viaje y el modo promocional consume exclusivamente `/api/mobile/free-map/*`.

`POST /api/auth/login` recibe:

```json
{
  "email": "demo@travelcompanion.local",
  "password": "TravelDemo!2026"
}
```

Y devuelve `AuthSessionDto` con `userId`, `email`, `displayName`, `mustChangePassword` y `token` si las credenciales son validas.

Los endpoints `/api/me/*`, `/api/auth/change-password` y `/api/auth/logout` esperan:

```text
Authorization: Bearer <token>
```

El token completo solo se devuelve a la app. En base de datos se guarda su hash SHA-256.
`LastSeenAt` de `AppUserSession` se actualiza de forma throttled (cada 15 minutos por sesion como maximo) para evitar escrituras en cada request autenticado.

Controles de acceso actuales para datos de usuario/viaje:

- `/api/users/{userId}/entitlements` y `/api/users/{userId}/schedule`: solo el propio usuario autenticado por bearer token o un admin CMS logueado por cookie.
- `/api/trips/{id}/schedule`: solo el usuario dueño del viaje o un admin CMS.
- `/api/users/demo/entitlements`: solo admin CMS (endpoint de soporte/demo).

`GET /api/mobile/bootstrap` es un endpoint autenticado pensado para reducir llamadas iniciales desde mobile. `destinationSlug` es opcional: si no se envia, la API selecciona el primer destino disponible por nombre. Devuelve en una sola respuesta:

- destino seleccionado;
- entitlements activos del usuario;
- recomendaciones del destino filtradas por acceso del usuario;
- paquetes del destino con `isUnlocked`;
- schedule vigente del usuario si existe.

`GET /api/mobile/discover` conserva un contrato autenticado reducido de catálogo. Devuelve solo:

- destino seleccionado;
- recomendaciones del destino ya filtradas por acceso del usuario.

La navegación actual usa el bootstrap compartido para Mapa y Viaje. Los contratos y datos de paquetes siguen incluidos para permisos y consulta offline, aunque no existe una tab Packs.

`GET /api/packages` acepta token bearer opcional. Si recibe una sesion valida, devuelve cada `TravelPackageDto` con:

- `requiredAccessLevel`: `Paid` para paquete pago.
- `isUnlocked`: calculado contra entitlements activos del usuario, destino y paquete.

Sin token, los paquetes se devuelven como no desbloqueados.

## Admin CMS

El admin vive en Razor Pages:

- `/admin`: dashboard.
- `/admin/destinations`: CRUD simple de destinos.
- `/admin/packages`: CRUD simple de paquetes por destino y gestion de usuarios asignados al paquete seleccionado.
- `/admin/recommendations`: CRUD simple de recomendaciones con selector de acceso `Free`, `Suscripcion` o `Paquete`; el selector multi-paquete aparece solo cuando corresponde.
- `/admin/trips`: editor de viaje, borrador y publicación; lista paginada de 50 con búsqueda, filtro y conteos.
- `/admin/reservations`: CRUD de reservas filtradas por viaje.
- `/admin/users`: gestion de usuarios y asignacion/eliminacion de entitlements.
- `/login` y `/logout`: autenticacion por cookie.

`/admin/users` genera passwords temporales al crear/resetear usuarios. En desarrollo, el CMS la muestra en pantalla para poder probar el flujo local. `LoggingUserInvitationSender` solo registra metadata de entrega pendiente y no escribe passwords en logs. En produccion debe reemplazarse por un sender real de email y evitar mostrar secretos en pantalla.

Reglas CMS actuales:

- Los formularios muestran errores de validacion, marcan campos obligatorios con `*` y resaltan inputs invalidos con estado visual propio.
- Los slugs de destinos y paquetes se normalizan a minusculas y reemplazan espacios por guiones.
- No se puede borrar un destino con paquetes, recomendaciones o viajes asociados.
- No se puede borrar un paquete con entitlements de usuario asociados.
- Al activar un paquete desde `/admin/packages`, el entitlement se crea con scope de paquete y destino usando `Paid`.
- La suscripcion no es un paquete: se asigna al destino/pais desde `/admin/users` usando `Subscription`.
- Un paquete es reutilizable: se crea una vez y puede tener muchos usuarios asignados mediante entitlements. La asignacion filtra usuarios que ya tienen acceso activo al paquete para evitar duplicados.
- En `/admin/reservations`, `Ver reservas` filtra el viaje y navega al bloque de reservas con ancla `#reservations-list`.
- No se puede borrar un viaje con reservas asociadas; primero hay que borrar sus reservas.

Credenciales de desarrollo:

```text
Usuario: admin
Password: travel-companion-dev
```

Las credenciales locales estan en `src/TravelCompanion.Api/appsettings.Development.json`.

## Mobile

La navegación principal de Shell contiene Viaje, Mapa, Asistente, Journal y Cuenta. El pase se presenta de forma contextual. Login y desbloqueo son rutas de autenticación; Documentos, Preparación, búsqueda del viaje y resumen de mañana son flujos secundarios.

Las antiguas pantallas `RecommendationsPage`/`PackagesPage`, sus ViewModels y modelos exclusivos fueron retirados tras comprobar que solo se referenciaban entre sí y desde DI. No tenían tab ni ruta registrada. Se conservan `RecommendationDetailPage`, el catálogo completo del bootstrap, los contratos de paquetes, la API y el almacenamiento offline. `MobileDiscoverStore` se conserva porque participa en la invalidación y limpieza de sesión.

### Estado, conexión y sincronización

- `AuthSessionService` guarda metadata de sesión en Preferences y el token en SecureStorage. Captura de usuario, viaje y `ContextVersion` protege operaciones que podrían terminar después de cambiar de contexto.
- `MobileBootstrapStore` publica primero la copia local de catálogo, accesos y agenda; comparte consultas en vuelo y una ventana de frescura en memoria. `MobileTodayStore` mantiene el estado por día. `MobileSyncStateStore` compara versiones y `OfflineSyncCoordinator` coordina actualizaciones y mutaciones pendientes.
- `OfflineCacheService` cifra snapshots y estado local con AES-GCM; la clave del dispositivo permanece en SecureStorage. No se descargan tiles del mapa para usarlo sin conexión.
- `DayPlannerStore` conserva las propuestas del usuario. La planificación permite añadir alternativas o reorganizar los planes elegidos, comparando agenda actual/propuesta y conservando las reservas fijas.
- `JournalStore` conserva identidades independientes para notas de actividades y entradas libres, borradores, portadas y cambios pendientes. Sincroniza notas confirmadas; fotos y borradores permanecen locales. Las listas de Journal cargan miniaturas y usan las fotos completas al abrirlas. Un tombstone recibido por GET o Save conserva las mutaciones pendientes y sus fotos como conflicto hasta la resolución explícita, sin resucitar el registro remoto. Las migraciones canceladas y copias de vinculación fallidas conservan el origen para reintentar.
- `ExpenseStore` mantiene gastos y moneda base localmente, recupera cargas sin desechar contenido y coalesce operaciones de sincronización. Los conflictos de ajustes o cotización mantienen los cambios pendientes. El ACK de presupuesto sólo rebasa el sucesor local de su propia mutación con la misma revisión de origen; no limpia ni cambia su identidad y no rebasa conflictos procedentes de otro dispositivo o de un editor obsoleto. `ExpensePolicy.Breakdown` es el cálculo compartido por API y móvil: excluye eliminados, cuenta cotizaciones ausentes o de otra moneda y usa la moneda configurada.
- `TripDocumentAttachmentService` comparte el selector/validación entre Documentos y Preparación. Admite PDF, JPEG y PNG hasta 20 MB; los archivos y metadatos personales quedan cifrados por cuenta/viaje.
- `TripPreparationOrganizerStore` mantiene documentos y decisiones manuales independientes. La importación de checks antiguos ocurre una sola vez; la petición opcional en segundo plano no bloquea adjuntos ni decisiones locales, ni se repite después de cada edición. La analítica opcional también queda fuera de la espera de la acción local.
- Documentos distingue vacío, fallo de carga, copia conservada y desconexión. Reintentar fuerza la actualización remota cuando hay conexión; un fallo no borra el contenido ya visible. Las versiones permiten reutilizar el catálogo incluido cuando sigue vigente.

El logout marca la sesión cerrada antes de navegar o limpiar servicios, revoca/elimina credenciales y resetea estados visibles. La eliminación de cuenta/viaje limpia sus datos locales y metadatos. El contenido privado de otro usuario no puede reutilizarse como fallback.

### Desbloqueo y permisos

`BiometricUnlockService` usa `Oscore.Maui.Biometric` con los permisos nativos de Android y el texto de Face ID en iOS. Cuenta permite elegir biometría o PIN/código por correo. La preferencia es local por usuario y sobrevive a nuevas sesiones, cambio de viaje y cambio de contraseña; eliminar la cuenta elimina su preferencia.

`LocalUnlockRouting` mantiene la pantalla de verificación al elegir PIN. `BiometricUnlockViewModel` envía esa elección al login existente; nunca abre la agenda por haber deshabilitado biometría. El PIN/código requiere backend y conexión. La biometría puede desbloquear una sesión guardada sin conexión. Se conserva el comportamiento previo de cuentas gratuitas que todavía no han hecho una elección explícita. Un cambio de cuenta o método durante el diálogo nativo invalida su resultado.

Los permisos del pase se resuelven en el backend y se mantienen en el estado de sesión. Adjuntos personales, Preparación, Journal y registro básico de gastos están disponibles con viaje activo; documentos curados, asistente, rutas, descargas offline y reportes conservan sus reglas de acceso. La compra y restauración necesitan verificación del servidor.

### Presentación y diagnóstico

Pages XAML/C# y ViewModels de CommunityToolkit.Mvvm comparten DTOs de `TravelCompanion.Shared`. `EditorialUi`, `Colors.xaml` y `Styles.xaml` centralizan papel cálido, títulos serif, texto, espaciado, bordes y acciones de al menos 48 dp. Los flujos modificados están localizados en español e inglés, con descripciones accesibles, estados de guardado, errores enfocables y reintento.

Viaje, Journal y Documentos conservan listas virtualizadas. Mapa compara el contenido visible antes de reemplazar colecciones; los cambios equivalentes no reconstruyen todos los pins. La búsqueda del viaje trabaja sobre el snapshot local, respeta tipo/ciudad/fechas y evita consultas por cada pulsación. El resumen de mañana deriva primera reserva y hotel de la misma agenda y ofrece sus acciones sin una petición independiente.

`TravelCompanionApiClient` y los stores registran tiempos y caché en diagnóstico. Android usa el tag `TravelCompanion`/`TCMOBILE`. Los errores de transporte nativo de Android envueltos en `WebException` se presentan como problemas de conexión; respuestas HTTP de protocolo conservan su tratamiento propio. Los logs no deben incluir tokens, parámetros privados ni contenido personal.

## Android local

El entorno local esta preparado para ejecutar la app en emulador Android o telefono fisico por USB.

Workloads instalados:

- `android`
- `maui`

SDK y herramientas:

- Android SDK: `C:\Program Files (x86)\Android\android-sdk`
- Java: `C:\Program Files\Android\openjdk\jdk-21.0.8`
- AVD disponible: `pixel_7_-_api_36_0`
- Google USB Driver instalado en el Android SDK.

Variables de usuario configuradas:

- `ANDROID_SDK_ROOT`
- `ANDROID_HOME`
- `JAVA_HOME`

Despues de cambiar estas variables, cerrar y volver a abrir Visual Studio para que detecte el entorno actualizado.

Para usar un telefono fisico:

1. Activar Developer Options en Android.
2. Activar USB debugging.
3. Conectar el telefono por USB.
4. Aceptar el prompt de confianza/RSA en el telefono.
5. En Visual Studio, seleccionar el dispositivo Android en el dropdown de target y presionar F5.

La app mobile resuelve su base URL con esta prioridad:

1. variable de entorno `TRAVELCOMPANION_API_BASE_URL`;
2. fallback seguro por plataforma:
   - Android emulador: `http://10.0.2.2:5289`;
   - Android fisico: `http://127.0.0.1:5289` (via `adb reverse tcp:5289 tcp:5289`);
   - resto: `https://localhost:7090`.

HTTP solo se permite para hosts locales (`localhost`, `127.0.0.1`, `::1`, `10.0.2.2`) para desarrollo local, incluso al iniciar sin debugger desde Visual Studio. Para ambientes remotos/dev/staging/prod, `TRAVELCOMPANION_API_BASE_URL` debe apuntar a HTTPS.

Android ya no usa cleartext global: `usesCleartextTraffic=false` con `network_security_config` que habilita excepciones de HTTP solo para esos hosts de desarrollo.

La URL de la API mobile se resuelve en este orden:

- variable de entorno `TRAVELCOMPANION_API_BASE_URL`, util para Debug desde Visual Studio;
- metadata de build `TravelCompanionApiBaseUrl`, util para builds compartibles;
- default local de desarrollo.

Ejemplo Android apuntando a Azure:

```powershell
dotnet publish src\TravelCompanion.Mobile\TravelCompanion.Mobile.csproj -f net10.0-android -c Release -p:TravelCompanionApiBaseUrl=https://tu-api.azurewebsites.net
```

Script de publicacion mobile:

```powershell
.\scripts\Publish-Mobile.ps1 -Platform Android -InstallAndroid
.\scripts\Publish-Mobile.ps1 -Platform Android -ApiUrl https://app-tc-dev-q352ao.azurewebsites.net -InstallAndroid
```

El script intenta leer `infra/terraform` output `api_url` si no se pasa `-ApiUrl`; si no puede, usa la URL dev publicada. Copia el APK/AAB generado a `artifacts/mobile`.

Para iOS, el publish debe correr en macOS por restricciones de Apple/Xcode:

```powershell
pwsh -File scripts/Publish-Mobile.ps1 -Platform iOS -ApiUrl https://app-tc-dev-q352ao.azurewebsites.net -ArchiveIos
```

Desde Windows se puede disparar en una Mac por SSH si el repo ya existe en esa Mac:

```powershell
.\scripts\Publish-Mobile.ps1 -Platform iOS -MacHost mi-mac.local -MacUser juan -MacRepoPath "/Users/juan/TravelCompanion" -ArchiveIos
```

Para instalar en iPhone real hace falta signing/provisioning de Apple configurado en la Mac. Si no esta configurado globalmente, el script acepta `-CodesignKey` y `-CodesignProvision`.

En Android Debug, `TravelCompanion.Mobile.csproj` define `EmbedAssembliesIntoApk=true`. Esto deshabilita el Fast Deployment de assemblies y evita crashes al abrir la app desde el telefono o con Ctrl+F5 cuando la carpeta temporal `files/.__override__` queda vacia despues de un clean/rebuild. El despliegue inicial puede tardar un poco mas, pero el APK queda autocontenido para desarrollo.

Para verificar desde terminal:

```powershell
adb devices -l
adb reverse --list
```

El dispositivo debe aparecer como `device`. Si aparece `unauthorized`, desbloquear el telefono y aceptar el permiso de depuracion. Si no aparece, probar otro cable USB o instalar el driver OEM del fabricante.

## Mapas

La tab `Mapa` usa el control nativo de .NET MAUI Maps en Android, iOS y MacCatalyst.

En Windows se muestra un fallback con lista cercana porque el control oficial de MAUI Maps no soporta WinUI.

Para ver tiles reales en Android, configurar una Google Maps API key en:

```text
src/TravelCompanion.Mobile/Platforms/Android/Resources/values/google_maps_api.xml
```

## Acceso a contenido

El enum compartido `ContentAccessLevel` diferencia contenido:

- `Free`
- `Paid`
- `Subscription`
- `AdminOnly`

El modelo comercial compartido vive en `ProductAccessModel`. Define labels, descripcion y si cada nivel puede usarse como requisito de contenido o como grant manual a usuarios.

La politica compartida `ContentAccessPolicy` centraliza la evaluacion de acceso. La app mobile consulta los entitlements del usuario logueado y marca recomendaciones como incluidas o bloqueadas. La API tambien usa la misma politica para marcar paquetes como desbloqueados cuando `/api/packages` recibe token bearer.

Los paquetes reutilizables se mapean a grants de usuario mediante `ProductAccessModel.GetPackageGrantLevel`:

- paquete asignado a usuario: `Paid`;
- suscripcion asignada a destino/pais: `Subscription`, sin `TravelPackageId`.

Los entitlements se pueden asignar desde `/admin/users`. Cada entitlement puede tener:

- usuario;
- nivel de acceso;
- destino opcional;
- paquete opcional;
- fecha opcional de expiracion;
- origen, por ejemplo `admin`, `seed-package` o `seed-subscription`.

Regla actual:

- `Free`: visible para todos.
- `Paid`: entitlement de usuario atado a un paquete puntual.
- `Subscription`: entitlement de usuario atado a un destino; desbloquea todos los paquetes de ese destino y recomendaciones exclusivas sin paquete.
- `AdminOnly`: bloqueado para la app publica.

Las reglas estan cubiertas por `tests/TravelCompanion.Shared.Tests/ContentAccessPolicyTests.cs`, y el pipeline ejecuta esos tests ademas del build de API.

La suite inicial de tests queda dividida asi:

- `tests/TravelCompanion.Shared.Tests`: reglas puras compartidas, por ejemplo matriz de acceso y grants de paquetes.
- `tests/TravelCompanion.Api.Tests`: reglas de API/Admin que no necesitan levantar UI, empezando por validacion de inputs opcionales del CMS para evitar `required` implicitos no deseados.
- Futuro `tests/TravelCompanion.Mobile.Tests`: ViewModels y servicios mobile con TFM `net10.0`, evitando referenciar TFMs de plataforma como `net10.0-android` para que xUnit corra en desktop/CI.

## Verificacion

Comando recomendado antes de cerrar cambios:

```powershell
dotnet build TravelCompanion.sln
dotnet test tests\TravelCompanion.Shared.Tests\TravelCompanion.Shared.Tests.csproj
dotnet test tests\TravelCompanion.Api.Tests\TravelCompanion.Api.Tests.csproj
```

Para validar API local:

```powershell
docker compose up -d
dotnet run --project src\TravelCompanion.Api\TravelCompanion.Api.csproj --launch-profile http
```

Probar:

```text
POST http://localhost:5289/api/auth/login
POST http://localhost:5289/api/auth/change-password
http://localhost:5289/api/users/demo/entitlements
http://localhost:5289/api/me/entitlements
http://localhost:5289/api/me/schedule
http://localhost:5289/api/users/66666666-6666-6666-6666-666666666601/schedule
```

Para validar infraestructura Terraform:

```powershell
cd infra\terraform
terraform init
terraform fmt
terraform validate
terraform plan
```

## Azure DevOps

`azure-pipelines.yml` define:

- stage `Build`: instala .NET 10, restaura y compila `TravelCompanion.Api`.
- stage `TerraformValidate`: instala Terraform, ejecuta `terraform fmt -check`, `terraform init -backend=false` y `terraform validate`.
- stage `TerraformPlan`: opcional, solo aparece si el parametro manual `runTerraformPlan` esta en `true`.

El pipeline no ejecuta `terraform apply`. El plan usa `allow_paid_resources=false` para mantener el modo de bajo costo por defecto. Para habilitar el plan en Azure DevOps hay que crear una service connection y reemplazar `TODO-AZURE-SERVICE-CONNECTION`.

## Regla de mantenimiento

Actualizar este documento cuando se cambie cualquiera de estos puntos:

- Arquitectura o estructura de proyectos.
- Dependencias importantes.
- Configuracion de Docker, Visual Studio o launch profiles.
- Migraciones, entidades o reglas de persistencia.
- Endpoints, autenticacion o autorizacion.
- Integraciones mobile, mapas, permisos o servicios compartidos.

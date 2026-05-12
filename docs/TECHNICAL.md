# Travel Companion - Documentacion tecnica

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

## Proyectos

- `src/TravelCompanion.Api`: API ASP.NET Core, Razor Pages admin, EF Core y PostgreSQL.
- `src/TravelCompanion.Mobile`: app .NET MAUI para iOS, Android, Windows y otros targets MAUI.
- `src/TravelCompanion.Shared`: DTOs, enums y politicas compartidas entre API y app.
- `tools/TravelCompanion.DevBootstrap`: bootstrap de desarrollo que levanta Docker Compose antes de iniciar API/mobile desde Visual Studio.

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

La API usa `TravelCompanionDbContext` con PostgreSQL y aplica migraciones automaticamente al iniciar mediante `MigrateAsync`.

Entidades principales:

- `Destination`: destino vendible, por ejemplo Japon.
- `TravelPackage`: paquete de contenido o suscripcion.
- `Recommendation`: recomendacion geolocalizada.
- `Trip`: viaje contratado o demo.
- `Reservation`: reserva dentro de un viaje.
- `AppUser`: usuario de la app.
- `UserEntitlement`: acceso concedido a un usuario por compra, paquete, destino o suscripcion.

Migraciones existentes:

- `InitialCreate`
- `AddContentAccessLevels`
- `AddUsersAndEntitlements`

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
- Paquetes `Japon Essentials` y `Travel Companion Premium`.
- Recomendaciones demo con distintos niveles de acceso.
- Viaje demo con reservas.
- Usuario demo `demo@travelcompanion.local` con entitlements de paquete y suscripcion.

Tambien normaliza algunos datos demo viejos cuando venian de migraciones anteriores, sin reemplazar contenido general del admin.

## API

Endpoints publicos actuales:

- `GET /api/destinations`
- `GET /api/packages?destinationSlug=japon`
- `GET /api/recommendations?destinationSlug=japon&latitude=35.6762&longitude=139.6503`
- `GET /api/trips/44444444-4444-4444-4444-444444444401/schedule`
- `GET /api/users/demo/entitlements`
- `GET /api/users/{userId}/entitlements`

La API serializa enums como strings usando `JsonStringEnumConverter`.

## Admin CMS

El admin vive en Razor Pages:

- `/admin`: dashboard.
- `/admin/recommendations`: CRUD simple de recomendaciones.
- `/admin/reservations`: CRUD simple de reservas del schedule demo.
- `/admin/users`: gestion de usuarios y asignacion/eliminacion de entitlements.
- `/login` y `/logout`: autenticacion por cookie.

Credenciales de desarrollo:

```text
Usuario: admin
Password: travel-companion-dev
```

Las credenciales locales estan en `src/TravelCompanion.Api/appsettings.Development.json`.

## Mobile

La app MAUI usa Shell y tabs:

- Recomendaciones
- Mapa
- Schedule
- Paquetes
- Soporte

Servicios principales:

- `TravelCompanionApiClient`: cliente HTTP hacia la API.
- `FavoritesService`: favoritos locales usando Preferences.

`TravelCompanionApiClient` usa opciones JSON compartidas con `JsonStringEnumConverter` para leer enums serializados como strings por la API.

Patron de UI:

- Pages XAML.
- ViewModels con CommunityToolkit.Mvvm.
- DTOs compartidos desde `TravelCompanion.Shared`.

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

En Android fisico la app usa `http://127.0.0.1:5289` y depende de `adb reverse tcp:5289 tcp:5289` para llegar a la API que corre en la PC. `TravelCompanion.DevBootstrap` configura ese reverse automaticamente para dispositivos Android autorizados antes de iniciar API/mobile desde el perfil `Travel Companion Dev`.

En emulador Android la app usa `http://10.0.2.2:5289`, que es el alias del host desde el emulador.

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
- `Bundle`
- `AdminOnly`

La politica compartida `ContentAccessPolicy` centraliza la evaluacion de acceso. La app mobile consulta los entitlements demo y marca recomendaciones como incluidas o bloqueadas.

Los entitlements se pueden asignar desde `/admin/users`. Cada entitlement puede tener:

- usuario;
- nivel de acceso;
- destino opcional;
- paquete opcional;
- fecha opcional de expiracion;
- origen, por ejemplo `admin`, `seed-package` o `seed-subscription`.

Regla actual:

- `Free`: visible para todos.
- `Paid`: desbloqueado por pago, suscripcion, bundle o acceso a destino/paquete.
- `Subscription`: desbloqueado por suscripcion.
- `Bundle`: desbloqueado por bundle o acceso a destino/paquete.
- `AdminOnly`: bloqueado para la app publica.

## Verificacion

Comando recomendado antes de cerrar cambios:

```powershell
dotnet build TravelCompanion.sln
```

Para validar API local:

```powershell
docker compose up -d
dotnet run --project src\TravelCompanion.Api\TravelCompanion.Api.csproj --launch-profile http
```

Probar:

```text
http://localhost:5289/api/users/demo/entitlements
```

## Regla de mantenimiento

Actualizar este documento cuando se cambie cualquiera de estos puntos:

- Arquitectura o estructura de proyectos.
- Dependencias importantes.
- Configuracion de Docker, Visual Studio o launch profiles.
- Migraciones, entidades o reglas de persistencia.
- Endpoints, autenticacion o autorizacion.
- Integraciones mobile, mapas, permisos o servicios compartidos.

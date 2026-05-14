# Travel Companion

App companion de viajes con cliente movil .NET MAUI, API ASP.NET Core y PostgreSQL.

## Documentacion

- [Documentacion tecnica](docs/TECHNICAL.md): arquitectura, stack, desarrollo local, API, base de datos, migraciones y verificaciones.
- [Documentacion funcional](docs/FUNCTIONAL.md): vision de producto, usuarios, pantallas, reglas de acceso, CMS y roadmap.
- [Infraestructura y Terraform](docs/INFRASTRUCTURE.md): explicacion practica de Terraform, state, recursos Azure y flujo de trabajo.
- [Terraform Azure](infra/terraform/README.md): infraestructura cloud para App Service, PostgreSQL, Storage, Key Vault y observabilidad.

## Regla del proyecto

Cada vez que se agregue codigo nuevo, se debe actualizar la documentacion correspondiente:

- Cambios de arquitectura, endpoints, base de datos, dependencias, setup o integraciones: actualizar `docs/TECHNICAL.md`.
- Cambios de comportamiento visible, pantallas, reglas de negocio, contenido, CMS o roadmap: actualizar `docs/FUNCTIONAL.md`.

## Stack actual

- .NET 10
- .NET MAUI 10
- ASP.NET Core 10
- Entity Framework Core 10
- PostgreSQL 16 para desarrollo local

## Desarrollo rapido

### Visual Studio F5

1. Abrir Docker Desktop.
2. Abrir `TravelCompanion.sln` en Visual Studio.
3. Seleccionar el perfil compartido `Travel Companion Dev`.
4. Elegir el target de `TravelCompanion.Mobile`.
5. Presionar F5.

### Manual

```powershell
docker compose up -d
dotnet run --project src\TravelCompanion.Api\TravelCompanion.Api.csproj --launch-profile http
```

API local:

```text
http://localhost:5289
```

Admin local:

```text
http://localhost:5289/admin
Usuario: admin
Password: travel-companion-dev
```

Login mobile demo:

```text
Usuario: demo@travelcompanion.local
Password temporal: TravelDemo!2026
```

Base URL mobile:

```text
TRAVELCOMPANION_API_BASE_URL=https://localhost:7090
```

Si no se define la variable, MAUI usa fallback local para desarrollo (`http://127.0.0.1:5289` en Android fisico con `adb reverse`, `http://10.0.2.2:5289` en Android emulador, `https://localhost:7090` en Windows). En `Release` no se permite HTTP.

Luego de cambiar la password temporal, la app puede desbloquear la sesion con biometria del dispositivo y fallback a password.
Usar `Bloquear app` mantiene la sesion para biometria; `Cerrar sesion` borra la sesion y requiere password.

## Verificacion

```powershell
dotnet build TravelCompanion.sln
```

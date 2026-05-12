# Travel Companion

App companion de viajes con cliente movil .NET MAUI, API ASP.NET Core y PostgreSQL.

## Documentacion

- [Documentacion tecnica](docs/TECHNICAL.md): arquitectura, stack, desarrollo local, API, base de datos, migraciones y verificaciones.
- [Documentacion funcional](docs/FUNCTIONAL.md): vision de producto, usuarios, pantallas, reglas de acceso, CMS y roadmap.

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

## Verificacion

```powershell
dotnet build TravelCompanion.sln
```

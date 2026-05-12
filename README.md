# Travel Companion

App companion de viajes con cliente movil .NET MAUI, API ASP.NET Core y PostgreSQL.

## Stack actual

- .NET 10
- .NET MAUI 10
- ASP.NET Core 10
- Entity Framework Core 10
- PostgreSQL 16 para desarrollo local

## Estructura

- `src/TravelCompanion.Api`: API REST con endpoints iniciales para destinos, paquetes, recomendaciones y schedule.
- `src/TravelCompanion.Shared`: DTOs compartidos entre API y app movil.
- `docker-compose.yml`: PostgreSQL local para desarrollo.

## Desarrollo local

### Opcion recomendada: Visual Studio F5

1. Abre Docker Desktop.
2. Abre `TravelCompanion.sln` en Visual Studio.
3. En la solucion, selecciona el perfil compartido `Travel Companion Dev`.
4. En `TravelCompanion.Mobile`, elige el target que quieras usar, por ejemplo `Windows Machine` o un emulador Android.
5. Presiona F5.

El perfil ejecuta `TravelCompanion.DevBootstrap`, que levanta PostgreSQL con Docker Compose, luego inicia la API y la app MAUI.

Si no ves el perfil, habilita `Tools > Options > Preview Features > Enable Multi-Project Launch Profiles` y reinicia Visual Studio.

### Manual

1. Levantar PostgreSQL:

   ```powershell
   docker compose up -d
   ```

2. Ejecutar la API:

   ```powershell
   dotnet run --project src\TravelCompanion.Api\TravelCompanion.Api.csproj
   ```

3. Probar endpoints:

   - `GET /api/destinations`
   - `GET /api/packages?destinationSlug=japon`
   - `GET /api/recommendations?destinationSlug=japon&latitude=35.6762&longitude=139.6503`
   - `GET /api/trips/44444444-4444-4444-4444-444444444401/schedule`

La API crea la base con `EnsureCreated` y carga datos demo de Japon al iniciar. Para produccion conviene cambiar esto por migraciones EF Core.

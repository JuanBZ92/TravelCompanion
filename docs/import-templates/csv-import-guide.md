# CSV Import Guide

Estos archivos son plantillas para cargar informacion curada de Japon en TravelCompanion. Estan pensados como contrato inicial para un futuro importador desde Admin CMS o un endpoint backend.

Archivos:

- `recommendations-template.csv`: experiencias, lugares, rutas, comidas, actividades y recomendaciones que aparecen en Discover, Details y Assistant.
- `schedules-template.csv`: vuelos, hospedajes y eventos de un viaje de usuario; alimenta Schedule y el contexto que usa el Assistant para proponer planes.

## Reglas Generales

- Usar UTF-8.
- Mantener la primera fila exacta: son los nombres de columnas.
- Separador de columnas: coma.
- Si un campo contiene coma, envolverlo entre comillas dobles.
- Fechas en formato `YYYY-MM-DD`.
- Horas en formato `HH:mm` de 24 horas.
- Decimales con punto, por ejemplo `35.665486`.
- Listas dentro de una celda con punto y coma, por ejemplo `food;local food;snacks`.
- Evitar filas parcialmente duplicadas. Usar `external_id` estable para que el importador pueda actualizar sin duplicar.

## Recommendations

Cada fila representa una recomendacion curada. En el sistema actual corresponde principalmente a `Recommendation`.

| Columna | Obligatoria | Ejemplo | Como se usa |
| --- | --- | --- | --- |
| `external_id` | Si | `rec-tokyo-tsukiji-snack-walk` | Identificador estable externo guardado en DB. Permite que el importador actualice sin duplicar. |
| `destination_slug` | Si | `japan` | Busca el destino (`Destination.Slug`). Para Japon usar `japan` si ese es el slug cargado. |
| `title` | Si | `Tsukiji snack walk` | Titulo visible en Discover, Details y cards del Assistant. Maximo recomendado: 160 caracteres. |
| `category` | Si | `Food` | Categoria principal. Tambien funciona como tag fallback. Valores recomendados: `Food`, `Culture`, `Nature`, `Shopping`, `Viewpoint`, `Nightlife`, `Neighborhood`, `Wellness`, `Transport`. |
| `neighborhood` | Si | `Tsukiji, Tokyo` | Zona o barrio visible y usado para contexto geografico. |
| `description` | Si | `Paseo compacto...` | Descripcion visible. Tambien ayuda al ranking deterministico y filtros por texto. |
| `tags` | Recomendado | `food;local food;snacks;market` | Tags visibles y accionables. El usuario puede evitar tags como `culture`, `shopping`, `onsen`. Usar canonicos en ingles cuando sea posible. |
| `price_level` | Si | `low` | Presupuesto estructurado. Valores: `free`, `low`, `medium`, `high`. El Assistant usa esto para pedidos como "coste bajo" o "premium". No es lo mismo que tag. |
| `latitude` | Si | `35.665486` | Latitud para mapa, distancia y ranking por cercania. |
| `longitude` | Si | `139.770667` | Longitud para mapa, distancia y ranking por cercania. |
| `suggested_duration_minutes` | Si | `90` | Duracion sugerida. El Assistant la usa para encajar planes entre reservas. |
| `rating` | No | `4.5` | Senal de calidad. Rango recomendado: `0` a `5`. |
| `opening_hours` | Recomendado | `09:00-14:00` | Horario de apertura. El ranking penaliza opciones cerradas si hay ventana de agenda. Para rangos nocturnos se acepta `18:00-02:00`. |
| `access_level` | Si | `Free` | Nivel de acceso. Valores usados por admin: `Free`, `Paid`, `Subscription`. Para contenido vendido por paquete, usar `Paid` y completar `package_slugs`. |
| `package_slugs` | No | `japon-premium-pack` | Paquetes asociados, separados por `;`. Corresponde a `TravelPackage.Slug`. |
| `source_name` | No | `Curador Japon 2026` | Origen editorial guardado en DB para auditoria interna. |
| `source_url` | No | `https://...` | URL de referencia guardada en DB si existe. |
| `curation_notes` | No | `Evitar mediodia...` | Notas internas guardadas en DB para revisar calidad, timing o warnings. No se exponen en la app mobile. |

### Tags Recomendados

Usar tags simples, en ingles y minusculas. Algunos ya tienen alias en el sistema:

- Intereses/categorias: `food`, `culture`, `nature`, `shopping`, `nightlife`, `viewpoint`, `neighborhood`, `wellness`.
- Comida: `local food`, `snacks`, `cafe`, `vegetarian`, `vegan`, `market`, `ramen`, `sushi`, `sake`.
- Cultura: `museum`, `history`, `art`, `temple`, `shrine`, `garden`.
- Ritmo/contexto: `hidden gem`, `rainy day`, `family friendly`, `romantic`, `premium`, `free`.

Importante: restricciones como "evitar culture" funcionan sobre `tags` y `category`. Pedidos de presupuesto como "coste bajo" funcionan sobre `price_level`.

## Schedules

Cada fila representa un item de agenda dentro de un viaje. En el sistema actual corresponde a `Reservation` dentro de un `Trip`.

| Columna | Obligatoria | Ejemplo | Como se usa |
| --- | --- | --- | --- |
| `external_id` | Si | `sch-demo-flight-out` | Identificador estable externo guardado en DB para actualizar sin duplicar. |
| `user_email` | Si | `demo@example.com` | Usuario dueño del viaje. El importador deberia resolver `AppUser.Email`. |
| `trip_external_id` | Si | `japan-demo-2026` | Identificador estable del viaje guardado como `Trip.ExternalId`. Agrupa schedules en un mismo `Trip`. |
| `destination_slug` | Si | `japan` | Destino del viaje. Resuelve `Destination.Slug`. |
| `traveler_name` | Si | `Demo Traveler` | Nombre visible del viajero en el viaje. |
| `trip_starts_on` | Si | `2026-10-06` | Inicio del viaje. Debe cubrir las fechas de sus schedule items. |
| `trip_ends_on` | Si | `2026-10-12` | Fin del viaje. |
| `type` | Si | `Flight` | Tipo de item. Valores exactos: `Event`, `Flight`, `Lodging`. |
| `date` | Si | `2026-10-07` | Fecha de inicio del item. |
| `starts_at` | Si | `18:30` | Hora de inicio. |
| `ends_on` | No | `2026-10-07` | Fecha de fin si aplica. Para hospedaje o vuelos, completarla. |
| `ends_at` | No | `20:00` | Hora de fin si aplica. |
| `title` | Si | `Cena en Ginza` | Titulo visible en Schedule y Assistant. Maximo actual recomendado: 160 caracteres. |
| `city` | Si | `Tokyo` | Ciudad usada para filtros y para contexto del Assistant. |
| `location_name` | Recomendado | `Ginza dinner spot` | Lugar principal. En el admin hoy se normaliza a texto vacio si falta, pero conviene completarlo. |
| `address` | Recomendado | `Ginza, Chuo City, Tokyo` | Direccion visible. |
| `confirmation_code` | No | `DIN789` | Codigo de reserva. Puede quedar vacio si no aplica. |
| `notes` | No | `Reservar counter...` | Notas visibles/internas del item. |
| `airline` | Solo vuelos | `Iberia` | Aerolinea. |
| `flight_number` | Solo vuelos | `IB281` | Numero de vuelo. |
| `origin_name` | Solo vuelos | `Madrid` | Origen legible. |
| `destination_name` | Solo vuelos | `Tokyo` | Destino legible. |
| `origin_airport` | Solo vuelos | `MAD` | Codigo o nombre de aeropuerto de salida. |
| `destination_airport` | Solo vuelos | `HND` | Codigo o nombre de aeropuerto de llegada. |
| `source_name` | No | `Curador Japon 2026` | Origen de la informacion guardado en DB para auditoria. |
| `source_url` | No | `https://...` | Referencia guardada en DB si existe. |

### Validaciones Recomendadas Para El Importador

- `destination_slug` debe existir.
- `user_email` debe existir antes de importar schedules.
- `trip_external_id` + `user_email` deberia resolver un unico viaje.
- `date` debe estar entre `trip_starts_on` y `trip_ends_on`.
- Si `type = Flight`, conviene exigir `origin_airport`, `destination_airport`, `airline` y `flight_number`.
- Si `type = Lodging`, conviene exigir `ends_on`.
- Si una fila trae `ends_at` sin `ends_on`, se interpreta como mismo dia.
- No cargar recomendaciones sin latitud/longitud: rompe la calidad de mapa, distancia y ranking.
- No cargar recomendaciones sin `suggested_duration_minutes`: el Assistant necesita duracion para encajar planes.

## Siguiente Paso Tecnico

Para convertir estas plantillas en funcionalidad, conviene implementar primero un importador backend con modo `dryRun`:

1. `POST /admin/import/recommendations/csv` para admins.
2. `POST /admin/import/schedules/csv` para admins.
3. Parsear CSV, validar filas y devolver preview con errores por fila.
4. Resolver `destination_slug`, `package_slugs`, `user_email` y `trip_external_id`.
5. Importar por `external_id`/`trip_external_id` para crear o actualizar sin duplicados.
6. Registrar resumen: creados, actualizados, omitidos, errores.

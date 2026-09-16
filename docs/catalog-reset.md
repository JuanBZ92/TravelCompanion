# Catalog reset and bilingual import

`TravelCompanion.CatalogAdmin` is the maintenance-only entry point for replacing the YUKU catalog. It is intentionally not exposed as a public API endpoint.

## Preview

Set `CATALOG_DATABASE_URL` to the intended PostgreSQL database and run:

```powershell
dotnet run --project tools/TravelCompanion.CatalogAdmin -- "C:\path\Base de datos YUKU Japan - FINAL bilingue.xlsx"
```

The tool applies pending migrations and ensures only the minimum technical records needed by the catalog. It then validates the workbook without changing recommendations or traveler data.

## Reset and import

Use a new backup path. The tool refuses to overwrite an existing archive or run the reset without the explicit confirmation flag.

```powershell
dotnet run --project tools/TravelCompanion.CatalogAdmin -- `
  "C:\path\Base de datos YUKU Japan - FINAL bilingue.xlsx" `
  --reset `
  --backup "C:\backups\travelcompanion-before-catalog-reset.dump" `
  --confirm-delete-test-data
```

The sequence is: validate workbook, create a PostgreSQL custom-format backup, verify that archive, then reset and import inside a serializable transaction. A failed import rolls back the database transaction.

The reset keeps technical destination and free-map configuration, replaces the recommendation catalog, removes test traveler content, revokes old sessions, and creates the three demonstration access modes:

- `0000`: Free map preview.
- `1111`: Pago, ready to create a new itinerary.
- `2222`: Premium, published three-day Tokyo example.

## Restore

Restore into a fresh database first, verify it, and only then decide whether to replace a production database:

```powershell
createdb --host <host> --username <user> restored_validation
pg_restore --host <host> --username <user> --dbname restored_validation --exit-on-error "C:\backups\travelcompanion-before-catalog-reset.dump"
```

Never commit connection strings, database passwords, workbook contents, or backup files. Render-hosted API services must use the database internal connection string; local maintenance uses the external string with TLS and an IP allow-list.

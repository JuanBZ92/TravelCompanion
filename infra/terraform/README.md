# Travel Companion Terraform

Infraestructura Azure para el MVP de Travel Companion.

## Recursos creados

Por defecto, con `allow_paid_resources = false`, Terraform crea solo una base minima:

- Resource Group
- Key Vault
- Log Analytics Workspace
- Application Insights

Cuando `allow_paid_resources = true`, tambien crea el MVP cloud completo:

- Linux App Service Plan
- Linux Web App para `TravelCompanion.Api` y Admin CMS
- Azure Database for PostgreSQL Flexible Server
- Base PostgreSQL `travel_companion`
- Secretos productivos en Key Vault para PostgreSQL y admin

El storage de media es opcional y esta apagado por defecto con `enable_media_storage = false`. Hoy la API no lo usa, asi que no hace falta para compartir la app.

## Stack minimo para compartir la app

Para que alguien use la app sin estar conectado a tu compu por USB/localhost, el minimo necesario es:

- API/Admin publicado en HTTPS con Azure Linux Web App.
- PostgreSQL administrado en Azure para que los datos y usuarios vivan fuera de tu maquina.
- Application Insights/Log Analytics con daily cap bajo para diagnosticar errores.
- Budget alert para controlar credito del free trial.
- Una build mobile que apunte a la URL publica de la API.

Ese modo esta representado por `share-mvp.tfvars.example`:

```powershell
Copy-Item share-mvp.tfvars.example terraform.tfvars
```

Luego editar `terraform.tfvars` y reemplazar passwords/secretos reales.

## Control de costos

El default esta pensado para free trial: `allow_paid_resources = false`.

En ese modo no se crean App Service ni PostgreSQL Flexible Server, que son los dos recursos que mas probablemente consumen creditos mientras existen. La base minima aun crea recursos Azure reales; en reposo deberian tener costo nulo o muy bajo, pero conviene revisar siempre el estimado en Azure Portal o Azure Pricing Calculator antes de aplicar.

Log Analytics y Application Insights tienen cotas diarias bajas por defecto:

- `log_analytics_daily_quota_gb = 0.1`
- `app_insights_daily_cap_gb = 0.1`
- `app_insights_sampling_percentage = 10`

Retencion por defecto:

- `log_analytics_retention_in_days = 30` (minimo practico para este tipo de logs)
- `app_insights_retention_in_days = 30`

Alertas de presupuesto habilitadas por defecto:

- `enable_budget_alerts = true`
- `monthly_budget_amount = 15`
- umbrales de alerta en 50%, 80% y 100% (forecasted)
- destinatarios: `budget_alert_contact_roles = ["Owner"]` y/o `budget_alert_contact_emails`

Para habilitar el MVP cloud completo o el modo shareable, cambiar explicitamente:

```hcl
allow_paid_resources = true
```

Luego completar passwords reales en `terraform.tfvars`.

## Prerrequisitos

- Terraform `>= 1.8`
- Azure CLI
- Una suscripcion Azure activa

Login:

```powershell
az login
az account set --subscription "<subscription-id>"
```

## Configuracion local

Crear un archivo local de variables:

```powershell
Copy-Item terraform.tfvars.example terraform.tfvars
```

Editar `terraform.tfvars` y reemplazar:

- `allow_paid_resources = true`, solo cuando quieras crear App Service y PostgreSQL
- `postgres_admin_password`, requerido cuando `allow_paid_resources = true`
- `admin_auth_username`, requerido cuando `allow_paid_resources = true`
- `admin_auth_password`, requerido cuando `allow_paid_resources = true`
- opcionalmente `postgres_firewall_rules` con tu IP publica

`terraform.tfvars` esta ignorado por Git porque contiene secretos.

## Comandos

```powershell
terraform init
terraform fmt
terraform validate
terraform plan
```

Si el plan se ve correcto y estas seguro de querer crear recursos:

```powershell
terraform plan -out tfplan
terraform apply tfplan
```

Ver outputs:

```powershell
terraform output
```

Destruir ambiente dev:

```powershell
terraform destroy
```

## Deploy de la API

Terraform crea la infraestructura, pero no publica el codigo. Para un deploy manual inicial:

Esto aplica solamente cuando `allow_paid_resources = true`, porque en modo base/minimo no existe la Web App.

```powershell
dotnet publish ..\..\src\TravelCompanion.Api\TravelCompanion.Api.csproj -c Release -o .\publish
Compress-Archive -Path .\publish\* -DestinationPath .\travelcompanion-api.zip -Force
az webapp deploy --resource-group "$(terraform output -raw resource_group_name)" --name "$(terraform output -raw api_app_name)" --src-path .\travelcompanion-api.zip --type zip
```

Despues deberia moverse a GitHub Actions.

## Build mobile apuntando a Azure

Despues del apply, obtener la URL publica:

```powershell
$apiUrl = terraform output -raw api_url
```

Para compilar Android con esa API embebida:

```powershell
dotnet publish ..\..\src\TravelCompanion.Mobile\TravelCompanion.Mobile.csproj -f net10.0-android -c Release -p:TravelCompanionApiBaseUrl=$apiUrl
```

Para iOS, usar la misma propiedad MSBuild al publicar desde Mac/Visual Studio:

```powershell
-p:TravelCompanionApiBaseUrl=https://tu-api.azurewebsites.net
```

La app sigue usando `TRAVELCOMPANION_API_BASE_URL` si existe, pero para compartir una build instalada en otro celular conviene usar `TravelCompanionApiBaseUrl`.

## Notas de seguridad

- El password de PostgreSQL queda en Terraform state por limitaciones naturales de IaC. El state debe tratarse como secreto.
- Para uso real, conviene migrar el state a Azure Storage con versionado y acceso restringido.
- Key Vault usa access policies: el usuario que ejecuta Terraform puede escribir secretos, y la Web App puede leerlos con Managed Identity.
- PostgreSQL queda con endpoint publico en esta primera version. Para produccion madura, considerar VNet integration/private endpoint.

## Region

El default es `westeurope`, una region madura y cercana a Espana. Puede cambiarse en `terraform.tfvars`.

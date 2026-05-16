data "azurerm_client_config" "current" {}

# Sufijo estable dentro del state. Evita colisiones globales en recursos como
# Storage Accounts y Key Vault, cuyos nombres deben ser unicos en Azure.
resource "random_string" "resource_suffix" {
  length  = 6
  lower   = true
  numeric = true
  special = false
  upper   = false
}

locals {
  # Convenciones de nombres. Mantenerlas centralizadas hace que agregar
  # ambientes dev/staging/prod sea mas predecible.
  name_prefix          = lower("${var.resource_prefix}-${var.environment}")
  compact_name_prefix  = replace(local.name_prefix, "-", "")
  postgres_location    = coalesce(var.postgres_location, var.location)
  postgres_name_region = replace(lower(local.postgres_location), " ", "")
  storage_account_name = substr("st${local.compact_name_prefix}${random_string.resource_suffix.result}", 0, 24)

  common_tags = merge(
    {
      project     = var.project_name
      environment = var.environment
      managed_by  = "terraform"
    },
    var.tags
  )

  # Connection string que la API consume con EF Core/Npgsql.
  # Se guarda en Key Vault y la Web App la lee como Key Vault reference.
  postgres_connection_string = var.allow_paid_resources ? join(";", [
    "Host=${azurerm_postgresql_flexible_server.main[0].fqdn}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.app[0].name}",
    "Username=${var.postgres_admin_login}",
    "Password=${var.postgres_admin_password}",
    "SSL Mode=Require",
    "Trust Server Certificate=true"
  ]) : null
}

# Contenedor logico de todos los recursos Azure del ambiente.
resource "azurerm_resource_group" "main" {
  name     = "rg-${local.name_prefix}"
  location = var.location
  tags     = local.common_tags
}

# Workspace central de logs. Application Insights escribe aca la telemetria.
resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${local.name_prefix}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_analytics_retention_in_days
  daily_quota_gb      = var.log_analytics_daily_quota_gb
  tags                = local.common_tags
}

# Observabilidad para la API: errores, requests, tiempos de respuesta, etc.
resource "azurerm_application_insights" "api" {
  name                 = "appi-${local.name_prefix}"
  location             = azurerm_resource_group.main.location
  resource_group_name  = azurerm_resource_group.main.name
  workspace_id         = azurerm_log_analytics_workspace.main.id
  application_type     = "web"
  daily_data_cap_in_gb = var.app_insights_daily_cap_gb
  retention_in_days    = var.app_insights_retention_in_days
  sampling_percentage  = var.app_insights_sampling_percentage
  tags                 = local.common_tags
}

# Alertas de presupuesto para no pasar el gasto mensual previsto del MVP.
resource "azurerm_consumption_budget_resource_group" "main" {
  count = var.enable_budget_alerts ? 1 : 0

  name              = "budget-${local.name_prefix}"
  resource_group_id = azurerm_resource_group.main.id
  amount            = var.monthly_budget_amount
  time_grain        = "Monthly"

  time_period {
    start_date = var.budget_alert_start_date
  }

  notification {
    enabled        = true
    threshold      = 50
    operator       = "GreaterThan"
    threshold_type = "Actual"
    contact_emails = var.budget_alert_contact_emails
    contact_roles  = var.budget_alert_contact_roles
  }

  notification {
    enabled        = true
    threshold      = 80
    operator       = "GreaterThan"
    threshold_type = "Actual"
    contact_emails = var.budget_alert_contact_emails
    contact_roles  = var.budget_alert_contact_roles
  }

  notification {
    enabled        = true
    threshold      = 100
    operator       = "GreaterThan"
    threshold_type = "Forecasted"
    contact_emails = var.budget_alert_contact_emails
    contact_roles  = var.budget_alert_contact_roles
  }
}

# Plan de App Service: define capacidad/costo para la Web App Linux.
resource "azurerm_service_plan" "api" {
  count = var.allow_paid_resources ? 1 : 0

  name                = "plan-${local.name_prefix}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  os_type             = "Linux"
  sku_name            = var.app_service_sku_name
  tags                = local.common_tags
}

# Storage para media privada: imagenes de destinos, recomendaciones, vouchers.
# Es opcional porque el MVP actual todavia no sube archivos a Azure Storage.
resource "azurerm_storage_account" "media" {
  count = var.enable_media_storage ? 1 : 0

  name                            = local.storage_account_name
  location                        = azurerm_resource_group.main.location
  resource_group_name             = azurerm_resource_group.main.name
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  tags                            = local.common_tags
}

resource "azurerm_storage_container" "media" {
  count = var.enable_media_storage ? 1 : 0

  name                  = var.blob_container_name
  storage_account_id    = azurerm_storage_account.media[0].id
  container_access_type = "private"
}

# Key Vault guarda secretos fuera del codigo y de App Settings planos.
# Terraform necesita permisos para escribir secretos durante apply.
resource "azurerm_key_vault" "main" {
  name                       = "kv-${local.name_prefix}-${random_string.resource_suffix.result}"
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 7
  purge_protection_enabled   = false
  tags                       = local.common_tags

  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = data.azurerm_client_config.current.object_id

    secret_permissions = [
      "Delete",
      "Get",
      "List",
      "Set"
    ]
  }

  lifecycle {
    # La API usa un azurerm_key_vault_access_policy separado porque su
    # principal_id existe recien despues de crear la Web App.
    ignore_changes = [access_policy]
  }
}

# Base de datos administrada. Para MVP usamos endpoint publico + firewall.
# Mas adelante podemos endurecerlo con VNet/private endpoint.
resource "azurerm_postgresql_flexible_server" "main" {
  count = var.allow_paid_resources ? 1 : 0

  name                          = "psql-${local.name_prefix}-${local.postgres_name_region}-${random_string.resource_suffix.result}"
  location                      = local.postgres_location
  resource_group_name           = azurerm_resource_group.main.name
  version                       = var.postgres_version
  administrator_login           = var.postgres_admin_login
  administrator_password        = var.postgres_admin_password
  sku_name                      = var.postgres_sku_name
  storage_mb                    = var.postgres_storage_mb
  public_network_access_enabled = true
  tags                          = local.common_tags

  lifecycle {
    # Azure puede asignar/normalizar la zona del Flexible Server. Evitamos que
    # un apply futuro intente recrear o mover el server por esa diferencia.
    ignore_changes = [zone]
  }
}

resource "azurerm_postgresql_flexible_server_database" "app" {
  count = var.allow_paid_resources ? 1 : 0

  name      = var.postgres_database_name
  server_id = azurerm_postgresql_flexible_server.main[0].id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

# Regla especial de Azure para permitir que App Service llegue al server.
resource "azurerm_postgresql_flexible_server_firewall_rule" "azure_services" {
  count            = var.allow_paid_resources && var.allow_azure_services_to_postgres ? 1 : 0
  name             = "AllowAzureServices"
  server_id        = azurerm_postgresql_flexible_server.main[0].id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# Reglas opcionales para IPs concretas, por ejemplo tu casa/oficina para psql.
resource "azurerm_postgresql_flexible_server_firewall_rule" "custom" {
  for_each         = var.allow_paid_resources ? { for rule in var.postgres_firewall_rules : rule.name => rule } : {}
  name             = each.value.name
  server_id        = azurerm_postgresql_flexible_server.main[0].id
  start_ip_address = each.value.start_ip
  end_ip_address   = each.value.end_ip
}

# Secretos consumidos por la Web App mediante referencias de Key Vault.
resource "azurerm_key_vault_secret" "postgres_connection_string" {
  count = var.allow_paid_resources ? 1 : 0

  name         = "postgres-connection-string"
  value        = local.postgres_connection_string
  key_vault_id = azurerm_key_vault.main.id
}

resource "azurerm_key_vault_secret" "admin_auth_username" {
  count = var.allow_paid_resources ? 1 : 0

  name         = "admin-auth-username"
  value        = var.admin_auth_username
  key_vault_id = azurerm_key_vault.main.id
}

resource "azurerm_key_vault_secret" "admin_auth_password" {
  count = var.allow_paid_resources ? 1 : 0

  name         = "admin-auth-password"
  value        = var.admin_auth_password
  key_vault_id = azurerm_key_vault.main.id
}

resource "azurerm_key_vault_secret" "openai_api_key" {
  count = var.allow_paid_resources ? 1 : 0

  name         = "openai-api-key"
  value        = var.openai_api_key
  key_vault_id = azurerm_key_vault.main.id
}

resource "azurerm_key_vault_secret" "storage_connection_string" {
  count = var.allow_paid_resources && var.enable_media_storage ? 1 : 0

  name         = "storage-connection-string"
  value        = azurerm_storage_account.media[0].primary_connection_string
  key_vault_id = azurerm_key_vault.main.id
}

locals {
  api_base_app_settings = {
    ASPNETCORE_ENVIRONMENT                = "Production"
    APPLICATIONINSIGHTS_CONNECTION_STRING = azurerm_application_insights.api.connection_string
    ApplicationInsights__ConnectionString = azurerm_application_insights.api.connection_string
    ConnectionStrings__TravelCompanionDb  = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.postgres_connection_string[0].versionless_id})"
    AdminAuth__Username                   = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.admin_auth_username[0].versionless_id})"
    AdminAuth__Password                   = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.admin_auth_password[0].versionless_id})"
    OpenAI__ApiKey                        = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.openai_api_key[0].versionless_id})"
  }

  api_media_app_settings = var.enable_media_storage ? {
    Storage__MediaContainerName = azurerm_storage_container.media[0].name
    Storage__ConnectionString   = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.storage_connection_string[0].versionless_id})"
  } : {}
}

# Web App que hostea la API ASP.NET Core y el Admin CMS Razor Pages.
resource "azurerm_linux_web_app" "api" {
  count = var.allow_paid_resources ? 1 : 0

  name                = "app-${local.name_prefix}-${random_string.resource_suffix.result}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  service_plan_id     = azurerm_service_plan.api[0].id
  https_only          = true
  tags                = local.common_tags

  identity {
    type = "SystemAssigned"
  }

  site_config {
    always_on           = true
    minimum_tls_version = "1.2"

    application_stack {
      dotnet_version = var.app_stack_dotnet_version
    }
  }

  logs {
    application_logs {
      file_system_level = "Information"
    }

    http_logs {
      file_system {
        retention_in_days = 3
        retention_in_mb   = 100
      }
    }
  }

  app_settings = merge(local.api_base_app_settings, local.api_media_app_settings)

  lifecycle {
    # Application Insights agrega este hidden-link tag automaticamente.
    ignore_changes = [tags["hidden-link: /app-insights-resource-id"]]
  }
}

# Permite que la identidad administrada de la Web App lea secretos del vault.
resource "azurerm_key_vault_access_policy" "api" {
  count = var.allow_paid_resources ? 1 : 0

  key_vault_id = azurerm_key_vault.main.id
  tenant_id    = azurerm_linux_web_app.api[0].identity[0].tenant_id
  object_id    = azurerm_linux_web_app.api[0].identity[0].principal_id

  secret_permissions = [
    "Get",
    "List"
  ]
}

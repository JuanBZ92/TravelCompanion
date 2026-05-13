output "resource_group_name" {
  description = "Azure resource group name."
  value       = azurerm_resource_group.main.name
}

output "api_app_name" {
  description = "Azure App Service name for the API/Admin."
  value       = try(azurerm_linux_web_app.api[0].name, null)
}

output "api_url" {
  description = "Public API/Admin URL."
  value       = try("https://${azurerm_linux_web_app.api[0].default_hostname}", null)
}

output "postgres_server_fqdn" {
  description = "PostgreSQL Flexible Server FQDN."
  value       = try(azurerm_postgresql_flexible_server.main[0].fqdn, null)
}

output "postgres_database_name" {
  description = "PostgreSQL database name."
  value       = try(azurerm_postgresql_flexible_server_database.app[0].name, null)
}

output "storage_account_name" {
  description = "Storage account used for media."
  value       = azurerm_storage_account.media.name
}

output "key_vault_name" {
  description = "Key Vault name."
  value       = azurerm_key_vault.main.name
}

output "application_insights_name" {
  description = "Application Insights name."
  value       = azurerm_application_insights.api.name
}

output "resource_group_budget_id" {
  description = "Resource Group budget alert id."
  value       = try(azurerm_consumption_budget_resource_group.main[0].id, null)
}

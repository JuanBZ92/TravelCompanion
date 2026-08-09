variable "project_name" {
  # Tag humano; no tiene que cumplir restricciones de nombre Azure.
  description = "Human-readable project name used for tags."
  type        = string
  default     = "travelcompanion"
}

variable "resource_prefix" {
  # Prefijo corto para mantener nombres legibles y dentro de limites Azure.
  description = "Short prefix used in Azure resource names."
  type        = string
  default     = "tc"
}

variable "environment" {
  # Cambiando esto a staging/prod se obtiene otro set de recursos.
  description = "Deployment environment, for example dev, staging, or prod."
  type        = string
  default     = "dev"
}

variable "location" {
  description = "Azure region for the deployment."
  type        = string
  default     = "westeurope"
}

variable "workload_location" {
  description = "Azure region for workload resources such as App Service, Key Vault, Log Analytics, Application Insights and Storage. Leave null to use location."
  type        = string
  default     = null
}

variable "tags" {
  description = "Extra tags applied to all taggable resources."
  type        = map(string)
  default     = {}
}

variable "allow_paid_resources" {
  # Safety switch. This stack includes resources that can spend credits while
  # they exist, mainly App Service Plan and PostgreSQL Flexible Server.
  description = "Creates the paid shareable MVP resources only when true: App Service Plan, Web App, PostgreSQL and production secrets."
  type        = bool
  default     = false
}

variable "enable_media_storage" {
  description = "Create a private Storage Account/container for future media uploads. Keep false for the minimum shareable MVP because the API does not use it yet."
  type        = bool
  default     = false
}

variable "app_service_sku_name" {
  # F1 minimiza costo para demos/dev. Para worker confiable o Always On, subir a B1+.
  description = "Azure App Service plan SKU. F1/D1 disable Always On automatically."
  type        = string
  default     = "F1"
}

variable "app_stack_dotnet_version" {
  description = "The .NET runtime version configured for the Linux Web App."
  type        = string
  default     = "10.0"
}

variable "api_run_from_package_enabled" {
  description = "Enable WEBSITE_RUN_FROM_PACKAGE for API-only deployments. Keep false when WebJobs must be visible/manageable in the App Service portal."
  type        = bool
  default     = false
}

variable "notifications_enabled" {
  description = "Enable the notifications worker in the API App Service WebJob."
  type        = bool
  default     = true
}

variable "notifications_poll_interval_seconds" {
  description = "Polling interval in seconds for the notifications worker."
  type        = number
  default     = 60

  validation {
    condition     = var.notifications_poll_interval_seconds >= 15
    error_message = "notifications_poll_interval_seconds must be at least 15 seconds."
  }
}

variable "notifications_look_ahead_hours" {
  description = "How many hours ahead the worker scans for schedule reminders."
  type        = number
  default     = 48

  validation {
    condition     = var.notifications_look_ahead_hours > 0
    error_message = "notifications_look_ahead_hours must be greater than 0."
  }
}

variable "notifications_send_batch_size" {
  description = "Maximum number of due notifications dispatched per worker pass."
  type        = number
  default     = 50

  validation {
    condition     = var.notifications_send_batch_size > 0
    error_message = "notifications_send_batch_size must be greater than 0."
  }
}

variable "notifications_stale_notification_grace_minutes" {
  description = "Grace window in minutes before stale due notifications are skipped."
  type        = number
  default     = 30

  validation {
    condition     = var.notifications_stale_notification_grace_minutes >= 0
    error_message = "notifications_stale_notification_grace_minutes cannot be negative."
  }
}

variable "notifications_schedule_time_zone_id" {
  description = "Fallback time zone used by the notifications worker when a reservation, trip, or destination time zone is missing."
  type        = string
  default     = "UTC"

  validation {
    condition     = length(trimspace(var.notifications_schedule_time_zone_id)) > 0
    error_message = "notifications_schedule_time_zone_id cannot be empty."
  }
}

variable "notifications_reservation_reminder_lead_minutes" {
  description = "Lead times in minutes for reservation reminders."
  type        = list(number)
  default     = [1440, 180]

  validation {
    condition     = length(var.notifications_reservation_reminder_lead_minutes) > 0 && alltrue([for minutes in var.notifications_reservation_reminder_lead_minutes : minutes > 0])
    error_message = "notifications_reservation_reminder_lead_minutes must contain positive minute values."
  }
}

variable "log_analytics_daily_quota_gb" {
  # Cota baja para evitar sorpresas si algun recurso empieza a emitir muchos logs.
  description = "Daily ingestion quota in GB for Log Analytics."
  type        = number
  default     = 0.1

  validation {
    condition     = var.log_analytics_daily_quota_gb >= 0.023
    error_message = "log_analytics_daily_quota_gb must be >= 0.023 GB (Azure minimum)."
  }
}

variable "log_analytics_retention_in_days" {
  # En workspaces de Azure Monitor el minimo practico para Analytics logs es 30 dias.
  description = "Data retention in days for Log Analytics workspace."
  type        = number
  default     = 30
}

variable "app_insights_daily_cap_gb" {
  # Application Insights tambien cobra por ingesta. Mantener bajo en dev/free trial.
  description = "Daily ingestion cap in GB for Application Insights."
  type        = number
  default     = 0.1
}

variable "app_insights_retention_in_days" {
  description = "Data retention in days for Application Insights."
  type        = number
  default     = 30
}

variable "app_insights_sampling_percentage" {
  description = "Application Insights ingestion sampling percentage (100 = no sampling)."
  type        = number
  default     = 10

  validation {
    condition     = var.app_insights_sampling_percentage > 0 && var.app_insights_sampling_percentage <= 100
    error_message = "app_insights_sampling_percentage must be > 0 and <= 100."
  }
}

variable "enable_budget_alerts" {
  description = "Create Azure budget alerts for this resource group."
  type        = bool
  default     = true
}

variable "monthly_budget_amount" {
  description = "Monthly budget amount in the subscription currency."
  type        = number
  default     = 15
}

variable "budget_alert_start_date" {
  description = "RFC3339 start date for the budget period."
  type        = string
  default     = "2026-01-01T00:00:00Z"
}

variable "budget_alert_contact_emails" {
  description = "Email recipients for budget alerts."
  type        = list(string)
  default     = []
}

variable "budget_alert_contact_roles" {
  description = "Azure RBAC roles that receive budget alerts."
  type        = list(string)
  default     = ["Owner"]
}

variable "postgres_version" {
  description = "PostgreSQL major version."
  type        = string
  default     = "16"
}

variable "postgres_location" {
  description = "Azure region for PostgreSQL Flexible Server. Leave null to use location. Useful when a free-trial subscription restricts PostgreSQL in the main region."
  type        = string
  default     = null
}

variable "postgres_sku_name" {
  # B_Standard_B1ms es economico. Para prod real, mirar General Purpose.
  description = "Azure Database for PostgreSQL Flexible Server SKU."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "postgres_storage_mb" {
  description = "PostgreSQL storage size in MB."
  type        = number
  default     = 32768
}

variable "postgres_database_name" {
  description = "Application database name."
  type        = string
  default     = "travel_companion"
}

variable "postgres_admin_login" {
  description = "PostgreSQL administrator username."
  type        = string
  default     = "tcadmin"
}

variable "postgres_admin_password" {
  # No versionar este valor. Usar terraform.tfvars local o variables de CI.
  description = "PostgreSQL administrator password."
  type        = string
  sensitive   = true
  nullable    = true
  default     = null

  validation {
    condition     = !var.allow_paid_resources || try(length(var.postgres_admin_password) >= 16, false)
    error_message = "postgres_admin_password must be at least 16 characters when allow_paid_resources is true."
  }

  validation {
    condition     = !var.allow_paid_resources || try(!can(regex(";", var.postgres_admin_password)), false)
    error_message = "postgres_admin_password cannot contain semicolons because it is embedded in a PostgreSQL connection string."
  }
}

variable "postgres_firewall_rules" {
  # Esto abre acceso directo a Postgres desde IPs conocidas. Mantener acotado.
  description = "Optional PostgreSQL firewall rules for local admin access."
  type = list(object({
    name     = string
    start_ip = string
    end_ip   = string
  }))
  default = []
}

variable "allow_azure_services_to_postgres" {
  description = "Allow Azure services to reach PostgreSQL over the public endpoint."
  type        = bool
  default     = true
}

variable "admin_auth_username" {
  description = "Initial production admin username."
  type        = string
  sensitive   = true
  nullable    = true
  default     = null

  validation {
    condition     = !var.allow_paid_resources || try(length(trimspace(var.admin_auth_username)) > 0, false)
    error_message = "admin_auth_username is required when allow_paid_resources is true."
  }
}

variable "admin_auth_password" {
  # Password inicial del admin CMS en cloud. Luego deberia migrar a Identity/SSO.
  description = "Initial production admin password."
  type        = string
  sensitive   = true
  nullable    = true
  default     = null

  validation {
    condition     = !var.allow_paid_resources || try(length(var.admin_auth_password) >= 16, false)
    error_message = "admin_auth_password must be at least 16 characters when allow_paid_resources is true."
  }
}

variable "openai_api_key" {
  description = "Server-side OpenAI API key for the travel assistant. Store only in local tfvars or CI secrets."
  type        = string
  sensitive   = true
  nullable    = true
  default     = null

  validation {
    condition     = !var.allow_paid_resources || try(length(trimspace(var.openai_api_key)) > 0, false)
    error_message = "openai_api_key is required when allow_paid_resources is true."
  }
}

variable "blob_container_name" {
  description = "Private blob container for destination/recommendation media."
  type        = string
  default     = "media"
}

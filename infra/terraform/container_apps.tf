# User-assigned (not system-assigned) identity, created before the compute
# resources that use it — breaks the circular dependency a system-assigned
# identity would create (role grants need a principal_id that doesn't exist
# until the resource using it is created, but that resource needs the role
# granted before it can resolve its own Key Vault secrets).
resource "azurerm_user_assigned_identity" "api" {
  name                = var.identity_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
}

resource "azurerm_role_assignment" "api_kv_secrets_user" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.api.principal_id
}

resource "azurerm_role_assignment" "api_acr_pull" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.api.principal_id
}

resource "azurerm_log_analytics_workspace" "main" {
  name                = var.log_analytics_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "azurerm_container_app_environment" "main" {
  name                       = var.container_app_environment_name
  resource_group_name        = azurerm_resource_group.main.name
  location                   = azurerm_resource_group.main.location
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
}

# Every secret block below follows the same shape: a Key Vault reference
# resolved by the shared user-assigned identity, never a literal value.
locals {
  secret_blocks = {
    for k, v in azurerm_key_vault_secret.secrets : k => {
      name                = replace(lower(k), "_", "-")
      key_vault_secret_id = v.id
    }
  }

  # Shared across the API app and both jobs — same secrets, same identity.
  common_env = [
    { name = "ENV", value = var.environment },
    { name = "PLAID_ENV", value = var.plaid_env },
    { name = "PLAID_CLIENT_ID", value = var.plaid_client_id },
    { name = "RESEND_FROM_EMAIL", value = var.resend_from_email },
    { name = "APNS_KEY_ID", value = var.apns_key_id },
    { name = "APNS_TEAM_ID", value = var.apns_team_id },
    { name = "APNS_ENVIRONMENT", value = var.apns_environment },
  ]

  common_secret_env = [
    for k in ["DATABASE_URL", "JWT_SECRET", "PLAID_SECRET", "ENCRYPTION_KEY", "RESEND_API_KEY", "MEDIATR_LICENSE_KEY", "APNS_AUTH_KEY"] :
    { name = k, secret_name = local.secret_blocks[k].name }
  ]
}

resource "azurerm_container_app" "api" {
  name                         = var.container_app_name
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.api.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.api.id
  }

  dynamic "secret" {
    for_each = local.secret_blocks
    content {
      name                = secret.value.name
      key_vault_secret_id = secret.value.key_vault_secret_id
      identity            = azurerm_user_assigned_identity.api.id
    }
  }

  template {
    min_replicas = 0
    max_replicas = 3

    container {
      name   = "api"
      image  = var.container_image
      cpu    = 0.5
      memory = "1Gi"

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.value.name
          value = env.value.value
        }
      }
      dynamic "env" {
        for_each = local.common_secret_env
        content {
          name        = env.value.name
          secret_name = env.value.secret_name
        }
      }
      env {
        name  = "CORS_ALLOWED_ORIGINS"
        value = var.cors_allowed_origins
      }
      env {
        name  = "FRONTEND_URL"
        value = var.frontend_url
      }
      env {
        name  = "GOOGLE_REDIRECT_URI"
        value = var.google_redirect_uri
      }
      env {
        name  = "GOOGLE_CLIENT_ID"
        value = var.google_client_id
      }
      env {
        name        = "GOOGLE_CLIENT_SECRET"
        secret_name = local.secret_blocks["GOOGLE_CLIENT_SECRET"].name
      }
      env {
        name  = "APPLE_KEY_ID"
        value = var.apple_key_id
      }
      env {
        name        = "APPLE_PRIVATE_KEY"
        secret_name = local.secret_blocks["APPLE_PRIVATE_KEY"].name
      }
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }
}

# cycle-budgets: 1st of each month, matching today's Cloud Scheduler trigger exactly.
resource "azurerm_container_app_job" "cycle_budgets" {
  name                         = "cycle-budgets"
  resource_group_name          = azurerm_resource_group.main.name
  location                     = azurerm_resource_group.main.location
  container_app_environment_id = azurerm_container_app_environment.main.id

  replica_timeout_in_seconds = 1800
  replica_retry_limit        = 1

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.api.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.api.id
  }

  dynamic "secret" {
    for_each = local.secret_blocks
    content {
      name                = secret.value.name
      key_vault_secret_id = secret.value.key_vault_secret_id
      identity            = azurerm_user_assigned_identity.api.id
    }
  }

  schedule_trigger_config {
    cron_expression          = "0 0 1 * *"
    parallelism              = 1
    replica_completion_count = 1
  }

  template {
    container {
      name    = "cycle-budgets"
      image   = var.container_image
      cpu     = 0.5
      memory  = "1Gi"
      command = ["./cycle-budgets/WellSpent.Jobs.CycleBudgets"]

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.value.name
          value = env.value.value
        }
      }
      dynamic "env" {
        for_each = local.common_secret_env
        content {
          name        = env.value.name
          secret_name = env.value.secret_name
        }
      }
      env {
        name  = "FRONTEND_URL"
        value = var.frontend_url
      }
    }
  }
}

# plaid-sync: Mon/Wed/Fri 9am UTC, matching today's Cloud Scheduler trigger exactly.
resource "azurerm_container_app_job" "plaid_sync" {
  name                         = "plaid-sync"
  resource_group_name          = azurerm_resource_group.main.name
  location                     = azurerm_resource_group.main.location
  container_app_environment_id = azurerm_container_app_environment.main.id

  replica_timeout_in_seconds = 1800
  replica_retry_limit        = 1

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.api.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.api.id
  }

  dynamic "secret" {
    for_each = local.secret_blocks
    content {
      name                = secret.value.name
      key_vault_secret_id = secret.value.key_vault_secret_id
      identity            = azurerm_user_assigned_identity.api.id
    }
  }

  schedule_trigger_config {
    cron_expression          = "0 9 * * 1,3,5"
    parallelism              = 1
    replica_completion_count = 1
  }

  template {
    container {
      name    = "plaid-sync"
      image   = var.container_image
      cpu     = 0.5
      memory  = "1Gi"
      command = ["./plaid-sync/WellSpent.Jobs.PlaidSync"]

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.value.name
          value = env.value.value
        }
      }
      dynamic "env" {
        for_each = local.common_secret_env
        content {
          name        = env.value.name
          secret_name = env.value.secret_name
        }
      }
    }
  }
}

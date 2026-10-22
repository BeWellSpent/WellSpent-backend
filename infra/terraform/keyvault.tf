data "azurerm_client_config" "current" {}

# RBAC authorization (not legacy access policies) — consistent with every
# other resource here using azurerm_role_assignment rather than a policy list.
resource "azurerm_key_vault" "main" {
  name                       = "wellspent-prod-kv"
  resource_group_name        = azurerm_resource_group.main.name
  location                   = azurerm_resource_group.main.location
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  purge_protection_enabled   = true
}

# Terraform (the CI identity) writes secret values; it does not need to read
# them back, so this is write-oriented, not the broad "Key Vault
# Administrator" role.
resource "azurerm_role_assignment" "github_actions_kv_officer" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = azuread_service_principal.github_actions.object_id
}

locals {
  secrets = {
    DATABASE_URL         = var.database_url
    JWT_SECRET           = var.jwt_secret
    GOOGLE_CLIENT_SECRET = var.google_client_secret
    RESEND_API_KEY       = var.resend_api_key
    PLAID_SECRET         = var.plaid_secret
    ENCRYPTION_KEY       = var.encryption_key
    MEDIATR_LICENSE_KEY  = var.mediatr_license_key
    APPLE_PRIVATE_KEY    = var.apple_private_key
    APNS_AUTH_KEY        = var.apns_auth_key
  }
}

resource "azurerm_key_vault_secret" "secrets" {
  for_each     = local.secrets
  name         = replace(each.key, "_", "-") # Key Vault secret names can't contain underscores
  value        = each.value
  key_vault_id = azurerm_key_vault.main.id

  depends_on = [azurerm_role_assignment.github_actions_kv_officer]
}

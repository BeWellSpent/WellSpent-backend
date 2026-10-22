# Replaces GCP Artifact Registry. Basic SKU — this is a single low-traffic
# image, nowhere near Basic's throughput/storage limits.
resource "azurerm_container_registry" "main" {
  name                = "wellspentacr"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Basic"
  admin_enabled       = false # push/pull via RBAC + OIDC identities, not a shared admin password
}

# CI identity needs to push the built image.
resource "azurerm_role_assignment" "github_actions_acr_push" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPush"
  principal_id         = azuread_service_principal.github_actions.object_id
}

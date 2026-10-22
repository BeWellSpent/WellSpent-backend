output "container_registry_login_server" {
  value = azurerm_container_registry.main.login_server
}

output "api_fqdn" {
  value = azurerm_container_app.api.ingress[0].fqdn
}

output "key_vault_uri" {
  value = azurerm_key_vault.main.vault_uri
}

# Add these three as GitHub repo secrets (AZURE_CLIENT_ID/AZURE_TENANT_ID/
# AZURE_SUBSCRIPTION_ID) for the rewritten deploy workflow's azure/login step.
output "github_actions_client_id" {
  value = azuread_application.github_actions.client_id
}

output "github_actions_tenant_id" {
  value = data.azurerm_client_config.current.tenant_id
}

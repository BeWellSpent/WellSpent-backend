# GitHub Actions authenticates as this app via OIDC — no client secret is
# ever generated or stored. Federated credentials below trust a GitHub
# Actions-issued token only when it asserts it's running for this exact
# repo+ref, nothing else.

resource "azuread_application" "github_actions" {
  display_name = var.github_actions_app_display_name
}

resource "azuread_service_principal" "github_actions" {
  client_id = azuread_application.github_actions.client_id
}

resource "azuread_application_federated_identity_credential" "develop" {
  application_id = azuread_application.github_actions.id
  display_name   = "github-develop-branch"
  description    = "OIDC trust for pushes to develop (ci-csharp.yml)"
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = "https://token.actions.githubusercontent.com"
  subject        = "repo:${var.github_repo}:ref:refs/heads/develop"
}

resource "azuread_application_federated_identity_credential" "main" {
  application_id = azuread_application.github_actions.id
  display_name   = "github-main-branch"
  description    = "OIDC trust for pushes to main (ci-main.yml auto-deploy)"
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = "https://token.actions.githubusercontent.com"
  subject        = "repo:${var.github_repo}:ref:refs/heads/main"
}

# No separate workflow_dispatch credential: GitHub's OIDC subject claim is
# ref-based regardless of trigger type, so a manually-dispatched run against
# develop (deploy-csharp.yml, run-csharp-job.yml) produces the identical
# subject the "develop" credential above already covers.

# Scoped to this resource group only, not the whole subscription — Terraform
# needs to manage everything in it (registry, vault, container apps, roles).
resource "azurerm_role_assignment" "github_actions_contributor" {
  scope                = azurerm_resource_group.main.id
  role_definition_name = "Contributor"
  principal_id         = azuread_service_principal.github_actions.object_id
}

# Separate from Contributor above: Contributor alone can't assign RBAC roles
# to other principals, and this config assigns roles (see keyvault.tf,
# registry.tf) — so the CI identity also needs this on the resource group.
resource "azurerm_role_assignment" "github_actions_rbac_admin" {
  scope                = azurerm_resource_group.main.id
  role_definition_name = "Role Based Access Control Administrator"
  principal_id         = azuread_service_principal.github_actions.object_id
}

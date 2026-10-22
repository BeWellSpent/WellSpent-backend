variable "subscription_id" {
  type = string
}

variable "location" {
  type    = string
  default = "eastus2"
}

variable "resource_group_name" {
  type    = string
  default = "wellspent-prod"
}

# Azure resource names below — none of these are fixed by anything, they're
# just names. acr_name/key_vault_name must be globally unique across all of
# Azure (same constraint as a GCP project ID or an S3 bucket name); the rest
# only need to be unique within this resource group. Change any default if
# `terraform plan` reports a name already taken.

variable "acr_name" {
  type    = string
  default = "wellspentacr"
}

variable "key_vault_name" {
  type    = string
  default = "wellspent-prod-kv"
}

variable "container_app_environment_name" {
  type    = string
  default = "wellspent-env"
}

variable "container_app_name" {
  type    = string
  default = "wellspent-csharp-backend"
}

variable "identity_name" {
  type    = string
  default = "wellspent-api-identity"
}

variable "log_analytics_name" {
  type    = string
  default = "wellspent-logs"
}

variable "github_actions_app_display_name" {
  type    = string
  default = "wellspent-backend-github-actions"
}

variable "environment" {
  type        = string
  default     = "prod"
  description = "Passed into the container as ENV, matching the existing convention (prod/dev)."
}

variable "github_repo" {
  type        = string
  default     = "BeWellSpent/WellSpent-backend"
  description = "owner/repo, for the OIDC federated credential subject claim."
}

# Non-secret, build-time env vars — mirrors deploy-csharp.yml's plain env_vars block.
variable "frontend_url" {
  type    = string
  default = "https://bewellspent.com"
}

variable "cors_allowed_origins" {
  type    = string
  default = "https://bewellspent.com"
}

variable "plaid_env" {
  type    = string
  default = "production"
}

variable "google_redirect_uri" {
  type = string
}

variable "google_client_id" {
  type = string
}

variable "plaid_client_id" {
  type = string
}

variable "resend_from_email" {
  type = string
}

variable "apple_key_id" {
  type = string
}

variable "apns_key_id" {
  type = string
}

variable "apns_team_id" {
  type = string
}

variable "apns_environment" {
  type    = string
  default = "production"
}

# Secrets — all land in Key Vault, resolved by the Container App's managed
# identity at runtime. Pass via TF_VAR_* env vars at apply time (sourced from
# the decrypted .env.prod, same as the GCP pipeline did); never commit a
# tfvars file containing these.
variable "database_url" {
  type      = string
  sensitive = true
}

variable "jwt_secret" {
  type      = string
  sensitive = true
}

variable "google_client_secret" {
  type      = string
  sensitive = true
}

variable "resend_api_key" {
  type      = string
  sensitive = true
}

variable "plaid_secret" {
  type      = string
  sensitive = true
}

variable "encryption_key" {
  type      = string
  sensitive = true
}

variable "mediatr_license_key" {
  type      = string
  sensitive = true
}

variable "apple_private_key" {
  type      = string
  sensitive = true
}

variable "apns_auth_key" {
  type      = string
  sensitive = true
}

variable "container_image" {
  type        = string
  description = "Full ACR image reference incl. tag, set per deploy (e.g. by CI passing -var=\"container_image=...\")."
}

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

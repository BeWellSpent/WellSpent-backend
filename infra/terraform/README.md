# Terraform — Azure infrastructure

Replaces the GCP Cloud Run + Secret Manager setup (`.github/workflows/deploy-csharp.yml`,
`ci-main.yml`'s deploy job) with Azure Container Apps + Key Vault, as code.

## What this manages

- Resource group, Azure Container Registry, Key Vault (RBAC-authorized)
- One Container Apps Environment, the API Container App, and the two
  background jobs (`cycle-budgets`: 1st of each month; `plaid-sync`: Mon/Wed/Fri
  9am UTC — both cron expressions copied exactly from the GCP Cloud Scheduler
  triggers they replace, in `ci-main.yml`)
- A user-assigned managed identity shared by the app and both jobs, with
  least-privilege RBAC roles (`Key Vault Secrets User`, `AcrPull`) — secrets
  are resolved by Azure at container-start time via this identity, never
  passed through a deploy command or CI logs
- A GitHub Actions OIDC app registration (`identity.tf`) — no client secret
  is ever generated or stored. This is for the *fast path* (build image →
  push to ACR → bump the running container app's image tag on every commit),
  kept separate from Terraform's own run below.

## Setup (one-time)

1. **Install the Azure CLI and Terraform locally** (`brew install azure-cli`,
   `brew install hashicorp/tap/terraform` — the plain `brew install terraform`
   formula is deprecated; it must come from HashiCorp's own tap).
2. **`az login`**, then `az account show` for your subscription ID.
3. **HCP Terraform**: org `wellspent-org` already exists. Create a workspace
   named `wellspent-backend` under it (CLI-driven mode is simplest to start —
   VCS-connected mode, where HCP Terraform watches this repo directly and
   runs plan/apply on its own, is a reasonable upgrade later but adds its own
   setup). Run `terraform login` once locally to get a token for state/backend
   access.
4. `terraform init` from this directory.
5. `terraform.tfvars` (gitignored, already generated once — regenerate if a
   value changes) holds `subscription_id` and the plain, non-secret
   variables. `source set-secret-vars.sh` (also gitignored) exports the
   9 `sensitive = true` variables as `TF_VAR_*` in your current shell only —
   3 from the already-decrypted `.env.prod`, 6 from GCP Secret Manager via
   `gcloud` — never printed, never written to a file.
6. `terraform plan`, review, then `terraform apply`.

## Open question, deliberately not resolved here

**How does `terraform apply` itself authenticate to Azure?** Two real options:

- **Today's default**: run it from your own machine, authenticated via your
  own `az login` session (the `azurerm`/`azuread` providers pick that up
  automatically with no extra config). Simplest, no additional setup, but
  means applies are manual and tied to your own account's permissions.
- **HCP Terraform Dynamic Credentials (OIDC)**: HCP Terraform's own runs
  authenticate to Azure directly, no stored secret, fully automatable from
  the HCP Terraform UI/VCS integration. Requires its own `azuread_application`
  + federated credential trusting HCP Terraform's OIDC issuer (not GitHub's —
  a different trust relationship from the one `identity.tf` already sets up
  for the image-build-and-deploy path), bootstrapped by hand once since it's
  the same chicken-and-egg as the state backend used to be.

Left as a manual `az login` for the first real apply; worth revisiting once
the setup is confirmed working end to end.

## What is deliberately NOT here

- Image build/push and the fast per-commit redeploy — that's a GitHub Actions
  workflow step (rewrite of `deploy-csharp.yml`), using the OIDC identity
  `identity.tf` creates, not Terraform. Terraform owns structure; CI owns the
  image tag on the already-existing Container App.
- Any change to Neon (the Postgres database) — it's a third-party service
  outside both GCP and Azure, untouched by this migration.

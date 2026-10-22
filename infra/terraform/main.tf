terraform {
  required_version = ">= 1.9"

  # HCP Terraform (Terraform Cloud) holds state, locking, and run history —
  # no Azure Storage Account to bootstrap for that purpose. Create the
  # "wellspent-backend" workspace under this org once (CLI-driven or
  # VCS-connected to this repo) before the first `terraform init` here.
  cloud {
    organization = "wellspent-org"
    workspaces {
      name = "wellspent-backend"
    }
  }

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
}

provider "azuread" {}

resource "azurerm_resource_group" "main" {
  name     = var.resource_group_name
  location = var.location
}

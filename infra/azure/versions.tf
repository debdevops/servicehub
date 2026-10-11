terraform {
  required_version = ">= 1.6"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}

provider "azurerm" {
  features {
    key_vault {
      # A destroy really removes the vault, so the same name can be used again straight away.
      purge_soft_delete_on_destroy = true
    }
  }
  subscription_id = var.subscription_id
}

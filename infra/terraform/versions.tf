terraform {
  required_version = ">= 1.6.0"

  backend "azurerm" {}

  required_providers {
    azurerm = {
      source = "hashicorp/azurerm"
      # >= 4.20 for a mature azurerm_function_app_flex_consumption resource.
      version = "~> 4.20"
    }

    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }

    time = {
      source  = "hashicorp/time"
      version = "~> 0.13"
    }
  }
}

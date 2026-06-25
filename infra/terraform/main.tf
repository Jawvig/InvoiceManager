data "azuread_client_config" "current" {}
data "azurerm_client_config" "current" {}

provider "azurerm" {
  features {}
}

resource "azurerm_resource_group" "invoice_manager" {
  name     = local.resource_group_name
  location = var.location
}

resource "azuread_application" "invoice_manager" {
  display_name     = local.application_display_name
  sign_in_audience = "AzureADMyOrg"

  web {
    redirect_uris = var.redirect_uris
  }

  required_resource_access {
    resource_app_id = local.api_permissions.azure_resource_manager.application_id

    resource_access {
      id   = local.api_permissions.azure_resource_manager.scopes.user_impersonation
      type = "Scope"
    }
  }

  required_resource_access {
    resource_app_id = local.api_permissions.microsoft_graph.application_id

    resource_access {
      id   = local.api_permissions.microsoft_graph.scopes.user_read
      type = "Scope"
    }
  }
}

resource "azuread_service_principal" "invoice_manager" {
  client_id = azuread_application.invoice_manager.client_id
}

resource "azuread_application_password" "admin_web" {
  application_id = azuread_application.invoice_manager.id
  display_name   = "InvoiceManager Admin Web"
  end_date       = timeadd(timestamp(), "8760h")
  rotate_when_changed = {
    application_object_id = azuread_application.invoice_manager.object_id
  }
}

resource "azurerm_key_vault" "invoice_manager" {
  name                       = local.key_vault_name
  location                   = azurerm_resource_group.invoice_manager.location
  resource_group_name        = azurerm_resource_group.invoice_manager.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 7

  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = data.azurerm_client_config.current.object_id

    secret_permissions = [
      "Delete",
      "Get",
      "List",
      "Purge",
      "Recover",
      "Set",
    ]
  }
}

resource "azurerm_key_vault_secret" "microsoft_authorization_client_secret" {
  name         = "MicrosoftAuthorization--ClientSecret"
  value        = azuread_application_password.admin_web.value
  key_vault_id = azurerm_key_vault.invoice_manager.id
  content_type = "Entra application password for InvoiceManager Admin Web"
}

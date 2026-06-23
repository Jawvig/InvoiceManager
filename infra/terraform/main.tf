data "azuread_client_config" "current" {}

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

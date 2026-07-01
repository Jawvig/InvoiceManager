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

resource "time_rotating" "admin_web_password" {
  rotation_days = 365
}

resource "azuread_application_password" "admin_web" {
  application_id = azuread_application.invoice_manager.id
  display_name   = "InvoiceManager Admin Web"
  end_date       = time_rotating.admin_web_password.rotation_rfc3339
  rotate_when_changed = {
    application_object_id = azuread_application.invoice_manager.object_id
    rotation              = time_rotating.admin_web_password.id
  }
}

resource "azurerm_cosmosdb_account" "invoice_manager" {
  name                = local.cosmos_account_name
  location            = azurerm_resource_group.invoice_manager.location
  resource_group_name = azurerm_resource_group.invoice_manager.name
  offer_type          = "Standard"
  kind                = "GlobalDocumentDB"

  consistency_policy {
    consistency_level = "Session"
  }

  geo_location {
    location          = azurerm_resource_group.invoice_manager.location
    failover_priority = 0
  }

  capabilities {
    name = "EnableServerless"
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_cosmosdb_sql_database" "invoice_manager" {
  name                = local.cosmos_database_name
  resource_group_name = azurerm_cosmosdb_account.invoice_manager.resource_group_name
  account_name        = azurerm_cosmosdb_account.invoice_manager.name

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_cosmosdb_sql_container" "invoice_configurations" {
  name                = "invoice-configurations"
  resource_group_name = azurerm_cosmosdb_account.invoice_manager.resource_group_name
  account_name        = azurerm_cosmosdb_account.invoice_manager.name
  database_name       = azurerm_cosmosdb_sql_database.invoice_manager.name
  partition_key_paths = ["/integrationType"]

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_cosmosdb_sql_container" "invoice_records" {
  name                = "invoice-records"
  resource_group_name = azurerm_cosmosdb_account.invoice_manager.resource_group_name
  account_name        = azurerm_cosmosdb_account.invoice_manager.name
  database_name       = azurerm_cosmosdb_sql_database.invoice_manager.name
  partition_key_paths = ["/configurationId"]

  lifecycle {
    prevent_destroy = true
  }
}

# Grants the deploying identity Cosmos DB data-plane write access so the seeder
# can run immediately after terraform apply without a separate auth step.
resource "azurerm_cosmosdb_sql_role_assignment" "deployer_data_contributor" {
  resource_group_name = azurerm_resource_group.invoice_manager.name
  account_name        = azurerm_cosmosdb_account.invoice_manager.name
  role_definition_id  = "${azurerm_cosmosdb_account.invoice_manager.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"
  principal_id        = data.azurerm_client_config.current.object_id
  scope               = azurerm_cosmosdb_account.invoice_manager.id
}

resource "azurerm_key_vault" "invoice_manager" {
  name                       = local.key_vault_name
  location                   = azurerm_resource_group.invoice_manager.location
  resource_group_name        = azurerm_resource_group.invoice_manager.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 7
  rbac_authorization_enabled = true

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_role_assignment" "terraform_key_vault_secrets_officer" {
  scope                = azurerm_key_vault.invoice_manager.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_key_vault_secret" "microsoft_authorization_client_secret" {
  name         = "MicrosoftAuthorization--ClientSecret"
  value        = azuread_application_password.admin_web.value
  key_vault_id = azurerm_key_vault.invoice_manager.id
  content_type = "Entra application password for InvoiceManager Admin Web"

  depends_on = [
    azurerm_role_assignment.terraform_key_vault_secrets_officer
  ]

  lifecycle {
    prevent_destroy = true
  }
}

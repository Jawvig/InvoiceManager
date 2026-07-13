output "environment" {
  description = "Deployment environment."
  value       = var.environment
}

output "tenant_id" {
  description = "Tenant ID used by the AzureAD provider."
  value       = data.azuread_client_config.current.tenant_id
}

output "application_client_id" {
  description = "Client ID for the InvoiceManager Entra app registration."
  value       = azuread_application.invoice_manager.client_id
}

output "application_object_id" {
  description = "Object ID for the InvoiceManager Entra app registration."
  value       = azuread_application.invoice_manager.object_id
}

output "service_principal_object_id" {
  description = "Object ID for the tenant-local Enterprise Application/service principal."
  value       = azuread_service_principal.invoice_manager.object_id
}

output "resource_group_name" {
  description = "Resource group for the InvoiceManager environment resources."
  value       = azurerm_resource_group.invoice_manager.name
}

output "cosmos_endpoint" {
  description = "Cosmos DB account endpoint URI used by the seeder and Functions app."
  value       = azurerm_cosmosdb_account.invoice_manager.endpoint
}

output "cosmos_database_name" {
  description = "Cosmos DB database name."
  value       = azurerm_cosmosdb_sql_database.invoice_manager.name
}

output "key_vault_name" {
  description = "Key Vault name for environment secrets."
  value       = azurerm_key_vault.invoice_manager.name
}

output "key_vault_uri" {
  description = "Key Vault URI for environment secrets."
  value       = azurerm_key_vault.invoice_manager.vault_uri
}

output "functions_app_name" {
  description = "Azure Functions app name. Consumed by CI to deploy the published package."
  value       = azurerm_function_app_flex_consumption.functions.name
}

output "functions_default_hostname" {
  description = "Functions app base URL used by the admin website (Functions:BaseUrl)."
  value       = "https://${azurerm_function_app_flex_consumption.functions.default_hostname}"
}

output "adminweb_container_app_name" {
  description = "Admin website Container App name. Consumed by CI to update the running image."
  value       = azurerm_container_app.adminweb.name
}

output "adminweb_fqdn" {
  description = "Admin website public hostname."
  value       = local.adminweb_fqdn
}

output "adminweb_signin_redirect_uri" {
  description = "Deployed admin website OIDC callback registered on the Entra app."
  value       = local.adminweb_signin_redirect_uri
}

output "functions_identity_client_id" {
  description = "Client ID of the Functions app user-assigned identity."
  value       = azurerm_user_assigned_identity.functions.client_id
}

output "adminweb_identity_client_id" {
  description = "Client ID of the admin website user-assigned identity."
  value       = azurerm_user_assigned_identity.adminweb.client_id
}

output "github_actions_client_id" {
  description = "Client ID of the per-environment GitHub Actions CI Entra app (AZURE_CLIENT_ID for deploy.yml)."
  value       = azuread_application.github_actions.client_id
}

output "github_actions_app_object_id" {
  description = "Object ID of the per-environment GitHub Actions CI Entra app registration."
  value       = azuread_application.github_actions.object_id
}

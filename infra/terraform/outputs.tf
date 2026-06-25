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

output "key_vault_name" {
  description = "Key Vault name for environment secrets."
  value       = azurerm_key_vault.invoice_manager.name
}

output "key_vault_uri" {
  description = "Key Vault URI for environment secrets."
  value       = azurerm_key_vault.invoice_manager.vault_uri
}

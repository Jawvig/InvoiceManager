# InvoiceManager Terraform

This directory contains the first-pass Terraform configuration for
InvoiceManager infrastructure. It currently creates the Microsoft identity
foundation used by the future admin authentication site:

- an Entra app registration
- the tenant-local service principal / Enterprise Application
- declared delegated API permissions for Azure Resource Manager
  `user_impersonation` and Microsoft Graph `User.Read`

Run Terraform through the bootstrap script from the repository root:

```powershell
./scripts/Deploy-Infra.ps1 -Environment test
./scripts/Deploy-Infra.ps1 -Environment production
```

The script creates the Azure Storage backend resources before running
`terraform init`. Backend resource groups and storage accounts are separate per
environment. Production resources use the base name without a `production`
suffix; non-production resources include the environment suffix.

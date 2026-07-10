variable "environment" {
  description = "Deployment environment. Production uses unsuffixed resource names."
  type        = string

  validation {
    condition     = contains(["test", "production"], var.environment)
    error_message = "Environment must be either test or production."
  }
}

variable "display_name" {
  description = "Human-readable application display name."
  type        = string
  default     = "InvoiceManager"
}

variable "application_name" {
  description = "Lowercase application name used in Azure resource names."
  type        = string
  default     = "invoicemanager"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,14}[a-z0-9]$", var.application_name))
    error_message = "Application name must be 3-16 lowercase letters, numbers, or hyphens, start with a letter, and end with a letter or number."
  }
}

variable "location" {
  description = "Azure region for environment resources."
  type        = string
  default     = "uksouth"
}

variable "redirect_uris" {
  description = "Additional allowed web redirect URIs (e.g. https://localhost:5001/signin-oidc for local admin auth). The deployed Container Apps callback is appended automatically."
  type        = list(string)
  default     = []
}

variable "adminweb_image" {
  description = "Container image for the admin website, published to a public ghcr.io package. CI updates the running tag out-of-band, so this is only the initial/bootstrap reference."
  type        = string
  default     = "mcr.microsoft.com/azuredocs/aci-helloworld:latest"
}

variable "functions_runtime_version" {
  description = "dotnet-isolated stack version for the Flex Consumption Functions app. Flex supports 8.0/9.0/10.0 (not net11.0 yet), so the app is published as net10.0 from its multi-targeted project."
  type        = string
  default     = "10.0"
}

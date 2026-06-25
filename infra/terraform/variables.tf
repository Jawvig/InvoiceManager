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
  description = "Allowed web redirect URIs for the future admin authentication site."
  type        = list(string)
  default     = []
}

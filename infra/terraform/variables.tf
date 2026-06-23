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

variable "redirect_uris" {
  description = "Allowed web redirect URIs for the future admin authentication site."
  type        = list(string)
  default     = []
}

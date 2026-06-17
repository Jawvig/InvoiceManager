locals {
  environment_suffix = var.environment == "production" ? "" : "-${var.environment}"

  application_display_name = "${var.display_name}${local.environment_suffix}"

  api_permissions = {
    azure_resource_manager = {
      application_id = "797f4846-ba00-4fd7-ba43-dac1f8f63013"
      scopes = {
        user_impersonation = "41094075-9dad-400e-a0bd-54e686782033"
      }
    }

    microsoft_graph = {
      application_id = "00000003-0000-0000-c000-000000000000"
      scopes = {
        user_read = "e1fe6dd8-ba31-4d61-89e7-88639da4683d"
      }
    }
  }
}

# ---------------------------------------------------------------------------
# Functions app identity-based authorization
#
# The HTTP-triggered GenerateExpectedRecordsHttp endpoint is fronted by App
# Service Authentication (Easy Auth, Entra ID) configured on the Function App
# in main.tf. This file owns the Entra objects that make it a least-privilege,
# assignment-gated API:
#
#   - an app registration that is the token audience and defines an "Invoke"
#     app role (grantable to both users and applications),
#   - its service principal with assignment required, so Entra only issues a
#     token to principals that hold the role, and
#   - the two role assignments: the AdminWeb managed identity (app-only, for
#     the operator-triggered run) and the named operator (for direct calls).
# ---------------------------------------------------------------------------

resource "azuread_application" "functions" {
  display_name     = local.functions_app_registration_display_name
  sign_in_audience = "AzureADMyOrg"

  app_role {
    allowed_member_types = ["Application", "User"]
    description          = "Allows invoking the expected-record generation endpoint."
    display_name         = "Invoke"
    enabled              = true
    id                   = local.functions_invoke_app_role_id
    value                = "Invoke"
  }
}

# api://<client-id> as the identifier URI / token audience. A separate resource
# because the client id is only known after the application is created.
resource "azuread_application_identifier_uri" "functions" {
  application_id = azuread_application.functions.id
  identifier_uri = "api://${azuread_application.functions.client_id}"
}

resource "azuread_service_principal" "functions" {
  client_id = azuread_application.functions.client_id

  # Only principals explicitly assigned the Invoke role receive a token, which is
  # what limits invocation to the AdminWeb identity and the named operator.
  app_role_assignment_required = true
}

# AdminWeb managed identity -> Invoke (app-only token for the operator-triggered run).
resource "azuread_app_role_assignment" "adminweb_invoke_functions" {
  app_role_id         = local.functions_invoke_app_role_id
  principal_object_id = azurerm_user_assigned_identity.adminweb.principal_id
  resource_object_id  = azuread_service_principal.functions.object_id
}

# Named operator -> Invoke (direct calls). Optional so a CI/service-principal apply
# without a signed-in user still plans and applies.
resource "azuread_app_role_assignment" "operator_invoke_functions" {
  count = var.function_invoker_user_object_id == "" ? 0 : 1

  app_role_id         = local.functions_invoke_app_role_id
  principal_object_id = var.function_invoker_user_object_id
  resource_object_id  = azuread_service_principal.functions.object_id
}

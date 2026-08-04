# ---------------------------------------------------------------------------
# CI deployment identity (GitHub Actions -> Azure via OIDC)
#
# One workload identity per environment: an Entra app + federated credential
# (no client secret — pure OIDC token exchange). Because Terraform state is
# per-environment, this file naturally produces one CI app per environment,
# federated to only that environment's GitHub deploy environment and scoped as
# Contributor to only that environment's resource group. This replaces the
# previous single, hand-built, shared app that spanned both environments.
# ---------------------------------------------------------------------------

resource "azuread_application" "github_actions" {
  display_name     = local.github_actions_app_display_name
  sign_in_audience = "AzureADMyOrg"
}

resource "azuread_service_principal" "github_actions" {
  client_id = azuread_application.github_actions.client_id
}

resource "azuread_application_federated_identity_credential" "github_actions" {
  application_id = azuread_application.github_actions.id
  display_name   = "github-${var.environment}"
  issuer         = "https://token.actions.githubusercontent.com"
  audiences      = ["api://AzureADTokenExchange"]

  # Matches GitHub's immutable subject format (owner/repo ids pinned alongside the
  # names) for a job bound to this deploy environment, e.g.
  # repo:omnics@117590014/InvoiceManager@1263133759:environment:test. GitHub moved
  # transferred repos to this format so a recycled owner/repo name can't replay a
  # stale trust relationship - see
  # https://github.blog/changelog/2026-04-23-immutable-subject-claims-for-github-actions-oidc-tokens/.
  subject = "repo:${var.github_owner}@${var.github_owner_id}/${var.github_repository}@${var.github_repository_id}:environment:${var.environment}"
}

# Contributor on this environment's resource group only. Sufficient for both the
# Flex Consumption Kudu deploy (Azure/functions-action) and `az containerapp
# update`, while keeping each environment's CI identity isolated from the other.
resource "azurerm_role_assignment" "github_actions_contributor" {
  scope                = azurerm_resource_group.invoice_manager.id
  role_definition_name = "Contributor"
  principal_id         = azuread_service_principal.github_actions.object_id

  # The service principal is created in the same apply; skip the AAD existence
  # pre-check so the assignment does not fail on Entra replication lag.
  skip_service_principal_aad_check = true
}

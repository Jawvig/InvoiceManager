# ---------------------------------------------------------------------------
# GitHub deploy environment consolidation
#
# Terraform owns the per-environment GitHub deploy environment, its protection
# rules, the CI identity secrets (the OIDC identifiers deploy.yml reads as
# secrets.AZURE_*), and the deploy-target variables the workflow reads to learn
# where to ship. This replaces the out-of-band Publish-GitHubEnvironmentVariables
# step in Deploy-Infra.ps1.
#
# All github_* resources are gated on var.manage_github so a GitHub-less apply is
# still possible; the provider block stays but is only exercised when they exist.
# The environment + secrets are create-or-update (PUT) so they adopt a pre-existing
# GitHub environment without a terraform import and never drop its reviewer gate.
# ---------------------------------------------------------------------------

# Resolves the numeric user id for the production required reviewer. Only needed
# for production (test has no reviewer gate), so it is gated on both flags.
data "github_user" "reviewer" {
  count    = var.manage_github && var.environment == "production" ? 1 : 0
  username = var.production_reviewer
}

resource "github_repository_environment" "deploy" {
  count       = var.manage_github ? 1 : 0
  repository  = var.github_repository
  environment = var.environment

  # Required-reviewer gate on production only; test deploys need no approval.
  dynamic "reviewers" {
    for_each = var.environment == "production" ? [1] : []
    content {
      users = [data.github_user.reviewer[0].id]
    }
  }

  # Only main can deploy. Deploys already run as workflow_run on main; this makes
  # the branch restriction explicit on the environment itself. NOTE: the existing
  # production environment currently uses custom_branch_policies = true, so the
  # first apply shows a one-time shift to protected_branches = true (intended).
  deployment_branch_policy {
    protected_branches     = true
    custom_branch_policies = false
  }
}

# --- CI identity, exposed as environment-scoped Actions secrets ---
# These are OIDC identifiers (not passwords); deploy.yml reads them unchanged as
# secrets.AZURE_CLIENT_ID / AZURE_TENANT_ID / AZURE_SUBSCRIPTION_ID.

resource "github_actions_environment_secret" "azure_client_id" {
  count           = var.manage_github ? 1 : 0
  repository      = var.github_repository
  environment     = github_repository_environment.deploy[0].environment
  secret_name     = "AZURE_CLIENT_ID"
  plaintext_value = azuread_application.github_actions.client_id
}

resource "github_actions_environment_secret" "azure_tenant_id" {
  count           = var.manage_github ? 1 : 0
  repository      = var.github_repository
  environment     = github_repository_environment.deploy[0].environment
  secret_name     = "AZURE_TENANT_ID"
  plaintext_value = data.azurerm_client_config.current.tenant_id
}

resource "github_actions_environment_secret" "azure_subscription_id" {
  count           = var.manage_github ? 1 : 0
  repository      = var.github_repository
  environment     = github_repository_environment.deploy[0].environment
  secret_name     = "AZURE_SUBSCRIPTION_ID"
  plaintext_value = data.azurerm_client_config.current.subscription_id
}

# --- Deploy-target variables (replaces Publish-GitHubEnvironmentVariables) ---

resource "github_actions_environment_variable" "functions_app_name" {
  count         = var.manage_github ? 1 : 0
  repository    = var.github_repository
  environment   = github_repository_environment.deploy[0].environment
  variable_name = "FUNCTIONS_APP_NAME"
  value         = azurerm_function_app_flex_consumption.functions.name
}

resource "github_actions_environment_variable" "functions_default_hostname" {
  count         = var.manage_github ? 1 : 0
  repository    = var.github_repository
  environment   = github_repository_environment.deploy[0].environment
  variable_name = "FUNCTIONS_DEFAULT_HOSTNAME"
  value         = "https://${azurerm_function_app_flex_consumption.functions.default_hostname}"
}

resource "github_actions_environment_variable" "adminweb_container_app_name" {
  count         = var.manage_github ? 1 : 0
  repository    = var.github_repository
  environment   = github_repository_environment.deploy[0].environment
  variable_name = "ADMINWEB_CONTAINER_APP_NAME"
  value         = azurerm_container_app.adminweb.name
}

resource "github_actions_environment_variable" "adminweb_fqdn" {
  count         = var.manage_github ? 1 : 0
  repository    = var.github_repository
  environment   = github_repository_environment.deploy[0].environment
  variable_name = "ADMINWEB_FQDN"
  value         = local.adminweb_fqdn
}

resource "github_actions_environment_variable" "azure_resource_group" {
  count         = var.manage_github ? 1 : 0
  repository    = var.github_repository
  environment   = github_repository_environment.deploy[0].environment
  variable_name = "AZURE_RESOURCE_GROUP"
  value         = azurerm_resource_group.invoice_manager.name
}

# --- Repository-level production deploy gate ---
# deploy.yml's deploy-production job gates at the job level on this repo variable
# (environment variables are invisible to a job-level `if`). Managed only in the
# production state so a test apply never touches it.
resource "github_actions_variable" "production_deploy_enabled" {
  count         = var.manage_github && var.environment == "production" ? 1 : 0
  repository    = var.github_repository
  variable_name = "PRODUCTION_DEPLOY_ENABLED"
  value         = "true"
}

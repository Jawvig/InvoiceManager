# Deployment Strategy

InvoiceManager uses Terraform for Azure infrastructure and GitHub Actions for
continuous deployment. The intended environments are `test` and `production`,
with production protected by a manual GitHub Environment approval.

## Environments

| Environment | Purpose | Deployment | Data and Secrets |
| --- | --- | --- | --- |
| `test` | Validate deployment changes, integrations, and new features. | Automatic after successful builds on `main`. | Test data and non-production credentials. |
| `production` | Run the live invoice workflow. | Manual approval after test succeeds. | Production data and secrets from Azure Key Vault. |

Azure resources should be separated by environment, for example with
`invoicemanager-test-*` and `invoicemanager-prod-*` naming.

## Pipeline

```text
Push to main
  -> Build and unit tests
  -> Deploy test infrastructure and Functions app
  -> Run test-environment checks
  -> Manual production approval
  -> Deploy production infrastructure and Functions app
  -> Run production smoke checks
```

Pull requests should build and test without deploying.

## Infrastructure

Terraform manages:

- Azure Functions Consumption Plan and Function App.
- Azure Cosmos DB for NoSQL in serverless mode.
- Azure Key Vault.
- Application Insights.
- Storage accounts required by the Functions app and Terraform state.

Suggested Terraform layout:

```text
terraform/
  environments/
    test/
    production/
  modules/
    application_insights/
    cosmos_db/
    functions/
    key_vault/
    storage/
  main.tf
  variables.tf
  outputs.tf
```

Each environment should have its own variable file and backend state.

## Configuration and Secrets

Configuration is split across:

- Committed source: project files, non-sensitive Terraform variables, and
  workflow definitions.
- GitHub Actions variables and secrets: Azure subscription, tenant, client, and
  deployment settings.
- Azure Key Vault: runtime secrets such as provider credentials, API keys, OAuth
  credentials, and database connection secrets.
- Local development: `dotnet user-secrets`, local settings ignored by Git, and
  Aspire-supported configuration.

Prefer GitHub OIDC federation for deployment identity. If a client secret is
used, store it only in GitHub secrets and rotate it regularly.

Runtime access to Key Vault should use managed identity where practical.

## Deployment Checklist

Before production deployment:

- All unit tests pass.
- Test environment deployment succeeds.
- Integration checks pass against non-production resources.
- OneDrive and FreeAgent behavior has been validated in test.
- Monitoring and alerting are configured.
- Production Key Vault secrets are present.
- Terraform plan shows expected changes only.
- Code review is approved.

## Rollback

For application regressions, revert the code and redeploy through the pipeline.

For infrastructure regressions, review the Terraform plan, revert the Terraform
change where appropriate, and apply the corrected configuration. Data-bearing
resources such as Cosmos DB may require manual care before destructive changes.

## Monitoring

After deployment, use Application Insights and Azure Monitor to track function
execution, exceptions, provider-call failures, Cosmos DB metrics, Key Vault
access, and invoice records that need retry.

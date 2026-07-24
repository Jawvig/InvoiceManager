# FreeAgent Sandbox POC Manual Setup

These are the human steps required before and during the FreeAgent sandbox POC.
Keep all activity in FreeAgent's sandbox. Do not connect a real bank account,
enter production company data, or authorise the app against the real company.

## Values Used by the POC

Use these values consistently:

| Setting | Value |
|---|---|
| App name | `InvoiceManager FreeAgent Sandbox POC` |
| App homepage, if required | `http://127.0.0.1:53682/` |
| OAuth redirect URI | `http://127.0.0.1:53682/callback/` |
| API base URL | `https://api.sandbox.freeagent.com/v2/` |
| Authorisation endpoint | `https://api.sandbox.freeagent.com/v2/approve_app` |
| Token endpoint | `https://api.sandbox.freeagent.com/v2/token_endpoint` |
| Test bank account name | `InvoiceManager POC Synthetic Bank` |

The redirect URI is an exact-match value. Scheme, host, port, path, and trailing
slash must be identical in the Developer Dashboard and the POC configuration.

If the Developer Dashboard refuses the HTTP loopback URI, stop and record its
validation message. Do not substitute an arbitrary public callback. The POC
implementation and this document should then be changed together to use an
exact `https://localhost` URI backed by the .NET development certificate.

## 1. Create the FreeAgent Sandbox Company

1. Open the official
   [FreeAgent Sandbox signup](https://signup.sandbox.freeagent.com/).
2. Create a new temporary sandbox account using an email address you control.
   Use a distinct sandbox password and store it in a password manager.
3. Follow the verification link if FreeAgent sends one.
4. Sign in to the sandbox company.
5. Complete every company setup/onboarding stage. FreeAgent explicitly warns
   that incomplete setup can cause unexpected API errors.
6. Use recognisably fictitious company details, for example:
   - company name: `InvoiceManager POC Ltd`;
   - company type: UK limited company;
   - base currency: GBP;
   - an accounting start date that permits test bills in both the current and a
     prior period.
7. If the setup asks about VAT, configure the sandbox as VAT registered so the
   POC can observe inclusive/exclusive totals, VAT calculations, and rounding.
   Use sandbox-only dummy registration details where required.
8. Do not import contacts, bills, statements, or other data from a real
   business.
9. Do not connect an Open Banking feed or supply real bank credentials.
10. Finish setup and reach the normal FreeAgent Overview screen.

Record outside the repository:

- the sandbox sign-in email;
- the sandbox company name;
- any expiry or temporary-account date shown by FreeAgent.

Do not record the sandbox password in the repository.

## 2. Verify the Sandbox User Has Full Access

The sandbox account owner should normally have full access, but verify it:

1. In the sandbox, open **Settings**.
2. Open **Users**.
3. Select your sandbox user.
4. Confirm its permission level is **8 — Full**.

Full access lets the POC explore ordinary bill operations, banking operations,
and an optional reversible account-lock scenario:

- level 5 is required for bills;
- level 6 is required for bank transactions and payment explanations;
- level 8 is required to create or remove account locks.

Do not create another user merely to simulate lower permissions during initial
setup. A lower-permission test identity is optional and can be added later if
the POC specifically needs to prove a `403` response.

## 3. Prepare a Synthetic Sandbox Bank Account

This account exists only to explore approved and auto-suggested bill payments.

1. In the sandbox, open **Banking** → **Bank Accounts**.
2. If onboarding already created a suitable business bank account, rename or
   edit it for the POC. Otherwise choose **Add new account** → **Bank Account**.
3. Name it `InvoiceManager POC Synthetic Bank`.
4. Keep it as a business account, not a personal account.
5. Set its opening balance to zero.
6. Ensure **Guess explanations for my transactions** is enabled.
7. Do not enable or connect a bank feed.
8. If sandbox validation requires account-number-like fields, use only
   fictitious sandbox values. Never enter a real sort code, account number, or
   online-banking credential.
9. Save the account.
10. Re-open its settings and verify Guess remains enabled.

Guess must be enabled before the POC imports a synthetic transaction. The POC
will create the corresponding synthetic bill first and then import a matching
test transaction. Do not approve any resulting orange **For approval**
explanation; that unapproved state is one of the required experiments.

If the sandbox cannot run Guess without a real feed, leave the account in place.
The POC will record the guessed-payment scenario as blocked rather than using
real banking data.

## 4. Create a FreeAgent Developer Account

The Developer Dashboard identity and sandbox-company identity are separate
concepts, even if the same email address is used.

1. Open the official
   [FreeAgent Developer Dashboard](https://dev.freeagent.com/).
2. Select **Sign up** if you do not already have a developer account.
3. Complete any email verification.
4. Sign in and open **My Apps**.

Use a password manager for the developer account. Do not save its password in
the repository.

## 5. Register the POC OAuth Application

1. On **My Apps**, select **Create New App**.
2. Enter the application name:

   ```text
   InvoiceManager FreeAgent Sandbox POC
   ```

3. Enter a description such as:

   ```text
   Disposable local console POC for testing FreeAgent sandbox bill search,
   updates, attachments, and payment-state behaviour.
   ```

4. If an application homepage URL is required, enter:

   ```text
   http://127.0.0.1:53682/
   ```

5. In **OAuth Redirect URIs**, enter exactly:

   ```text
   http://127.0.0.1:53682/callback/
   ```

6. If the field supports multiple redirect URIs, remove unrelated defaults and
   retain only the POC callback unless the dashboard requires otherwise.
7. Create/save the application.
8. Open the newly created app's details page.
9. Locate:
   - **OAuth identifier** — the OAuth client ID;
   - **OAuth secret** — the OAuth client secret.
10. Copy both directly into a password manager entry dedicated to this POC.
11. Do not paste either value into:
    - this document;
    - source code;
    - a committed JSON file;
    - an issue or pull request;
    - terminal commands that contain the literal secret;
    - chat or email.

FreeAgent recommends protecting the client secret like a password. A newly
generated replacement secret is shown only once, and the dashboard permits at
most two active secrets.

## 6. Check the App Registration

Before writing or running the OAuth flow:

1. Re-open the app from **My Apps**.
2. Confirm the app name identifies it as a sandbox POC.
3. Confirm the redirect URI is exactly:

   ```text
   http://127.0.0.1:53682/callback/
   ```

4. Confirm the saved OAuth identifier matches the password-manager entry.
5. Do not generate an additional secret unless the first was lost or exposed.
6. Do not use the Google OAuth Playground for this POC. The console application
   will perform and verify its own OAuth flow.

The same Developer Dashboard may manage apps used with different endpoints.
The safety boundary for this POC is therefore also enforced by configuring only
`api.sandbox.freeagent.com` URLs in the console.

## 7. Add Credentials to the POC's Isolated Secret Store

Perform this step only after the POC console project and its unique
`UserSecretsId` exist.

1. Open a terminal in:

   ```text
   poc/freeagent-sandbox/
   ```

2. Follow the POC README's secret-initialisation command.
3. Supply the OAuth identifier and secret from the password manager.
4. Ensure the command targets only:

   ```text
   src/FreeAgentSandboxPoc/FreeAgentSandboxPoc.csproj
   ```

5. Run the POC's configuration-check command.
6. Confirm it reports that credentials are present without printing their
   values.

Do not add these credentials to the user-secrets of the AdminWeb, Functions,
AppHost, or any other InvoiceManager project.

## 8. Authorise the POC Against the Sandbox

Perform this after the `auth login` command has been implemented:

1. Ensure you are signed out of production FreeAgent, or use a private browser
   window dedicated to the sandbox.
2. Run the POC's `auth login` command.
3. Check the printed authorisation URL before opening it. Its host must be:

   ```text
   api.sandbox.freeagent.com
   ```

4. Let the POC open the browser, or copy the URL into the sandbox-only browser
   window.
5. Sign in using the sandbox-company credentials from step 1.
6. On FreeAgent's consent screen, verify:
   - the app name is `InvoiceManager FreeAgent Sandbox POC`;
   - the company is the fictitious sandbox company.
7. Select **Allow access**.
8. FreeAgent should redirect to:

   ```text
   http://127.0.0.1:53682/callback/
   ```

9. Return to the terminal.
10. Confirm `auth login` reports:
    - successful code exchange;
    - the sandbox company name;
    - the authorised sandbox user;
    - token presence and expiry metadata without token values.
11. Run `auth status` in a new process to prove the stored refresh token enables
    unattended access.

Stop immediately if the consent page shows the real company or the URL uses
`api.freeagent.com` without `.sandbox`. Deny access and correct the POC
configuration.

## 9. Verify the Sandbox Is Ready for Scenario Seeding

Before `scenarios seed`:

1. Sign in to the sandbox UI.
2. Confirm the company is fictitious and disposable.
3. Confirm the synthetic bank account exists.
4. Confirm Guess is enabled on it.
5. Confirm there are no real contacts, bills, statements, or attachments.
6. Confirm no account lock is currently active unless deliberately created by
   the POC's reversible lock scenario.
7. Run `auth status` and confirm the expected sandbox company and user.
8. Run the read-only commands to list contacts, categories, and bank accounts.
9. Note the synthetic bank account's FreeAgent URL for comparison with the
   seeded fixture manifest.

The POC should create its own supplier, bills, attachments, and synthetic
transactions. Do not create them manually unless scenario seeding discovers a
sandbox API limitation and the findings explicitly call for a manual fixture.

## 10. Manual Action During the Guess Scenario

Normally the POC should import the synthetic statement and poll for a genuine
`marked_for_review` explanation. If FreeAgent creates the guess:

1. Open the synthetic bank account in the sandbox UI.
2. Confirm the matching transaction appears orange or under **For approval**.
3. Do not approve, edit, or delete it.
4. Return to the console and allow the POC to inspect it.
5. Before the destructive fallback experiment, review the console's exact
   proposed explanation deletion.
6. Confirm only if the bill reference and transaction belong to the current POC
   run.

If FreeAgent does not create a guess, do not manufacture success by approving a
manual explanation. Let the POC report
`BlockedBySandboxFixtureCapability`.

## 11. Cleanup

At the end of the POC:

1. Retain the generated fixture manifest and findings until results have been
   reviewed.
2. If sandbox fixtures should be removed, run the POC's guarded cleanup command
   and review its deletion plan before confirmation.
3. In the Developer Dashboard, open the POC app.
4. Revoke the OAuth secret or delete the app if the dashboard offers app
   deletion and the POC is finished.
5. Remove the POC project's user-secrets entry from the local machine.
6. Remove the entire repository subtree:

   ```text
   poc/freeagent-sandbox/
   ```

7. Allow the temporary sandbox company to expire, or delete it if FreeAgent
   provides that option.
8. Verify that no POC credential remains in a password manager unless retained
   with a clear expiry/revocation note.

Do not perform cleanup against the production FreeAgent company.

## Ready Checklist

- [ ] Temporary sandbox company created.
- [ ] All sandbox onboarding stages completed.
- [ ] Sandbox user confirmed as level 8.
- [ ] No real company or banking data entered.
- [ ] Synthetic business bank account created.
- [ ] Guess enabled before any synthetic transaction import.
- [ ] Developer account created.
- [ ] POC app registered.
- [ ] Exact loopback redirect URI saved.
- [ ] OAuth identifier and secret stored outside the repository.
- [ ] POC credentials added only to its isolated user-secrets store.
- [ ] `auth login` approved against the sandbox company.
- [ ] `auth status` succeeds from a new process.
- [ ] Sandbox identity rechecked before mutation scenarios.

## Official References

- [FreeAgent API quick start](https://dev.freeagent.com/docs/quick_start)
- [FreeAgent OAuth documentation](https://dev.freeagent.com/docs/oauth)
- [FreeAgent Developer Dashboard](https://dev.freeagent.com/)
- [FreeAgent user API and permission levels](https://dev.freeagent.com/docs/users)
- [How to enable Guess](https://support.freeagent.com/hc/en-us/articles/115001793224-How-to-enable-Guess)
- [How Guess explains transactions](https://support.freeagent.com/hc/en-us/articles/115001586004-How-does-Guess-explain-your-transactions)
- [FreeAgent client-secret rotation](https://dev.freeagent.com/docs/rotating_client_secrets)

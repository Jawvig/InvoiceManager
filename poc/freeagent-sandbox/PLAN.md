# FreeAgent Sandbox POC Plan

## Purpose

Build a disposable C#/.NET console application that proves how InvoiceManager
can authenticate to the FreeAgent sandbox and perform the bill operations needed
by the unattended workflow.

The POC is investigative rather than production-ready. It must:

- exercise each supported operation against the real FreeAgent sandbox;
- print enough sanitised request, response, and verification detail to explain
  every success or failure;
- distinguish an API limitation from an invalid request, insufficient
  permission, account state, and transport failure;
- avoid silently modifying or deleting an accounting object;
- record observed sandbox behaviour where the public documentation is
  ambiguous.

The POC should not yet be wired into `InvoiceManager.Core`, the Functions app,
Cosmos DB, Key Vault, or the production workflow. Reusable findings and API
models can subsequently inform
`src/InvoiceManager.Integrations.FreeAgent`.

## Isolation and Removal Boundary

The entire POC must live beneath one new directory:

```text
poc/freeagent-sandbox/
```

This directory is the POC's ownership boundary. It contains its plan, solution,
projects, tests, fixtures, scripts, configuration templates, generated reports,
and local ignore rules. Implementing or running the POC must not require changes
outside that directory.

In particular:

- do not add POC projects to `InvoiceManager.slnx`;
- do not add project references to or from any existing production or test
  project;
- do not reuse or modify `InvoiceManager.TestSupport`;
- do not add POC material to existing files under `docs/`;
- do not modify the repository `README.md`, root build configuration, CI
  workflows, Aspire AppHost, Functions app, Terraform, or deployment scripts;
- do not add POC settings to existing application configuration or user-secret
  stores owned by another project;
- do not place POC artifacts in existing build, test, coverage, or deployment
  paths;
- do not introduce shared NuGet version or build-property changes for the POC;
- do not make normal repository build or test commands discover or run the POC
  implicitly.

The POC may mirror small patterns from the main repository, but it should copy
the minimum necessary code into its own namespace rather than couple to an
existing project. Any later promotion into production is a separate, reviewed
implementation task.

The isolation acceptance test is:

1. record `git status --short` before POC implementation;
2. implement and run the POC;
3. verify that every POC-owned path is beneath `poc/freeagent-sandbox/`;
4. verify the main solution builds and tests without the POC;
5. verify the POC builds and tests through its own solution;
6. demonstrate that removing `poc/freeagent-sandbox/` leaves no dangling
   project, solution, documentation, CI, configuration, or deployment
   references.

## Clarifications and Assumptions

### Bill date, not bill-item date

FreeAgent bill items have amounts, categories, descriptions, tax fields, and
quantities, but no date. `dated_on` and `due_on` are properties of the parent
bill. Therefore, the requested “change the date on an open bill item” scenario
will change the open **bill's** `dated_on` value and verify that all its items
remain attached to the same bill.

### Search “range”

The phrase “date range and/or contact and/or range” is interpreted as:

- bill-date range;
- contact;
- total-amount range.

The CLI should also support an optional reference substring because it is a
useful disambiguator. If the second “range” meant a different field, only the
search criteria and local filtering step need to change.

FreeAgent supports date and contact filters on the Bills endpoint. It does not
document a server-side amount-range or reference filter, so those filters will
be applied locally after retrieving all paginated results matching the
server-side filters.

### Bill name and contact

A bill exposes a `reference` and a contact URL, but not an embedded supplier
name. The POC will present:

- `reference` as the bill name/reference;
- a separately resolved contact display name, preferring
  `organisation_name`, then `first_name + last_name`;
- the contact URL as its stable FreeAgent identity.

### Auto-suggested payments

An unapproved FreeAgent bank guess is represented by a bank transaction
explanation whose `marked_for_review` property is `true`. Bill endpoints require
level 5 (“Bills”), but bank transactions and explanations require level 6
(“Banking”). The suggested-payment scenario consequently requires a level-6
sandbox user.

The POC must first observe whether a bill update succeeds while the suggestion
exists. It must not assume that the suggestion has the same locking behaviour as
an approved payment. Deleting a suggestion is a separately confirmed fallback,
not an automatic side effect.

## Proposed Project

Create a self-contained solution:

```text
poc/
  freeagent-sandbox/
    PLAN.md
    MANUAL-SETUP.md
    README.md
    FINDINGS.md
    FreeAgentSandboxPoc.slnx
    Directory.Build.props
    .gitignore
    src/
      FreeAgentSandboxPoc/
        FreeAgentSandboxPoc.csproj
        Program.cs
        Commands/
        FreeAgent/
          FreeAgentClient.cs
          FreeAgentModels.cs
          OAuthClient.cs
          TokenStore.cs
          ApiResult.cs
        Output/
          ConsoleResultWriter.cs
    tests/
      FreeAgentSandboxPoc.Tests/
        FreeAgentSandboxPoc.Tests.csproj
    fixtures/
      sample-invoice.pdf
    artifacts/
      .gitkeep
```

Use the repository's current local target, `net11.0`, with nullable reference
types and implicit usings enabled. Prefer the .NET BCL and
`Microsoft.Extensions.*` packages already familiar to the solution. A small CLI
library may be used only if it materially improves validation and help output.

The POC owns its `Directory.Build.props` and package versions. Its projects are
included only in `FreeAgentSandboxPoc.slnx`. Commands in its README must use that
solution or explicit POC project paths so they cannot accidentally operate on
the main solution.

`HttpClient` should be injected into a typed `FreeAgentClient`. JSON contracts
should use `System.Text.Json` with explicit property names. Keep wire DTOs
separate from command results so FreeAgent-specific shapes do not leak into
future core workflow contracts.

## Configuration and Secrets

Non-secret settings:

```text
FreeAgentPoc:ApiBaseUrl=https://api.sandbox.freeagent.com/v2/
FreeAgentPoc:AuthorizationEndpoint=https://api.sandbox.freeagent.com/v2/approve_app
FreeAgentPoc:TokenEndpoint=https://api.sandbox.freeagent.com/v2/token_endpoint
FreeAgentPoc:RedirectUri=http://127.0.0.1:53682/callback/
FreeAgentPoc:UserAgent=InvoiceManager-FreeAgent-Poc/<version>
```

Secrets:

- OAuth client ID;
- OAuth client secret;
- latest refresh token.

Use .NET user-secrets attached only to the POC console project's unique
`UserSecretsId` for the client ID and secret. Store the refresh token in that
same POC-owned secret store, replacing it immediately whenever FreeAgent rotates
it. Never add these values to an existing InvoiceManager project's user-secrets,
commit tokens, or put them in command history.

The token update should be crash-conscious:

1. parse and validate the refresh response;
2. save the new refresh token;
3. only then expose the new access token to the calling command.

If a refresh succeeds but saving the replacement token fails, stop the command
and print a high-severity local-secret-storage failure. Do not continue issuing
API calls with an unreliably persisted token.

## OAuth Console Flow

Implement these commands:

```text
auth login
auth status
auth forget
```

`auth login` will:

1. generate and retain a cryptographically random OAuth `state`;
2. start a temporary loopback callback listener;
3. print the authorisation URL and attempt to open it in the default browser;
4. accept one callback only;
5. validate `state` and reject OAuth error callbacks;
6. exchange the code for access and refresh tokens;
7. save only the refresh token;
8. call the personal-profile and company endpoints to show which sandbox user
   and company authorised the application.

The redirect URI must exactly match the application registration. If the
FreeAgent developer dashboard rejects an HTTP loopback URI, switch the listener
to ASP.NET Core/Kestrel on `https://localhost` with the .NET development
certificate and register that exact URI.

`auth status` must refresh if necessary and perform a harmless identity/company
request. It prints identities and expiry metadata but never token values.

There is no unattended client-credentials flow. The one-time interactive login
bootstraps the refresh token that subsequent console runs use unattended.

## CLI Commands

The initial command surface should be:

```text
auth login|status|forget
contacts list
categories list
bank-accounts list

bills find [--from DATE] [--to DATE] [--contact URL_OR_ID]
           [--min-total DECIMAL] [--max-total DECIMAL]
           [--reference TEXT] [--view VIEW]
bills show --bill URL_OR_ID
bills create ...
bills attach --bill URL_OR_ID --file PATH [--description TEXT]
bills set-date --bill URL_OR_ID --date DATE
bills set-amount --bill URL_OR_ID --item URL_OR_ID --amount DECIMAL

payments inspect-suggestion --bill URL_OR_ID
payments change-bill-amount-with-suggestion
         --bill URL_OR_ID --item URL_OR_ID --amount DECIMAL
         [--allow-delete-suggestion]

scenarios seed
scenarios run-all --fixture-manifest PATH
scenarios cleanup --fixture-manifest PATH
```

All mutating commands should print a proposed before/after diff and require
`--confirm`. `scenarios run-all` may imply confirmation only for objects whose
URLs are present in the POC-created fixture manifest and whose references carry
the unique POC run ID. It must refuse to mutate any other object.

## API Operations and Scenarios

### 1. Find bills

Request:

```text
GET /bills?from_date=...&to_date=...&contact=...&view=...
```

Follow pagination until exhausted. Apply amount range and reference matching
locally. Test:

1. date only;
2. contact only;
3. amount range only;
4. date + contact;
5. date + contact + amount range + reference;
6. no matches;
7. multiple matches;
8. invalid range (`from > to`, `min > max`);
9. an invalid contact URI;
10. pagination with a deliberately small page size if supported by the API.

Output must state which filters were server-side and which were local. Never
silently choose one bill when several match.

### 2. Read bill details and payment state

Call `GET /bills/:id`, then resolve its contact URL with
`GET /contacts/:id`.

Print:

- bill URL and reference;
- bill date and due date;
- currency, total, paid, and due values;
- `status`, `long_status`, and `paid_on` when present;
- `isFullyPaid`, derived primarily from the documented status (`Paid` for a
  normal positive bill, `Refunded` for a bill refund) and cross-checked against
  paid/due values;
- contact URL, organisation name, person name, and chosen display name;
- bill items, including URL, description, category, tax data, and totals;
- existing attachment metadata without downloading the file.

If status and numeric values disagree, return an `InconsistentRemoteState`
result rather than guessing.

### 3. Create a bill

Create a POC fixture with:

- a known sandbox supplier contact;
- a unique reference containing the POC run ID;
- bill date and due date;
- native sandbox currency;
- one ordinary spending-category bill item;
- an unambiguous decimal total and VAT treatment.

Call `POST /bills`, require `201 Created`, capture the `Location`, then GET the
created bill and compare every meaningful field. Record FreeAgent-generated
values separately from submitted values.

Negative probes:

- missing contact;
- missing reference;
- invalid date or due date;
- invalid category;
- invalid decimal;
- more than the documented maximum of 40 items.

Negative probes should create separate requests and must not prevent the valid
fixture from being retained for later scenarios.

### 4. Attach an invoice

Generate or commit a tiny non-sensitive test PDF fixture. Read it as bytes,
base64 encode it, and update the bill's `attachment` field through
`PUT /bills/:id`.

Verify with a fresh GET that attachment URL, filename, type, description, and
size are present.

Probe:

1. attach to an open bill with no attachment;
2. attach to a paid bill;
3. attach when an attachment already exists;
4. delete the attachment via `DELETE /attachments/:id`, verify removal, and
   reattach;
5. a file larger than 5 MB;
6. an unsupported content type;
7. invalid base64 sent by a low-level diagnostic command.

The “already attached” probe is important: the singular API field does not make
replacement behaviour sufficiently explicit. Record whether FreeAgent rejects,
replaces, or otherwise handles the second upload.

### 5. Change an open bill's date

Preconditions:

- fixture belongs to the current POC run;
- status is `Open` or `Overdue`;
- new date differs from the old date;
- new date is not within an account lock.

Call:

```text
PUT /bills/:id
{
  "bill": {
    "dated_on": "YYYY-MM-DD"
  }
}
```

GET the bill again and verify:

- `dated_on` changed;
- `due_on` behaviour is observed rather than assumed;
- bill-item URLs and values did not change;
- status and totals remain coherent.

Also probe an invalid date and a date inside an account-locked period if the
sandbox user has level 8 and a reversible test lock can safely be created. The
lock probe must save the prior lock state and restore it in `finally`. Otherwise
report it as `NotRun: RequiresFullAccess`, not as a pass.

### 6. Change an open bill's amount

The bill total is calculated from its bill items. Require an explicit item URL;
do not guess which item to change on a multi-item bill.

Call:

```text
PUT /bills/:id
{
  "bill": {
    "bill_items": [
      {
        "url": "<bill-item-url>",
        "total_value": "123.45"
      }
    ]
  }
}
```

GET and verify the item total, bill total, tax values, paid value, due value, and
status. Test both inclusive (`total_value`) and exclusive
(`total_value_ex_tax`) entry on separate fixtures so the POC records rounding
and VAT behaviour.

Negative probes:

- item belongs to another bill;
- zero and negative amount;
- excessive decimal precision;
- paid bill;
- locked bill;
- multi-item bill without `--item`.

### 7. Change an amount with an unapproved auto-suggested payment

This is a level-6 scenario.

Fixture setup should first attempt to create or identify a bank transaction and
guessed Bill Payment explanation with:

- `paid_bill` equal to the fixture bill URL;
- `marked_for_review = true`;
- `is_deletable = true`;
- `is_locked = false`.

If the sandbox does not allow creation of a genuine guessed explanation through
the API and does not generate one through its guessing feature, stop this
scenario as `BlockedBySandboxFixtureCapability` with the observed response. Do
not substitute an approved payment and claim equivalent behaviour.

The experiment has two stages:

1. **Direct update probe**
   - GET the bill and explanation.
   - Assert the explanation is still marked for review.
   - Attempt the bill-item amount update without modifying the explanation.
   - Capture the complete sanitised response.
   - GET both resources again, even when the PUT fails, to detect partial or
     unexpected state changes.

2. **Explicit fallback probe**
   - Run only with `--allow-delete-suggestion --confirm`.
   - Require exactly one matching explanation.
   - Require `marked_for_review`, `is_deletable`, and not `is_locked`.
   - DELETE the explanation.
   - Verify that the bank transaction becomes unexplained as expected and the
     bill is editable.
   - Update the bill item.
   - Verify the new amount and the state of the original bank transaction.

The POC must not automatically recreate or approve a payment explanation.
Whether the production workflow should leave the transaction unexplained for
FreeAgent to re-guess, create a new marked-for-review explanation, or stop for
human review is a later accounting-policy decision based on the observed
sandbox behaviour.

### 8. Paid-bill behaviour

Create a separate bill and an approved Bill Payment explanation. Prove:

- the bill reports `Paid`;
- adding an attachment remains possible;
- changing the amount directly either fails or behaves as observed;
- removing a payment requires level 6 and an explicit delete;
- the POC never removes an approved payment unless a dedicated destructive
  scenario is confirmed.

This scenario must remain separate from the unapproved suggestion scenario.

## Response and Failure Output

Each command should print a human summary followed by a machine-readable JSON
result. Use a common envelope:

```json
{
  "command": "bills set-amount",
  "outcome": "RejectedByFreeAgent",
  "exitCode": 20,
  "operationId": "local-guid",
  "request": {
    "method": "PUT",
    "uri": "https://api.sandbox.freeagent.com/v2/bills/123",
    "bodySummary": "bill item 456 total_value -> 123.45"
  },
  "response": {
    "status": 422,
    "reason": "Unprocessable Entity",
    "headers": {},
    "freeAgentError": {},
    "rawBody": "sanitised response body"
  },
  "verification": {
    "attempted": true,
    "remoteStateChanged": false
  }
}
```

Do not assume exact failure status codes in the test plan. Record what the
sandbox actually returns.

Outcome taxonomy:

- `Succeeded`;
- `ValidationFailed` — rejected locally before an API call;
- `AuthenticationFailed`;
- `PermissionDenied`;
- `NotFound`;
- `AmbiguousMatch`;
- `PreconditionFailed`;
- `RejectedByFreeAgent`;
- `RateLimited`;
- `TransportFailed`;
- `DeserialisationFailed`;
- `InconsistentRemoteState`;
- `VerificationFailed`;
- `BlockedBySandboxFixtureCapability`;
- `NotRun`.

Suggested process exit codes:

```text
0  success
2  local validation or usage error
10 authentication failure
11 permission failure
12 not found
13 ambiguous match
14 failed mutation precondition
20 FreeAgent rejected the request
21 rate limited
30 transport failure
31 response parse failure
40 verification or inconsistent-state failure
50 scenario could not be constructed in the sandbox
```

For every API failure, print:

- HTTP method and sanitised URI;
- response status and reason;
- `Retry-After` and rate-limit headers when present;
- FreeAgent's parsed error object when possible;
- a bounded, sanitised raw response body when parsing fails;
- whether a verification GET was attempted;
- before and after resource summaries for mutation attempts.

Never print:

- `Authorization`;
- client secret;
- access or refresh token;
- OAuth authorisation code;
- attachment base64;
- contact email, postal address, tax identifier, or other unnecessary personal
  data.

Use invariant-culture decimal handling and ISO `yyyy-MM-dd` dates throughout.

## Automated Tests

Add a POC-local unit-test project for everything that does not require the
sandbox:

```text
poc/
  freeagent-sandbox/
    tests/
      FreeAgentSandboxPoc.Tests/
```

Cover:

- OAuth URL construction and `state` validation;
- rotating refresh-token persistence;
- secret redaction;
- problem/error response parsing;
- pagination;
- date/contact/amount/reference filtering;
- contact display-name selection;
- paid-state derivation and inconsistent states;
- mutation preconditions;
- bill-item ownership checks;
- before/after verification;
- exit-code mapping;
- invariant decimal and ISO date serialisation.

Create a small POC-local stub `HttpMessageHandler`; do not reference, promote,
or modify the repository's `InvoiceManager.TestSupport`. Unit tests must not
require real FreeAgent credentials.

Live sandbox scenarios should be explicitly marked and excluded from the POC's
ordinary unit-test command. The POC is not added to the repository's normal CI
test run. The console's `scenarios run-all` command is the auditable live POC
runner and should emit a timestamped JSON report under
`poc/freeagent-sandbox/artifacts/`, ignored by the POC-local `.gitignore`.

## Sandbox Fixture Strategy

`scenarios seed` should create or select:

- one supplier contact;
- one ordinary spending category;
- an open single-item bill;
- an open multi-item bill;
- a bill reserved for attachment replacement;
- a paid bill;
- a bill with a marked-for-review guessed payment, when sandbox capabilities
  allow it;
- a small PDF and invalid attachment fixtures.

Write a local fixture manifest beneath the POC's ignored `artifacts/` directory
containing resource URLs, original values, run ID, and resources created by the
POC. Do not store tokens or unnecessary contact data in it.

Cleanup should:

- operate only on URLs in the manifest;
- show the deletion plan;
- require confirmation;
- delete in dependency order;
- tolerate already-deleted fixtures;
- report anything that could not be restored or removed.

Because cleanup is destructive, retaining the sandbox fixtures for manual
inspection should be the default after `run-all`.

## Negative and Resilience Matrix

The live run should deliberately capture:

| Condition | How it is exercised | Expected classification |
|---|---|---|
| Bad local input | Invalid date/range/decimal | `ValidationFailed` |
| Expired access token | Allow normal refresh path | Transparent refresh, then success |
| Invalid refresh token | Use an in-memory altered value, never overwrite the stored token | `AuthenticationFailed` |
| Insufficient level 5 access | Only when a lower-permission sandbox user is available | `PermissionDenied` |
| Banking call with level 5 only | Only when such a user is available | `PermissionDenied` |
| Missing bill/contact/item | Known nonexistent IDs | `NotFound` |
| Several matching bills | Broad fixture search | `AmbiguousMatch` |
| Invalid FreeAgent payload | Isolated diagnostic probes | `RejectedByFreeAgent` |
| Rate limit | Sandbox `X-RateLimit-Test` facility, bounded requests | `RateLimited`, honour `Retry-After` |
| Timeout/network failure | Stubbed unit test; do not disrupt the machine network | `TransportFailed` |
| Malformed response | Stubbed unit test | `DeserialisationFailed` |
| Mutation response lies or is stale | Verification GET mismatch in a stubbed test | `VerificationFailed` |
| Locked resource | Reversible level-8 sandbox fixture only | Observed rejection or `NotRun` |
| Paid bill edit | Dedicated paid fixture | Observed result, no assumptions |
| Existing attachment | Dedicated fixture | Observed replace/reject semantics |
| Auto-guess cannot be constructed | Retain actual API response | `BlockedBySandboxFixtureCapability` |

## Delivery Phases

### Phase 1 — Registration and skeleton

1. Follow `MANUAL-SETUP.md` to create a FreeAgent sandbox account and sandbox
   app registration.
2. Register the exact loopback redirect URI.
3. Create the isolated `poc/freeagent-sandbox/` directory, its own solution, and
   the console and unit-test projects.
4. Implement validated configuration, user-secrets, common output, and redaction.
5. Add a boundary check that fails if the POC implementation has modified or
   created a path outside `poc/freeagent-sandbox/`.

Exit criterion: the CLI starts, validates configuration, and cannot leak a
seeded fake secret in tests; the main `InvoiceManager.slnx` does not reference
either POC project.

### Phase 2 — OAuth and read-only discovery

1. Implement login, callback, code exchange, refresh, rotation, and identity
   verification.
2. Implement generic API result/error handling and pagination.
3. Implement contacts, categories, bank accounts, bill search, and bill detail.

Exit criterion: a fresh process can use the stored refresh token to find and
display sandbox bills without interactive login.

### Phase 3 — Basic bill mutations

1. Seed POC contacts/categories/bills.
2. Create bills.
3. Change open-bill dates.
4. Change open-bill item amounts.
5. Attach, inspect, delete, and reattach test PDFs.
6. Verify every mutation with a subsequent GET.

Exit criterion: all basic mutations have before/after evidence and all negative
probes have classified, sanitised responses.

### Phase 4 — Payment-state experiments

1. Create and inspect an approved paid-bill fixture.
2. Attempt paid-bill attachment and amount scenarios.
3. Construct or locate a genuine `marked_for_review` guessed Bill Payment.
4. Run direct-update and explicitly confirmed delete-suggestion fallback probes.

Exit criterion: the final report clearly distinguishes approved-payment and
unapproved-guess behaviour, or documents why the sandbox cannot create the
required guess.

### Phase 5 — Report and production recommendations

Produce a checked-in `poc/freeagent-sandbox/FINDINGS.md` containing:

- tested sandbox date and FreeAgent API behaviour;
- actual HTTP statuses and representative sanitised error shapes;
- required access level for each operation;
- attachment replacement behaviour;
- paid and marked-for-review update behaviour;
- VAT/rounding observations;
- production-safe operation sequence;
- unresolved questions to take to FreeAgent or the accountants.

Only after those findings should the production
`InvoiceManager.Integrations.FreeAgent` contract and workflow-state changes be
designed.

### Phase 6 — Isolation and removal proof

1. Run the POC build and unit tests through `FreeAgentSandboxPoc.slnx`.
2. Run the main solution's standard non-integration build and test commands
   independently.
3. Search the repository outside `poc/freeagent-sandbox/` for
   `FreeAgentSandboxPoc`, its project IDs, user-secrets ID, and POC paths; expect
   no matches.
4. Confirm `git diff --name-only` contains no POC-caused modification outside
   the isolated subtree.
5. Document the single repository-removal operation in the POC README: remove
   `poc/freeagent-sandbox/`, then separately remove that POC project's local
   user-secrets entry.

Exit criterion: there are no build-time, run-time, documentation, test, CI,
deployment, or configuration references to the POC outside its directory.

## POC Acceptance Criteria

The POC is complete when:

- OAuth can be bootstrapped interactively and then reused unattended;
- refresh-token rotation is persisted and tested;
- bills can be searched by every combination of date, contact, amount range,
  and optional reference;
- pagination is proven;
- bill reference, dates, totals, currency, payment status, resolved supplier
  name, contact identity, items, and attachment metadata are displayed;
- a new bill is created and verified;
- an open bill's parent date is changed and verified;
- a specific open bill item's amount is changed and the recalculated bill total
  is verified;
- a PDF is attached to an existing bill and verified;
- existing-attachment behaviour is observed;
- paid-bill attachment and amount behaviour are observed separately;
- a genuine unapproved auto-suggested payment is tested, or the sandbox
  limitation is evidenced;
- every failure has a classified sanitised response and non-zero exit code;
- no secret or attachment body appears in output or reports;
- no mutation can target a non-POC object without explicit confirmation;
- live test resources are either retained with a manifest or explicitly cleaned
  up with a complete report;
- every POC-owned repository change is beneath `poc/freeagent-sandbox/`;
- the main solution neither references nor implicitly builds/tests the POC;
- deleting the isolated directory leaves the repository buildable with no
  dangling references.

## Official References

- [FreeAgent OAuth](https://dev.freeagent.com/docs/oauth)
- [Bills](https://dev.freeagent.com/docs/bills)
- [Contacts](https://dev.freeagent.com/docs/contacts)
- [Attachments](https://dev.freeagent.com/docs/attachments)
- [Bank transactions](https://dev.freeagent.com/docs/bank_transactions)
- [Bank transaction explanations](https://dev.freeagent.com/docs/bank_transaction_explanations)
- [Account locks](https://dev.freeagent.com/docs/account_locks)
- [API introduction, access levels, pagination, and rate limits](https://dev.freeagent.com/docs/introduction)

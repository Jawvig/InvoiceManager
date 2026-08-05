// Progressive-disclosure wizard for the Create/Edit invoice configuration form, plus lazy
// loading of billing accounts. See wwwroot/js/onedrive-picker.js for the OneDrive folder picker,
// which is independent of this file.
(function () {
    "use strict";
    const wizard = document.getElementById("configuration-wizard");
    if (!wizard) return;

    const isEdit = wizard.dataset.isEdit === "true";
    // Set when the server already rendered a chosen integration type without the user picking
    // it in this page load — an Import file pre-fill, or a failed Create postback being
    // re-rendered with the user's earlier choice still in Input.IntegrationType. Either way the
    // fields for that integration should already be visible, not hidden pending a fresh "change".
    const isPrefilled = wizard.dataset.prefilled === "true";
    const integrationSelect = document.getElementById("Input_IntegrationType");
    const idField = document.getElementById("configuration-id-field");
    const commonDetails = document.getElementById("common-details");
    const billingDetails = document.getElementById("billing-details");
    const emailDetails = document.getElementById("email-details");
    const billingSelect = document.getElementById("Input_BillingAccountId");
    const billingStatus = document.getElementById("billing-account-status");
    const hasExpectedAmountCheckbox = document.getElementById("Input_HasExpectedAmount");
    const amountMatchingFields = document.getElementById("amount-matching-fields");
    const bodyPatternInput = document.getElementById("Input_BodyPattern");
    const bodyPatternStatus = document.getElementById("body-pattern-status");
    const hasFreeAgentMatchingCheckbox = document.getElementById("Input_HasFreeAgentMatching");
    const freeAgentMatchingFields = document.getElementById("freeagent-matching-fields");
    const hasFreeAgentDateReconciliationCheckbox = document.getElementById("Input_HasFreeAgentDateReconciliation");
    const freeAgentDateFields = document.getElementById("freeagent-date-fields");
    const hasFreeAgentAmountReconciliationCheckbox = document.getElementById("Input_HasFreeAgentAmountReconciliation");
    const freeAgentAmountFields = document.getElementById("freeagent-amount-fields");

    let billingAccountsRequested = false;

    function buildHandlerUrl(handler, params) {
        const url = new URL(window.location.href);
        url.searchParams.set("handler", handler);
        if (params) {
            for (const [key, value] of Object.entries(params)) {
                if (value !== null && value !== undefined) url.searchParams.set(key, value);
            }
        }
        return url.toString();
    }

    // Input.IntegrationType defaults to MicrosoftBilling server-side, so on Create the <select>
    // already has a real (truthy) value before the user has touched it — "value is set" is NOT a
    // reliable signal that the user chose an integration. Only an explicit change event (or Edit
    // mode, where the integration is already fixed and known) counts as a real choice.
    function applyVisibility(userChoseIntegration) {
        const value = integrationSelect ? integrationSelect.value : "";
        // On Edit, integration type is immutable and everything is already rendered visible
        // server-side (see _ConfigurationForm.cshtml's hidden="@(!Model.IsEdit)" bindings) — the
        // "hide until selected" behavior below is Create-only.
        if (!isEdit) {
            if (idField) idField.hidden = !userChoseIntegration;
            if (commonDetails) commonDetails.hidden = !userChoseIntegration;
            if (billingDetails) billingDetails.hidden = !userChoseIntegration || value !== "MicrosoftBilling";
            if (emailDetails) emailDetails.hidden = !userChoseIntegration || value !== "GraphEmail";
        }
        if (userChoseIntegration && value === "MicrosoftBilling" && !billingAccountsRequested) {
            loadBillingAccounts();
        }
    }

    async function loadBillingAccounts() {
        if (!billingSelect) return;
        billingAccountsRequested = true;
        const previouslySelected = billingSelect.value;
        setBillingStatus("Loading billing accounts…");
        try {
            const response = await fetch(buildHandlerUrl("BillingAccounts"), { headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error(`Request failed with status ${response.status}`);
            const accounts = await response.json();
            const hasSelected = accounts.some(a => a.id === previouslySelected);

            billingSelect.innerHTML = "";
            if (previouslySelected && !hasSelected) {
                billingSelect.appendChild(makeOption(previouslySelected, previouslySelected));
            }
            for (const account of accounts) {
                const label = account.displayName ? `${account.displayName} (${account.id})` : account.id;
                billingSelect.appendChild(makeOption(account.id, label));
            }
            billingSelect.value = previouslySelected;

            setBillingStatus(previouslySelected && !hasSelected
                ? "This account may no longer be available."
                : "");
        } catch {
            billingAccountsRequested = false;
            setBillingStatus("");
            if (billingStatus) {
                const retry = document.createElement("button");
                retry.type = "button";
                retry.className = "secondary-action";
                retry.textContent = "Retry loading billing accounts";
                retry.addEventListener("click", () => loadBillingAccounts());
                billingStatus.appendChild(retry);
            }
        }
    }

    function makeOption(value, label) {
        const option = document.createElement("option");
        option.value = value;
        option.textContent = label;
        return option;
    }

    function setBillingStatus(text) {
        if (!billingStatus) return;
        billingStatus.innerHTML = "";
        if (text) billingStatus.textContent = text;
    }

    integrationSelect?.addEventListener("change", () => applyVisibility(true));
    applyVisibility(isEdit || isPrefilled);

    // Expected amount / currency / amount tolerance are only meaningful (and only validated
    // server-side) when "Match expected amount" is checked — hide them otherwise so an
    // unchecked box can't be paired with stray leftover values.
    function applyAmountMatchingVisibility() {
        if (amountMatchingFields) amountMatchingFields.hidden = !hasExpectedAmountCheckbox?.checked;
    }
    hasExpectedAmountCheckbox?.addEventListener("change", applyAmountMatchingVisibility);
    applyAmountMatchingVisibility();

    // Same progressive-disclosure treatment for FreeAgent bill matching: the contact URL and
    // each reconciliation's tolerance are only meaningful (and only validated server-side) when
    // their enabling checkbox is checked.
    function applyFreeAgentMatchingVisibility() {
        if (freeAgentMatchingFields) freeAgentMatchingFields.hidden = !hasFreeAgentMatchingCheckbox?.checked;
    }
    function applyFreeAgentDateReconciliationVisibility() {
        if (freeAgentDateFields) freeAgentDateFields.hidden = !hasFreeAgentDateReconciliationCheckbox?.checked;
    }
    function applyFreeAgentAmountReconciliationVisibility() {
        if (freeAgentAmountFields) freeAgentAmountFields.hidden = !hasFreeAgentAmountReconciliationCheckbox?.checked;
    }
    hasFreeAgentMatchingCheckbox?.addEventListener("change", applyFreeAgentMatchingVisibility);
    hasFreeAgentDateReconciliationCheckbox?.addEventListener("change", applyFreeAgentDateReconciliationVisibility);
    hasFreeAgentAmountReconciliationCheckbox?.addEventListener("change", applyFreeAgentAmountReconciliationVisibility);
    applyFreeAgentMatchingVisibility();
    applyFreeAgentDateReconciliationVisibility();
    applyFreeAgentAmountReconciliationVisibility();

    // Give immediate feedback on an invalid body-pattern regex rather than waiting for a
    // server round-trip on Save. JavaScript's RegExp syntax isn't identical to .NET's, so this
    // is a best-effort early warning only — the server-side check in
    // InvoiceConfigurationValidation.Validate remains authoritative.
    function validateBodyPattern() {
        if (!bodyPatternInput || !bodyPatternStatus) return;
        if (!bodyPatternInput.value) {
            bodyPatternStatus.textContent = "";
            return;
        }
        try {
            new RegExp(bodyPatternInput.value);
            bodyPatternStatus.textContent = "";
        } catch {
            bodyPatternStatus.textContent = "This does not look like a valid regular expression.";
        }
    }
    bodyPatternInput?.addEventListener("input", validateBodyPattern);
    validateBodyPattern();

    window.InvoiceManagerConfigurationWizard = { buildHandlerUrl };
})();

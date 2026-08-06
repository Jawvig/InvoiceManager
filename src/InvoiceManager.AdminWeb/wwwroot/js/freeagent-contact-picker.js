// Live search picker for a FreeAgent contact. FreeAgent's contacts endpoint has no free-text
// search parameter, so OnGetFreeAgentContactsAsync pages through contacts and filters
// server-side; this file only debounces keystrokes and renders whatever comes back.
(function () {
    "use strict";
    const openButton = document.getElementById("freeagent-contact-picker-open");
    if (!openButton) return;

    const dialog = document.getElementById("freeagent-contact-picker-dialog");
    const closeButton = document.getElementById("freeagent-contact-picker-close");
    const cancelButton = document.getElementById("freeagent-contact-picker-cancel");
    const queryInput = document.getElementById("freeagent-contact-picker-query");
    const statusEl = document.getElementById("freeagent-contact-picker-status");
    const listEl = document.getElementById("freeagent-contact-picker-list");
    const summaryEl = document.getElementById("freeagent-contact-picker-summary");

    const urlInput = document.getElementById("Input_FreeAgentContactUrl");
    const displayNameInput = document.getElementById("Input_FreeAgentContactDisplayName");

    const buildHandlerUrl = window.InvoiceManagerConfigurationWizard?.buildHandlerUrl
        ?? function (handler, params) {
            const url = new URL(window.location.href);
            url.searchParams.set("handler", handler);
            if (params) for (const [key, value] of Object.entries(params)) {
                if (value !== null && value !== undefined) url.searchParams.set(key, value);
            }
            return url.toString();
        };

    const MIN_QUERY_LENGTH = 3;
    const DEBOUNCE_MS = 300;
    let debounceHandle = null;
    let searchToken = 0;

    openButton.addEventListener("click", openPicker);
    closeButton?.addEventListener("click", closePicker);
    cancelButton?.addEventListener("click", closePicker);
    dialog?.addEventListener("cancel", closePicker);
    queryInput?.addEventListener("input", onQueryInput);

    function openPicker() {
        queryInput.value = "";
        listEl.innerHTML = "";
        setStatus("Type at least 3 characters to search.");
        if (typeof dialog.showModal === "function") dialog.showModal();
        else dialog.setAttribute("open", "open");
        queryInput.focus();
    }

    function closePicker() {
        if (debounceHandle) clearTimeout(debounceHandle);
        if (typeof dialog.close === "function") dialog.close();
        else dialog.removeAttribute("open");
    }

    function onQueryInput() {
        if (debounceHandle) clearTimeout(debounceHandle);
        const query = queryInput.value.trim();
        if (query.length < MIN_QUERY_LENGTH) {
            // Invalidate any in-flight search too, or its response could still land after this
            // point (the token check in search() would otherwise accept it) and repopulate the
            // list with results for a query the box no longer shows.
            searchToken++;
            listEl.innerHTML = "";
            setStatus("Type at least 3 characters to search.");
            return;
        }
        debounceHandle = setTimeout(() => search(query), DEBOUNCE_MS);
    }

    async function search(query) {
        const token = ++searchToken;
        setStatus("Searching…");
        try {
            const response = await fetch(
                buildHandlerUrl("FreeAgentContacts", { query }), { headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error(await describeFailedResponse(response));
            const contacts = await response.json();
            if (token !== searchToken) return; // a later keystroke's search has already superseded this one

            listEl.innerHTML = "";
            setStatus(contacts.length ? "" : "No matching contacts were found.");
            for (const contact of contacts) {
                listEl.appendChild(makeRow(contact));
            }
        } catch (err) {
            if (token !== searchToken) return;
            // Deliberately distinct, in wording and styling, from the "no matches" case above -
            // this is a system error (FreeAgent unreachable, rejected the request, ...), not a
            // legitimate empty result, and the administrator needs to be able to tell the two
            // apart and see enough detail to diagnose or report it.
            setStatus(`FreeAgent search failed: ${err.message}`, /* isError */ true);
            appendRetry(() => search(query));
        }
    }

    // OnGetFreeAgentContactsAsync returns a JSON body of the shape { error: "..." } (its
    // sanitized ex.Message - see EnsureSuccessAsync, which never includes FreeAgent's raw
    // response body) whenever the search itself failed rather than just finding zero matches.
    async function describeFailedResponse(response) {
        try {
            const body = await response.json();
            if (body && typeof body.error === "string" && body.error) return body.error;
        } catch {
            // Body wasn't JSON (or was empty) - fall through to the status-based description.
        }
        return `request failed with status ${response.status}`;
    }

    function makeRow(contact) {
        const item = document.createElement("li");
        const button = document.createElement("button");
        button.type = "button";
        button.className = "freeagent-contact-picker-row";
        button.textContent = contact.displayName;
        button.addEventListener("click", () => {
            urlInput.value = contact.url;
            displayNameInput.value = contact.displayName;
            if (summaryEl) summaryEl.textContent = contact.displayName;
            closePicker();
        });
        item.appendChild(button);
        return item;
    }

    function setStatus(text, isError) {
        statusEl.textContent = text || "";
        statusEl.classList.toggle("status-error", Boolean(isError));
    }

    function appendRetry(retryFn) {
        const retry = document.createElement("button");
        retry.type = "button";
        retry.className = "secondary-action";
        retry.textContent = "Retry";
        retry.addEventListener("click", retryFn);
        statusEl.appendChild(document.createElement("br"));
        statusEl.appendChild(retry);
    }
})();

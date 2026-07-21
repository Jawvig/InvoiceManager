// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Configuration ID (slug) generation. Mirrors InvoiceConfigurationValidation.GenerateSlug exactly:
// NFD-normalize, strip combining marks, lowercase, collapse non-alphanumeric runs to "-", trim
// "-", fall back to "invoice" if empty, and use "{integrationType} invoice" as the source text
// when the description is blank.
//
// Regeneration is triggered ONLY by:
//   - "input" on #Input_InvoiceDescription
//   - "change" on #Input_IntegrationType
// and ONLY while the ID field has not been hand-edited by the user (dataset.edited !== "true").
// It must never run on page load and never on any other field's input/change.
function regenerateConfigurationSlug() {
    const description = document.querySelector("#Input_InvoiceDescription");
    const id = document.querySelector("#Input_Id:not([readonly])");
    const integration = document.querySelector("#Input_IntegrationType");
    if (!id || id.dataset.edited === "true") return;
    const source = description?.value.trim() || `${(integration?.value || "invoice").toLowerCase()} invoice`;
    id.value = source.toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "")
        .replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "invoice";
}

document.addEventListener("input", event => {
    if (event.target?.id !== "Input_InvoiceDescription") return;
    regenerateConfigurationSlug();
});
document.addEventListener("change", event => {
    if (event.target?.id === "Input_Id") {
        event.target.dataset.edited = "true";
        return;
    }
    if (event.target?.id === "Input_IntegrationType") {
        regenerateConfigurationSlug();
    }
});

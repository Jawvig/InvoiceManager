// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
document.addEventListener("input", event => {
    const description = document.querySelector("#Input_InvoiceDescription");
    const id = document.querySelector("#Input_Id:not([readonly])");
    const integration = document.querySelector("#Input_IntegrationType");
    if (!id || event.target !== description || id.dataset.edited === "true") return;
    const source = description.value.trim() || `${(integration?.value || "invoice").toLowerCase()} invoice`;
    id.value = source.toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, "")
        .replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "invoice";
});
document.addEventListener("change", event => {
    if (event.target?.id === "Input_Id") event.target.dataset.edited = "true";
});

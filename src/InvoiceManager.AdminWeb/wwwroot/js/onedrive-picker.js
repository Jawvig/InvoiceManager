// Custom Graph-based OneDrive folder drill-down picker. Deliberately NOT the Microsoft File
// Picker v8 (that requires SharePoint tokens / new AAD permissions); this only needs the Graph
// Files.ReadWrite.All scope already used elsewhere. Each folder level is fetched on demand as the
// user drills in (see OnGetOneDriveFolderChildrenAsync) rather than walking the whole tree.
(function () {
    "use strict";
    const openButton = document.getElementById("onedrive-picker-open");
    if (!openButton) return;

    const dialog = document.getElementById("onedrive-picker-dialog");
    const closeButton = document.getElementById("onedrive-picker-close");
    const cancelButton = document.getElementById("onedrive-picker-cancel");
    const selectButton = document.getElementById("onedrive-picker-select");
    const breadcrumbNav = document.getElementById("onedrive-picker-breadcrumb");
    const statusEl = document.getElementById("onedrive-picker-status");
    const listEl = document.getElementById("onedrive-picker-list");
    const summaryEl = document.getElementById("onedrive-picker-summary");

    const driveIdInput = document.getElementById("Input_DriveId");
    const driveNameInput = document.getElementById("Input_DriveName");
    const folderItemIdInput = document.getElementById("Input_FolderItemId");
    const folderPathInput = document.getElementById("Input_FolderPath");

    const buildHandlerUrl = window.InvoiceManagerConfigurationWizard?.buildHandlerUrl
        ?? function (handler, params) {
            const url = new URL(window.location.href);
            url.searchParams.set("handler", handler);
            if (params) for (const [key, value] of Object.entries(params)) {
                if (value !== null && value !== undefined) url.searchParams.set(key, value);
            }
            return url.toString();
        };

    // currentDrive: {id, name} | null. breadcrumb: [{id, name}, ...] folders drilled into below
    // the drive root (does not include a synthetic "root" entry).
    let currentDrive = null;
    let breadcrumb = [];

    openButton.addEventListener("click", openPicker);
    closeButton?.addEventListener("click", closePicker);
    cancelButton?.addEventListener("click", closePicker);
    selectButton?.addEventListener("click", commitSelection);
    dialog?.addEventListener("cancel", closePicker);

    function openPicker() {
        currentDrive = driveIdInput.value
            ? { id: driveIdInput.value, name: driveNameInput.value || "OneDrive" }
            : null;
        breadcrumb = [];
        if (typeof dialog.showModal === "function") dialog.showModal();
        else dialog.setAttribute("open", "open");
        loadLevel();
    }

    function closePicker() {
        if (typeof dialog.close === "function") dialog.close();
        else dialog.removeAttribute("open");
    }

    function commitSelection() {
        if (!currentDrive || breadcrumb.length === 0) return;
        const folder = breadcrumb[breadcrumb.length - 1];
        driveIdInput.value = currentDrive.id;
        driveNameInput.value = currentDrive.name;
        folderItemIdInput.value = folder.id;
        folderPathInput.value = "/" + breadcrumb.map(f => f.name).join("/");
        if (summaryEl) summaryEl.textContent = `${currentDrive.name} · ${folderPathInput.value}`;
        closePicker();
    }

    function renderBreadcrumb() {
        breadcrumbNav.innerHTML = "";
        if (!currentDrive) return;

        const rootLink = document.createElement("button");
        rootLink.type = "button";
        rootLink.textContent = currentDrive.name;
        rootLink.addEventListener("click", () => { breadcrumb = []; loadLevel(); });
        breadcrumbNav.appendChild(rootLink);

        breadcrumb.forEach((folder, index) => {
            breadcrumbNav.appendChild(document.createTextNode(" / "));
            const link = document.createElement("button");
            link.type = "button";
            link.textContent = folder.name;
            link.disabled = index === breadcrumb.length - 1;
            link.addEventListener("click", () => { breadcrumb = breadcrumb.slice(0, index + 1); loadLevel(); });
            breadcrumbNav.appendChild(link);
        });
    }

    function setStatus(text) {
        statusEl.textContent = text || "";
    }

    async function loadLevel() {
        renderBreadcrumb();
        listEl.innerHTML = "";
        selectButton.disabled = !(currentDrive && breadcrumb.length > 0);

        if (!currentDrive) {
            await loadDrives();
            return;
        }
        await loadFolderChildren();
    }

    async function loadDrives() {
        setStatus("Loading drives…");
        try {
            const response = await fetch(buildHandlerUrl("OneDriveDrives"), { headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error(`Request failed with status ${response.status}`);
            const drives = await response.json();
            setStatus(drives.length ? "" : "No OneDrive drives were found for this account.");
            for (const drive of drives) {
                listEl.appendChild(makeRow(drive.name, () => {
                    currentDrive = { id: drive.id, name: drive.name };
                    breadcrumb = [];
                    loadLevel();
                }));
            }
        } catch {
            setStatus("Could not load OneDrive drives.");
            appendRetry(loadDrives);
        }
    }

    async function loadFolderChildren() {
        setStatus("Loading folders…");
        const folderItemId = breadcrumb.length > 0 ? breadcrumb[breadcrumb.length - 1].id : null;
        try {
            const response = await fetch(
                buildHandlerUrl("OneDriveFolderChildren", { driveId: currentDrive.id, folderItemId }),
                { headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error(`Request failed with status ${response.status}`);
            const folders = await response.json();
            setStatus("");
            for (const folder of folders) {
                listEl.appendChild(makeRow(folder.name, () => {
                    breadcrumb = [...breadcrumb, { id: folder.id, name: folder.name }];
                    loadLevel();
                }));
            }
        } catch {
            setStatus("Could not load folders.");
            appendRetry(loadFolderChildren);
        }
    }

    function makeRow(name, onOpen) {
        const item = document.createElement("li");
        const button = document.createElement("button");
        button.type = "button";
        button.className = "onedrive-picker-row";
        button.textContent = name;
        button.addEventListener("click", onOpen);
        item.appendChild(button);
        return item;
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

document.addEventListener("DOMContentLoaded", () => {
    const toggle = document.querySelector("#nav-toggle");
    const menu = document.querySelector("#nav-menu");
    const backdrop = document.querySelector("#nav-backdrop");
    if (!toggle || !menu || !backdrop) return;

    const open = () => {
        menu.classList.add("open");
        menu.setAttribute("aria-hidden", "false");
        backdrop.hidden = false;
        toggle.setAttribute("aria-expanded", "true");
    };

    const close = () => {
        menu.classList.remove("open");
        menu.setAttribute("aria-hidden", "true");
        backdrop.hidden = true;
        toggle.setAttribute("aria-expanded", "false");
    };

    toggle.addEventListener("click", () => {
        if (menu.classList.contains("open")) close(); else open();
    });
    backdrop.addEventListener("click", close);
    menu.querySelectorAll("a").forEach(link => link.addEventListener("click", close));
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && menu.classList.contains("open")) close();
    });
});

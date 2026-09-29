(function () {
    "use strict";

    const shell = document.querySelector("[data-app-shell]");
    const sidebarToggleButtons = document.querySelectorAll("[data-sidebar-toggle]");
    const sidebarScrim = document.querySelector("[data-sidebar-scrim]");

    function setNavigation(open) {
        if (!shell) return;
        shell.classList.toggle("nav-open", open);
        document.body.classList.toggle("nav-is-open", open);
        sidebarToggleButtons.forEach(function (button) {
            button.setAttribute("aria-expanded", String(open));
        });
    }

    sidebarToggleButtons.forEach(function (button) {
        button.addEventListener("click", function () {
            setNavigation(!shell.classList.contains("nav-open"));
        });
    });

    if (sidebarScrim) {
        sidebarScrim.addEventListener("click", function () {
            setNavigation(false);
        });
    }

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            setNavigation(false);
            document.querySelectorAll("dialog[open]").forEach(function (dialog) {
                dialog.close();
            });
        }
    });

    window.addEventListener("resize", function () {
        if (window.innerWidth > 1080) {
            setNavigation(false);
        }
    });

    document.querySelectorAll("[data-modal-open]").forEach(function (trigger) {
        trigger.addEventListener("click", function () {
            const id = trigger.getAttribute("data-modal-open");
            const dialog = id ? document.getElementById(id) : null;
            if (dialog && typeof dialog.showModal === "function") {
                dialog.showModal();
            }
        });
    });

    document.querySelectorAll("[data-modal-close]").forEach(function (trigger) {
        trigger.addEventListener("click", function () {
            const dialog = trigger.closest("dialog");
            if (dialog) dialog.close();
        });
    });

    document.querySelectorAll("dialog").forEach(function (dialog) {
        dialog.addEventListener("click", function (event) {
            if (event.target === dialog) dialog.close();
        });
    });
}());

(function (root, factory) {
    "use strict";

    const api = factory();

    if (typeof module === "object" && module.exports) {
        module.exports = api;
    }

    if (root && typeof window !== "undefined" && root === window) {
        root.BillingControlSite = api;
    }

    if (typeof document !== "undefined" && typeof window !== "undefined") {
        api.initialize(document, window);
    }
}(typeof globalThis === "undefined" ? this : globalThis, function () {
    "use strict";

    const navigationBreakpoint = 1080;
    const versionedWorkflowStatuses = new Set([
        "QueriesSent",
        "DraftManagementReportSent",
    ]);

    const toArray = (value) => Array.from(value || []);

    const eligibleGridRows = (rows) =>
        toArray(rows).filter((row) => row?.dataset?.gridEligible !== "false");

    const gridRowCountText = (shown, total) =>
        `${shown} of ${total} Rows shown`;

    const gridEmptyMessage = (count, options = {}) => {
        if (Number(count) > 0) return "";
        const config = options || {};
        return config.emptyEligibleMessage ||
            config.emptyMessage ||
            "No records match the current view.";
    };

    const workflowVersionRequired = (status) =>
        versionedWorkflowStatuses.has(status);

    const workflowSuggestedVersion = (status, versions) => {
        if (!workflowVersionRequired(status)) return 0;

        const validVersions = toArray(versions)
            .map(Number)
            .filter((version) => Number.isInteger(version) && version > 0);
        return Math.max(1, ...validVersions);
    };

    const navigationMode = (width) =>
        Number(width) > navigationBreakpoint ? "desktop" : "mobile";

    const setNavigationState = (documentRef, shell, toggleButtons, open) => {
        if (!shell) return;

        shell.classList?.toggle("nav-open", open);
        documentRef.body?.classList?.toggle("nav-is-open", open);
        toArray(toggleButtons).forEach((button) => {
            button.setAttribute?.("aria-expanded", String(open));
        });
    };

    const setWorkflowVersionState = (root, status, lastStatus) => {
        const container = root.querySelector?.("[data-workflow-version-container]");
        const input = root.querySelector?.("[data-workflow-version-input]");
        if (!container || !input) return;

        const required = workflowVersionRequired(status);
        container.hidden = !required;
        container.setAttribute?.("aria-hidden", String(!required));
        input.required = required;
        input.disabled = !required;

        if (!required) return;

        const dataKey = status === "QueriesSent"
            ? "nextQueriesVersion"
            : "nextDraftVersion";
        const suggested = Number(root.dataset?.[dataKey]);
        const statusChanged = lastStatus !== undefined && lastStatus !== status;
        if (Number.isInteger(suggested) && suggested > 0 &&
            (!input.value || statusChanged)) {
            input.value = String(suggested);
        }
    };

    const initializeWorkflowForm = (root) => {
        const status = root.querySelector?.("[data-workflow-status]");
        if (!status) return;

        let lastStatus = status.value;
        const sync = () => {
            setWorkflowVersionState(root, status.value, lastStatus);
            lastStatus = status.value;
        };
        status.addEventListener?.("change", sync);
        sync();
    };

    const initializeWorkflowBatch = (root) => {
        const action = root.querySelector?.("[data-batch-action]");
        const fields = root.querySelector?.("[data-batch-workflow-fields]");
        const status = root.querySelector?.("[data-workflow-status]");
        if (!action || !fields || !status) return;

        let lastStatus = status.value;
        const sync = () => {
            const workflowAction = action.value === "UpdateWorkflow";
            fields.hidden = !workflowAction;
            fields.setAttribute?.("aria-hidden", String(!workflowAction));
            toArray(fields.querySelectorAll?.("select, input") || [])
                .forEach((control) => {
                    control.disabled = !workflowAction;
                });
            setWorkflowVersionState(root, status.value, lastStatus);
            if (!workflowAction) {
                toArray(fields.querySelectorAll?.("select, input") || [])
                    .forEach((control) => {
                        control.disabled = true;
                    });
            }
            lastStatus = status.value;
        };

        action.addEventListener?.("change", sync);
        status.addEventListener?.("change", sync);
        sync();
    };

    const initializeWorkflowSelection = (root) => {
        const selectAll = root.querySelector?.("[data-workflow-select-all]");
        const selections = () => toArray(root.querySelectorAll?.("[data-workflow-select]") || [])
            .filter((checkbox) => !checkbox.disabled);
        if (!selectAll) return;

        const syncSelectAll = () => {
            const available = selections();
            const checked = available.filter((checkbox) => checkbox.checked).length;
            selectAll.checked = available.length > 0 && checked === available.length;
            selectAll.indeterminate = checked > 0 && checked < available.length;
        };

        selectAll.addEventListener?.("change", () => {
            selections().forEach((checkbox) => {
                checkbox.checked = selectAll.checked;
            });
            syncSelectAll();
        });
        selections().forEach((checkbox) => {
            checkbox.addEventListener?.("change", syncSelectAll);
        });
        syncSelectAll();
    };

    const initializeModals = (documentRef) => {
        toArray(documentRef.querySelectorAll?.("[data-modal-open]") || [])
            .forEach((trigger) => {
                trigger.addEventListener?.("click", () => {
                    const id = trigger.getAttribute?.("data-modal-open");
                    const dialog = id ? documentRef.getElementById?.(id) : null;
                    if (dialog && typeof dialog.showModal === "function") {
                        dialog.showModal();
                    }
                });
            });

        toArray(documentRef.querySelectorAll?.("[data-modal-close]") || [])
            .forEach((trigger) => {
                trigger.addEventListener?.("click", () => {
                    const dialog = trigger.closest?.("dialog");
                    if (dialog && typeof dialog.close === "function") dialog.close();
                });
            });

        toArray(documentRef.querySelectorAll?.("dialog") || [])
            .forEach((dialog) => {
                dialog.addEventListener?.("click", (event) => {
                    if (event.target === dialog && typeof dialog.close === "function") {
                        dialog.close();
                    }
                });
            });
    };

    const initialize = (documentRef, windowRef) => {
        if (!documentRef || !windowRef ||
            typeof documentRef.querySelector !== "function") return;

        const shell = documentRef.querySelector("[data-app-shell]");
        const toggleButtons = documentRef.querySelectorAll?.("[data-sidebar-toggle]") || [];
        const sidebarScrim = documentRef.querySelector?.("[data-sidebar-scrim]");
        const setNavigation = (open) =>
            setNavigationState(documentRef, shell, toggleButtons, open);

        if (shell) {
            toArray(toggleButtons).forEach((button) => {
                button.addEventListener?.("click", () => {
                    setNavigation(!shell.classList?.contains("nav-open"));
                });
            });

            sidebarScrim?.addEventListener?.("click", () => setNavigation(false));
        }

        documentRef.addEventListener?.("keydown", (event) => {
            if (event.key !== "Escape") return;
            setNavigation(false);
            toArray(documentRef.querySelectorAll?.("dialog[open]") || [])
                .forEach((dialog) => dialog.close?.());
        });

        windowRef.addEventListener?.("resize", () => {
            if (navigationMode(windowRef.innerWidth) === "desktop") {
                setNavigation(false);
            }
        });

        toArray(documentRef.querySelectorAll?.("[data-workflow-form]") || [])
            .forEach(initializeWorkflowForm);
        toArray(documentRef.querySelectorAll?.("[data-workflow-batch]") || [])
            .forEach((root) => {
                initializeWorkflowBatch(root);
                initializeWorkflowSelection(root);
            });
        initializeModals(documentRef);
    };

    return {
        navigationBreakpoint,
        navigationMode,
        eligibleGridRows,
        gridRowCountText,
        gridEmptyMessage,
        workflowVersionRequired,
        workflowSuggestedVersion,
        initialize,
    };
}));

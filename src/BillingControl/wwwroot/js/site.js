(() => {
  const sidebarStorageKey = "billing-control.sidebar-collapsed";
  const sidebarBreakpoint = 900;
  const navGroupNames = Object.freeze([
    "home",
    "billing",
    "work",
    "documents",
    "directory",
    "reports",
    "administration",
  ]);
  const sidebarMode = (viewportWidth) =>
    viewportWidth < sidebarBreakpoint ? "mobile" : "desktop";
  const savedSidebarPreference = (value) => value === "true";
  const desktopSidebarLabel = (open) =>
    open ? "Hide navigation" : "Show navigation";
  const keepOneNavGroupOpen = (groups, selected) => {
    groups.forEach((group) => {
      if (group !== selected) group.open = false;
    });
  };
  const isGridEligible = (row) => row.dataset.gridEligible !== "false";
  const eligibleGridRows = (items) => items.filter((item) =>
    isGridEligible(item.row ?? item),
  );
  const gridRowCountText = (shown, total) =>
    `${shown} of ${total} Rows shown`;
  const gridPageCount = (total, pageSize) =>
    pageSize > 0 ? Math.max(1, Math.ceil(total / pageSize)) : 1;
  const gridRangeText = (page, pageSize, total) => {
    if (total === 0) return "Showing 0 records";
    if (pageSize <= 0) return `Showing 1–${total} of ${total}`;
    const start = (page - 1) * pageSize + 1;
    const end = Math.min(total, page * pageSize);
    return `Showing ${start}–${end} of ${total}`;
  };
  const defaultGridEmptyMessage =
    "No rows to show. Add a record or clear your filters.";
  const versionedWorkflow = new Set([
    "QueriesSent",
    "DraftManagementReportSent",
  ]);
  const workflowVersionRequired = (status) => versionedWorkflow.has(status);
  const workflowSuggestedVersion = (status, candidates = []) =>
    workflowVersionRequired(status)
      ? Math.max(1, ...candidates.map(Number).filter((value) => Number.isInteger(value) && value > 0))
      : 0;
  const gridEmptyMessage = (eligibleCount, dataset) =>
    eligibleCount === 0 && dataset.emptyEligibleMessage
      ? dataset.emptyEligibleMessage
      : dataset.emptyMessage ?? defaultGridEmptyMessage;
  if (typeof module === "object" && module.exports)
    module.exports = {
      isGridEligible,
      eligibleGridRows,
      gridRowCountText,
      gridPageCount,
      gridRangeText,
      gridEmptyMessage,
      workflowVersionRequired,
      workflowSuggestedVersion,
      sidebarStorageKey,
      sidebarBreakpoint,
      sidebarMode,
      savedSidebarPreference,
      navGroupNames,
      keepOneNavGroupOpen,
      desktopSidebarLabel,
    };
  if (typeof document === "undefined") return;

  const root = document.documentElement;
  const sidebar = document.getElementById("app-sidebar");
  const sidebarToggles = [...document.querySelectorAll("[data-sidebar-toggle]")];
  const sidebarScrim = document.querySelector("[data-sidebar-scrim]");
  const navGroups = sidebar
    ? [...sidebar.querySelectorAll("[data-nav-group]")]
    : [];
  const isMobileSidebar = () => sidebarMode(window.innerWidth) === "mobile";
  const readSidebarPreference = () => {
    try {
      return savedSidebarPreference(window.localStorage.getItem(sidebarStorageKey));
    } catch {
      return false;
    }
  };
  const writeSidebarPreference = (collapsed) => {
    try {
      window.localStorage.setItem(sidebarStorageKey, String(collapsed));
    } catch {
      // A blocked storage area should not prevent navigation from working.
    }
  };
  const syncSidebarToggle = () => {
    const mobile = isMobileSidebar();
    const open = mobile
      ? root.classList.contains("sidebar-mobile-open")
      : !root.classList.contains("sidebar-collapsed");
    sidebarToggles.forEach((toggle) => {
      toggle.setAttribute("aria-expanded", String(open));
      toggle.setAttribute(
        "aria-label",
        mobile
          ? open
            ? "Close navigation"
            : "Open navigation"
          : desktopSidebarLabel(open),
      );
    });
  };
  const closeMobileSidebar = () => {
    root.classList.remove("sidebar-mobile-open");
    syncSidebarToggle();
  };
  const resetSidebarForViewport = () => {
    root.classList.remove("sidebar-collapsed");
    if (isMobileSidebar()) {
      root.classList.remove("sidebar-mobile-open");
    } else {
      root.classList.remove("sidebar-mobile-open");
    }
    syncSidebarToggle();
  };
  if (sidebar && sidebarToggles.length) {
    resetSidebarForViewport();
    sidebarToggles.forEach((toggle) =>
      toggle.addEventListener("click", () => {
        if (isMobileSidebar()) {
          root.classList.toggle("sidebar-mobile-open");
        } else {
          root.classList.remove("sidebar-collapsed");
        }
        syncSidebarToggle();
      }),
    );
    sidebarScrim?.addEventListener("click", closeMobileSidebar);
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && root.classList.contains("sidebar-mobile-open"))
        closeMobileSidebar();
    });
    sidebar.querySelectorAll("a").forEach((link) =>
      link.addEventListener("click", () => {
        if (isMobileSidebar()) closeMobileSidebar();
      }),
    );
    window.addEventListener("resize", () => {
      const wasMobile = root.dataset.sidebarMode;
      const nextMode = sidebarMode(window.innerWidth);
      if (wasMobile !== nextMode) resetSidebarForViewport();
      root.dataset.sidebarMode = nextMode;
    });
    root.dataset.sidebarMode = sidebarMode(window.innerWidth);
  }

  sidebar?.querySelectorAll(".nav-group-summary").forEach((summary) =>
    summary.addEventListener("click", (event) => {
      if (isMobileSidebar() || !root.classList.contains("sidebar-collapsed")) return;
      event.preventDefault();
      const group = summary.closest("[data-nav-group]");
      root.classList.remove("sidebar-collapsed");
      writeSidebarPreference(false);
      if (group) {
        group.open = true;
        keepOneNavGroupOpen(navGroups, group);
      }
      syncSidebarToggle();
    }),
  );

  navGroups.forEach((group) =>
    group.addEventListener("toggle", () => {
      if (group.open) keepOneNavGroupOpen(navGroups, group);
    }),
  );

  const element = (tag, cls, text) => {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined) e.textContent = text;
    return e;
  };
  const button = (text, action, cls = "btn btn-sm btn-outline-secondary") => {
    const b = element("button", cls, text);
    b.type = "button";
    b.addEventListener("click", action);
    return b;
  };
  const dataTableTransactional = (table) => {
    if (table.matches(".invoice-allocation-table, .receipt-allocation-edit-table, .template-items-table"))
      return true;
    const owningForm = table.closest("form");
    return Boolean(owningForm && table.querySelector("input, select, textarea"));
  };

  document.querySelectorAll("table.data-grid").forEach((table) => {
    if (typeof window.DataTable !== "function") {
      console.error("DataTables.net failed to load for", table);
      return;
    }

    const transactional = dataTableTransactional(table);
    const headers = [...(table.tHead?.rows[0]?.cells ?? [])];
    const columnDefs = headers
      .map((header, index) =>
        header.hasAttribute("data-nosort")
          ? { targets: index, orderable: false }
          : null,
      )
      .filter(Boolean);

    const options = transactional
      ? {
          paging: false,
          searching: false,
          ordering: false,
          info: false,
          autoWidth: false,
          layout: {
            topStart: null,
            topEnd: null,
            bottomStart: null,
            bottomEnd: null,
          },
        }
      : {
          pageLength: 25,
          lengthMenu: [10, 25, 50, 100, -1],
          order: [],
          autoWidth: false,
          columnDefs,
          layout: {
            topStart: "search",
            topEnd: "pageLength",
            bottomStart: "info",
            bottomEnd: "paging",
          },
          language: {
            search: "",
            searchPlaceholder: "Search records…",
            lengthMenu: "_MENU_ rows",
            info: "Showing _START_–_END_ of _TOTAL_",
            infoEmpty: "Showing 0 records",
            infoFiltered: " (filtered from _MAX_)",
            zeroRecords: table.dataset.emptyMessage ?? "No matching records",
            emptyTable:
              table.dataset.emptyEligibleMessage ??
              table.dataset.emptyMessage ??
              defaultGridEmptyMessage,
            paginate: {
              first: "First",
              previous: "‹",
              next: "›",
              last: "Last",
            },
          },
        };

    const dataTable = new window.DataTable(table, options);
    table.dataset.dataTables = "active";
    table.dataset.dataTablesMode = transactional ? "transactional" : "register";

    if (!transactional) {
      const container = dataTable.table().container();
      const firstLayoutRow = container.querySelector(".dt-layout-row");
      const columnButton = button(
        "Columns",
        () => {
          const d = element("dialog", "filter-dialog dt-column-dialog");
          d.setAttribute("aria-label", "Visible columns");
          d.append(element("h3", "", "Visible columns"));
          const picker = element("div", "column-picker");

          headers.forEach((header, index) => {
            const label = element("label", "check-line");
            const check = element("input");
            check.type = "checkbox";
            check.checked = dataTable.column(index).visible();
            check.onchange = () => {
              dataTable.column(index).visible(check.checked, false);
              dataTable.columns.adjust().draw(false);
            };
            label.append(check, document.createTextNode(header.textContent.trim()));
            picker.append(label);
          });

          d.append(picker, button("Done", () => d.close(), "btn btn-primary"));
          document.body.append(d);
          d.addEventListener("close", () => d.remove());
          d.showModal();
        },
        "btn btn-sm btn-outline-secondary dt-columns-button",
      );

      if (firstLayoutRow) {
        const end = firstLayoutRow.querySelector(".dt-layout-end");
        (end ?? firstLayoutRow).append(columnButton);
      }
    }

    table.addEventListener("grid:refresh", () => {
      if (transactional) return;
      dataTable.rows().invalidate("dom").draw(false);
    });
  });

  const workflowDefaultVersion = (root, status, rows = []) => {
    const attribute =
      status === "QueriesSent"
        ? "nextQueriesVersion"
        : "nextDraftVersion";
    const values = rows
      .map((row) => Number(row.dataset[attribute]))
      .filter((value) => Number.isInteger(value) && value > 0);
    const own = Number(root.dataset[attribute]);
    return workflowSuggestedVersion(status, [...values, own]);
  };
  const syncWorkflowVersion = (root, rows = [], reset = false) => {
    const status = root.querySelector("[data-workflow-status]");
    const container = root.querySelector("[data-workflow-version-container]");
    const input = root.querySelector("[data-workflow-version-input]");
    if (!status || !container || !input) return;
    const required = workflowVersionRequired(status.value);
    container.hidden = !required;
    input.required = required;
    if (!required) input.value = "";
    else if (reset || input.value === "")
      input.value = String(workflowDefaultVersion(root, status.value, rows));
  };
  document.querySelectorAll("[data-workflow-form]").forEach((form) => {
    const status = form.querySelector("[data-workflow-status]");
    syncWorkflowVersion(form);
    status?.addEventListener("change", () => syncWorkflowVersion(form, [], true));
  });
  document.querySelectorAll("[data-workflow-batch]").forEach((form) => {
    const action = form.querySelector("[data-batch-action]");
    const fields = form.querySelector("[data-batch-workflow-fields]");
    const selectedRows = () =>
      [...form.querySelectorAll("[data-workflow-select]:checked")]
        .map((input) => input.closest("tr"))
        .filter(Boolean);
    const sync = (reset = false) => {
      const changingWorkflow = !action || action.value === "UpdateWorkflow";
      if (fields) fields.hidden = !changingWorkflow;
      if (changingWorkflow) syncWorkflowVersion(form, selectedRows(), reset);
    };
    form.querySelectorAll("[data-workflow-select]").forEach((input) =>
      input.addEventListener("change", () => sync()),
    );
    form.querySelector("[data-workflow-status]")?.addEventListener("change", () =>
      sync(true),
    );
    action?.addEventListener("change", () => sync());
    form.querySelector("[data-workflow-select-all]")?.addEventListener("change", (event) => {
      const check = event.currentTarget.checked;
      form.querySelectorAll("[data-workflow-select]").forEach((input) => {
        if (!input.closest("tr").hidden) input.checked = check;
      });
      sync();
    });
    sync();
  });
})();

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
  document.querySelectorAll("table.data-grid").forEach((table) => {
    const body = table.tBodies[0];
    const headerRow = table.tHead?.rows[0];
    if (!body || !headerRow) return;

    const rows = [...body.rows];
    const headers = [...headerRow.cells];
    const values = (row) => [...row.cells].map((c) => c.textContent.trim());
    const data = rows.map((row, i) => ({ row, i, values: values(row) }));

    let filters = new Map();
    let sorts = [];
    let searchText = "";
    let pageSize = 25;
    let currentPage = 1;
    const hidden = new Set();

    const toolbar = element("div", "grid-toolbar");
    const searchWrap = element("label", "grid-search");
    const searchLabel = element("span", "visually-hidden", "Search records");
    const search = element("input", "form-control grid-search-input");
    search.type = "search";
    search.placeholder = "Search records…";
    search.setAttribute("aria-label", "Search records");
    searchWrap.append(searchLabel, search);

    const tools = element("div", "grid-tools");
    const reset = button("Reset", () => {
      filters.clear();
      searchText = "";
      search.value = "";
      currentPage = 1;
      render();
    }, "btn btn-sm btn-outline-secondary grid-reset");

    const columns = button("Columns", () => {
      const d = dialog("Visible columns");
      const list = element("div", "column-picker");
      headers.forEach((h, i) => {
        const label = element("label", "check-line");
        const check = element("input");
        check.type = "checkbox";
        check.checked = !hidden.has(i);
        check.onchange = () => {
          check.checked ? hidden.delete(i) : hidden.add(i);
          render();
        };
        label.append(check, document.createTextNode(h.dataset.title));
        list.append(label);
      });
      d.append(list, button("Done", () => d.close(), "btn btn-primary"));
      d.showModal();
    });

    const pageSizeWrap = element("label", "grid-page-size");
    pageSizeWrap.append(element("span", "", "Rows"));
    const pageSizeSelect = element("select", "form-select grid-limit");
    pageSizeSelect.setAttribute("aria-label", "Rows per page");
    [
      [10, "10"],
      [25, "25"],
      [50, "50"],
      [100, "100"],
      [0, "All"],
    ].forEach(([v, t]) => {
      const o = element("option", "", t);
      o.value = v;
      if (v === pageSize) o.selected = true;
      pageSizeSelect.append(o);
    });
    pageSizeSelect.onchange = () => {
      pageSize = Number(pageSizeSelect.value);
      currentPage = 1;
      render();
    };
    pageSizeWrap.append(pageSizeSelect);

    tools.append(reset, columns, pageSizeWrap);
    toolbar.append(searchWrap, tools);
    table.before(toolbar);

    const scroll = element("div", "table-scroll");
    table.before(scroll);
    scroll.append(table);

    const defaultEmptyMessage = gridEmptyMessage(1, table.dataset);
    const empty = element("div", "empty-state", defaultEmptyMessage);
    scroll.append(empty);

    const footer = element("div", "grid-footer");
    const count = element("span", "grid-count");
    const pagination = element("nav", "grid-pagination");
    pagination.setAttribute("aria-label", "Table pagination");
    footer.append(count, pagination);
    scroll.after(footer);

    const numeric = (s) => Number(s.replace(/,/g, ""));
    const matches = (value, f) => {
      if (f.values) return f.values.has(value);
      const n = numeric(value);
      if (value === "" || !Number.isFinite(n)) return false;
      return {
        "=": n === f.a,
        "<>": n !== f.a,
        ">": n > f.a,
        ">=": n >= f.a,
        "<": n < f.a,
        "<=": n <= f.a,
        "><": n >= Math.min(f.a, f.b) && n <= Math.max(f.a, f.b),
      }[f.op];
    };

    const eligible = () => eligibleGridRows(data);
    const searchable = (item) =>
      !searchText ||
      item.values.some((value, i) =>
        !hidden.has(i) && value.toLowerCase().includes(searchText),
      );

    function renderPagination(pageCount) {
      pagination.replaceChildren();
      if (pageCount <= 1) return;

      const pageButton = (label, page, current = false, disabled = false) => {
        const b = button(label, () => {
          if (disabled || page === currentPage) return;
          currentPage = page;
          render();
        }, current ? "grid-page active" : "grid-page");
        b.disabled = disabled;
        if (current) b.setAttribute("aria-current", "page");
        return b;
      };

      pagination.append(pageButton("‹", Math.max(1, currentPage - 1), false, currentPage === 1));
      const candidates = new Set([1, pageCount, currentPage - 1, currentPage, currentPage + 1]);
      const pages = [...candidates].filter((p) => p >= 1 && p <= pageCount).sort((a, b) => a - b);
      let previous = 0;
      pages.forEach((page) => {
        if (previous && page - previous > 1)
          pagination.append(element("span", "grid-page-gap", "…"));
        pagination.append(pageButton(String(page), page, page === currentPage));
        previous = page;
      });
      pagination.append(pageButton("›", Math.min(pageCount, currentPage + 1), false, currentPage === pageCount));
    }

    const render = () => {
      const eligibleRows = eligible();
      const filtered = eligibleRows.filter((r) =>
        searchable(r) && [...filters].every(([i, f]) => matches(r.values[i], f)),
      );

      filtered.sort((a, b) => {
        for (const s of sorts) {
          const av = a.values[s.i];
          const bv = b.values[s.i];
          const c =
            headers[s.i].dataset.type === "amount"
              ? numeric(av) - numeric(bv)
              : av.localeCompare(bv, undefined, { numeric: true });
          if (c) return c * s.dir;
        }
        return a.i - b.i;
      });

      const pageCount = gridPageCount(filtered.length, pageSize);
      currentPage = Math.min(Math.max(1, currentPage), pageCount);
      const pageRows =
        pageSize > 0
          ? filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize)
          : filtered;

      rows.forEach((r) => (r.hidden = true));
      pageRows.forEach((r) => {
        r.row.hidden = false;
        body.append(r.row);
      });

      headers.forEach((h, i) => {
        h.hidden = hidden.has(i);
        rows.forEach((r) => (r.cells[i].hidden = hidden.has(i)));
        const f = h.querySelector(".filter-button");
        if (f) {
          f.classList.toggle("filtered", filters.has(i));
          f.setAttribute("aria-pressed", String(filters.has(i)));
        }
        const sort = h.querySelector(".sort-button");
        if (sort) {
          const idx = sorts.findIndex((s) => s.i === i);
          sort.textContent =
            h.dataset.title +
            (idx >= 0
              ? " " +
                (sorts[idx].dir === 1 ? "↑" : "↓") +
                (sorts.length > 1 ? " " + (idx + 1) : "")
              : "");
        }
      });

      count.textContent = gridRangeText(currentPage, pageSize, filtered.length);
      reset.hidden = filters.size === 0 && !searchText;
      empty.textContent = gridEmptyMessage(eligibleRows.length, table.dataset);
      empty.hidden = filtered.length !== 0;
      renderPagination(pageCount);
    };

    function dialog(title) {
      const d = element("dialog", "filter-dialog");
      d.setAttribute("aria-label", title);
      d.append(element("h3", "", title));
      document.body.append(d);
      d.addEventListener("close", () => d.remove());
      return d;
    }

    function openFilter(i) {
      const h = headers[i];
      const d = dialog(h.dataset.title);
      const type = h.dataset.type;

      if (type === "amount") {
        const op = element("select", "form-select");
        ["=", "<>", ">=", ">", "<=", "<", "><"].forEach((s) => {
          const o = element("option", "", s);
          op.append(o);
        });
        const a = element("input", "form-control");
        const b = element("input", "form-control");
        const error = element("p", "text-danger");
        a.type = b.type = "number";
        a.step = b.step = "any";
        a.setAttribute("aria-label", "Amount");
        b.setAttribute("aria-label", "Second amount");
        a.placeholder = "Amount";
        b.placeholder = "Second amount";
        const f = filters.get(i);
        if (f && !f.values) {
          op.value = f.op;
          a.value = f.a;
          if (f.b !== undefined) b.value = f.b;
        }
        b.hidden = op.value !== "><";
        op.onchange = () => (b.hidden = op.value !== "><");
        const apply = () => {
          if (
            a.value === "" ||
            !Number.isFinite(Number(a.value)) ||
            (op.value === "><" &&
              (b.value === "" || !Number.isFinite(Number(b.value))))
          ) {
            error.textContent = "Enter valid amounts.";
            return;
          }
          filters.set(i, {
            op: op.value,
            a: Number(a.value),
            b: Number(b.value),
          });
          currentPage = 1;
          render();
          d.close();
        };
        [a, b].forEach((e) =>
          e.addEventListener("keydown", (e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              apply();
            }
          }),
        );
        d.append(op, a, b, error);
        const dialogFooter = element("div", "filter-footer");
        dialogFooter.append(
          button("Clear", () => {
            filters.delete(i);
            currentPage = 1;
            render();
            d.close();
          }),
          button("Cancel", () => d.close()),
          button("Apply", apply, "btn btn-primary btn-sm"),
        );
        d.append(dialogFooter);
        d.showModal();
        return;
      }

      const all = [...new Set(eligible().map((r) => r.values[i]))].sort((a, b) =>
        a.localeCompare(b, undefined, { numeric: true }),
      );
      let selected = new Set(filters.get(i)?.values ?? all);
      let filterSearchText = "";
      const filterSearch = element("input", "form-control");
      filterSearch.placeholder = "Search values";
      filterSearch.setAttribute("aria-label", "Search values");
      const list = element("div", "filter-values");
      const selectLabel = element("label", "check-line");
      const selectAll = element("input");
      selectAll.type = "checkbox";
      const selectText = element("span", "", "Select all");
      selectLabel.append(selectAll, selectText);

      const dateMatches = (v, q) => {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(v)) return v.toLowerCase().includes(q);
        const [y, m, day] = v.split("-");
        const date = new Date(Number(y), Number(m) - 1, Number(day));
        if (/^\d{4}$/.test(q)) return y === q;
        if (/^\d{2}$/.test(q)) return day === q;
        return (
          date.toLocaleString("en", { month: "long" }).toLowerCase().includes(q) ||
          v.includes(q)
        );
      };
      const visible = () =>
        all.filter((v) =>
          type === "date"
            ? dateMatches(v, filterSearchText)
            : v.toLowerCase().includes(filterSearchText),
        );
      const sync = () => {
        const vv = visible();
        selectAll.checked = vv.length > 0 && vv.every((v) => selected.has(v));
        selectAll.indeterminate =
          vv.some((v) => selected.has(v)) && !selectAll.checked;
        list.querySelectorAll("input[data-group]").forEach((c) => {
          const grouped = JSON.parse(c.dataset.group);
          c.checked = grouped.every((v) => selected.has(v));
          c.indeterminate =
            grouped.some((v) => selected.has(v)) && !c.checked;
        });
      };
      function leaf(v, parent) {
        const label = element("label");
        const check = element("input");
        check.type = "checkbox";
        check.checked = selected.has(v);
        check.onchange = () => {
          check.checked ? selected.add(v) : selected.delete(v);
          sync();
        };
        label.append(
          check,
          document.createTextNode(type === "date" && v ? v.slice(8) : v || "(Blank)"),
        );
        parent.append(label);
      }
      function group(title, vv, parent, child) {
        const details = element("details");
        const summary = element("summary");
        const check = element("input");
        details.open = Boolean(filterSearchText);
        check.type = "checkbox";
        check.dataset.group = JSON.stringify(vv);
        check.addEventListener("click", (e) => e.stopPropagation());
        check.onchange = () => {
          vv.forEach((v) => (check.checked ? selected.add(v) : selected.delete(v)));
          details.querySelectorAll("input").forEach((k) => (k.checked = check.checked));
          sync();
        };
        summary.append(check, document.createTextNode(title));
        details.append(summary);
        parent.append(details);
        child(details);
      }
      function draw() {
        list.replaceChildren();
        const vv = visible();
        selectText.textContent = filterSearchText ? "Select search results" : "Select all";
        if (!vv.length) list.append(element("p", "hint", "No matching values."));
        if (type === "date") {
          const years = [
            ...new Set(vv.filter((v) => /^\d{4}-/.test(v)).map((v) => v.slice(0, 4))),
          ];
          years.forEach((y) => {
            const dates = vv.filter((v) => v.startsWith(y + "-"));
            group(y, dates, list, (year) => {
              [...new Set(dates.map((v) => v.slice(5, 7)))].forEach((m) => {
                const days = dates.filter((v) => v.slice(5, 7) === m);
                group(
                  new Date(2000, Number(m) - 1, 1).toLocaleString("en", { month: "short" }),
                  days,
                  year,
                  (month) => days.forEach((v) => leaf(v, month)),
                );
              });
            });
          });
          vv.filter((v) => !/^\d{4}-/.test(v)).forEach((v) => leaf(v, list));
        } else {
          vv.sort((a, b) => Number(selected.has(b)) - Number(selected.has(a)))
            .forEach((v) => leaf(v, list));
        }
        sync();
      }

      filterSearch.oninput = () => {
        filterSearchText = filterSearch.value.trim().toLowerCase();
        draw();
      };
      selectAll.onchange = () => {
        visible().forEach((v) =>
          selectAll.checked ? selected.add(v) : selected.delete(v),
        );
        draw();
      };
      const apply = () => {
        if (filterSearchText) selected = new Set(visible());
        if (all.every((v) => selected.has(v))) filters.delete(i);
        else filters.set(i, { values: new Set(selected) });
        currentPage = 1;
        render();
        d.close();
      };
      filterSearch.addEventListener("keydown", (e) => {
        if (e.key === "Enter") {
          e.preventDefault();
          apply();
        }
      });
      const dialogFooter = element("div", "filter-footer");
      dialogFooter.append(
        button("Clear", () => {
          selected.clear();
          filterSearch.value = "";
          filterSearchText = "";
          draw();
        }),
        button("Cancel", () => d.close()),
        button("Apply", apply, "btn btn-primary btn-sm"),
      );
      d.append(filterSearch, selectLabel, list, dialogFooter);
      draw();
      d.showModal();
      filterSearch.focus();
    }

    headers.forEach((h, i) => {
      h.dataset.title = h.textContent.trim();
      if (h.hasAttribute("data-nosort")) return;
      h.textContent = "";
      const sort = button(
        h.dataset.title,
        (event) => {
          const current = sorts.find((x) => x.i === i);
          const next = { i, dir: current ? -current.dir : 1 };
          sorts = event.shiftKey
            ? current
              ? sorts.map((x) => (x.i === i ? next : x))
              : [...sorts, next]
            : [next];
          currentPage = 1;
          render();
        },
        "sort-button",
      );
      const filter = button("⌄", () => openFilter(i), "filter-button");
      filter.setAttribute("aria-label", `Filter ${h.dataset.title}`);
      h.append(sort, filter);
    });

    search.addEventListener("input", () => {
      searchText = search.value.trim().toLowerCase();
      currentPage = 1;
      render();
    });

    table.addEventListener("grid:refresh", () => {
      data.forEach((item) => (item.values = values(item.row)));
      currentPage = 1;
      render();
    });

    render();
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

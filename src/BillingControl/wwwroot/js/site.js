(() => {
  const sidebarStorageKey = "billing-control.sidebar-collapsed";
  const sidebarBreakpoint = 900;
  const sidebarMode = (viewportWidth) =>
    viewportWidth < sidebarBreakpoint ? "mobile" : "desktop";
  const savedSidebarPreference = (value) => value === "true";
  const isGridEligible = (row) => row.dataset.gridEligible !== "false";
  const eligibleGridRows = (items) => items.filter((item) =>
    isGridEligible(item.row ?? item),
  );
  const gridRowCountText = (shown, total) =>
    `${shown} of ${total} Rows shown`;
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
      gridEmptyMessage,
      workflowVersionRequired,
      workflowSuggestedVersion,
      sidebarStorageKey,
      sidebarBreakpoint,
      sidebarMode,
      savedSidebarPreference,
    };
  if (typeof document === "undefined") return;

  const root = document.documentElement;
  const sidebar = document.getElementById("app-sidebar");
  const sidebarToggles = [...document.querySelectorAll("[data-sidebar-toggle]")];
  const sidebarScrim = document.querySelector("[data-sidebar-scrim]");
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
          : open
            ? "Collapse navigation"
            : "Expand navigation",
      );
    });
  };
  const closeMobileSidebar = () => {
    root.classList.remove("sidebar-mobile-open");
    syncSidebarToggle();
  };
  const resetSidebarForViewport = () => {
    if (isMobileSidebar()) {
      root.classList.remove("sidebar-collapsed", "sidebar-mobile-open");
    } else {
      root.classList.remove("sidebar-mobile-open");
      root.classList.toggle("sidebar-collapsed", readSidebarPreference());
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
          const collapsed = !root.classList.contains("sidebar-collapsed");
          root.classList.toggle("sidebar-collapsed", collapsed);
          writeSidebarPreference(collapsed);
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
    const body = table.tBodies[0],
      rows = [...body.rows],
      headers = [...table.tHead.rows[0].cells];
    const values = (row) => [...row.cells].map((c) => c.textContent.trim());
    const data = rows.map((row, i) => ({ row, i, values: values(row) }));
    let filters = new Map(),
      sorts = [],
      limit = 500;
    const hidden = new Set();
    const toolbar = element("div", "grid-toolbar"),
      count = element("span", "grid-count"),
      tools = element("div", "grid-tools");
    const clear = button("Clear filters", () => {
      filters.clear();
      render();
    });
    const limitSelect = element("select", "form-select grid-limit");
    limitSelect.setAttribute("aria-label", "Row limit");
    [
      [500, "500 rows"],
      [1000, "1,000 rows"],
      [0, "All rows"],
    ].forEach(([v, t]) => {
      const o = element("option", "", t);
      o.value = v;
      limitSelect.append(o);
    });
    limitSelect.onchange = () => {
      limit = Number(limitSelect.value);
      render();
    };
    const columns = button("Columns", () => {
      const d = dialog("Visible columns");
      headers.forEach((h, i) => {
        const label = element("label", "check-line"),
          check = element("input");
        check.type = "checkbox";
        check.checked = !hidden.has(i);
        check.onchange = () => {
          check.checked ? hidden.delete(i) : hidden.add(i);
          render();
        };
        label.append(check, document.createTextNode(h.dataset.title));
        d.append(label);
      });
      d.append(button("Done", () => d.close(), "btn btn-primary"));
      d.showModal();
    });
    tools.append(clear, columns, limitSelect);
    toolbar.append(count, tools);
    table.before(toolbar);
    const scroll = element("div", "table-scroll");
    table.before(scroll);
    scroll.append(table);
    const defaultEmptyMessage = gridEmptyMessage(1, table.dataset);
    const empty = element("div", "empty-state", defaultEmptyMessage);
    scroll.append(empty);
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
    const loaded = () => {
      const items = eligible();
      return limit ? items.slice(0, limit) : items;
    };
    const render = () => {
      const loadedRows = loaded();
      const shown = loadedRows.filter((r) =>
        [...filters].every(([i, f]) => matches(r.values[i], f)),
      );
      shown.sort((a, b) => {
        for (const s of sorts) {
          const av = a.values[s.i],
            bv = b.values[s.i];
          const c =
            headers[s.i].dataset.type === "amount"
              ? numeric(av) - numeric(bv)
              : av.localeCompare(bv, undefined, { numeric: true });
          if (c) return c * s.dir;
        }
        return a.i - b.i;
      });
      rows.forEach((r) => (r.hidden = true));
      shown.forEach((r) => {
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
      count.textContent = gridRowCountText(shown.length, loadedRows.length);
      clear.hidden = filters.size === 0;
      empty.textContent = gridEmptyMessage(eligible().length, table.dataset);
      empty.hidden = shown.length !== 0;
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
      const h = headers[i],
        d = dialog(h.dataset.title),
        type = h.dataset.type;
      if (type === "amount") {
        const op = element("select", "form-select");
        ["=", "<>", ">=", ">", "<=", "<", "><"].forEach((s) => {
          const o = element("option", "", s);
          op.append(o);
        });
        const a = element("input", "form-control"),
          b = element("input", "form-control"),
          error = element("p", "text-danger");
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
        const footer = element("div", "filter-footer");
        footer.append(
          button("Clear", () => {
            filters.delete(i);
            render();
            d.close();
          }),
          button("Cancel", () => d.close()),
          button("Apply", apply, "btn btn-primary btn-sm"),
        );
        d.append(footer);
        d.showModal();
        return;
      }
      const all = [...new Set(loaded().map((r) => r.values[i]))].sort((a, b) =>
        a.localeCompare(b, undefined, { numeric: true }),
      );
      let selected = new Set(filters.get(i)?.values ?? all),
        searchText = "";
      const search = element("input", "form-control");
      search.placeholder = "Search values";
      search.setAttribute("aria-label", "Search values");
      const list = element("div", "filter-values"),
        selectLabel = element("label", "check-line"),
        selectAll = element("input");
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
          date
            .toLocaleString("en", { month: "long" })
            .toLowerCase()
            .includes(q) || v.includes(q)
        );
      };
      const visible = () =>
        all.filter((v) =>
          type === "date"
            ? dateMatches(v, searchText)
            : v.toLowerCase().includes(searchText),
        );
      const sync = () => {
        const vv = visible();
        selectAll.checked = vv.length > 0 && vv.every((v) => selected.has(v));
        selectAll.indeterminate =
          vv.some((v) => selected.has(v)) && !selectAll.checked;
        list.querySelectorAll("input[data-group]").forEach((c) => {
          const vv = JSON.parse(c.dataset.group);
          c.checked = vv.every((v) => selected.has(v));
          c.indeterminate = vv.some((v) => selected.has(v)) && !c.checked;
        });
      };
      function leaf(v, parent) {
        const l = element("label"),
          c = element("input");
        c.type = "checkbox";
        c.checked = selected.has(v);
        c.onchange = () => {
          c.checked ? selected.add(v) : selected.delete(v);
          sync();
        };
        l.append(
          c,
          document.createTextNode(
            type === "date" && v ? v.slice(8) : v || "(Blank)",
          ),
        );
        parent.append(l);
      }
      function group(title, vv, parent, child) {
        const details = element("details"),
          summary = element("summary"),
          c = element("input");
        details.open = Boolean(searchText);
        c.type = "checkbox";
        c.dataset.group = JSON.stringify(vv);
        c.addEventListener("click", (e) => e.stopPropagation());
        c.onchange = () => {
          vv.forEach((v) => (c.checked ? selected.add(v) : selected.delete(v)));
          details
            .querySelectorAll("input")
            .forEach((k) => (k.checked = c.checked));
          sync();
        };
        summary.append(c, document.createTextNode(title));
        details.append(summary);
        parent.append(details);
        child(details);
      }
      function draw() {
        list.replaceChildren();
        const vv = visible();
        selectText.textContent = searchText
          ? "Select All Search Results"
          : "Select all";
        if (!vv.length)
          list.append(element("p", "hint", "No matching values."));
        if (type === "date") {
          const years = [
            ...new Set(
              vv.filter((v) => /^\d{4}-/.test(v)).map((v) => v.slice(0, 4)),
            ),
          ];
          years.forEach((y) => {
            const dates = vv.filter((v) => v.startsWith(y + "-"));
            group(y, dates, list, (year) => {
              [...new Set(dates.map((v) => v.slice(5, 7)))].forEach((m) => {
                const days = dates.filter((v) => v.slice(5, 7) === m);
                group(
                  new Date(2000, Number(m) - 1, 1).toLocaleString("en", {
                    month: "short",
                  }),
                  days,
                  year,
                  (month) => days.forEach((v) => leaf(v, month)),
                );
              });
            });
          });
          vv.filter((v) => !/^\d{4}-/.test(v)).forEach((v) => leaf(v, list));
        } else {
          vv.sort(
            (a, b) => Number(selected.has(b)) - Number(selected.has(a)),
          ).forEach((v) => leaf(v, list));
        }
        sync();
      }
      search.oninput = () => {
        searchText = search.value.trim().toLowerCase();
        draw();
      };
      selectAll.onchange = () => {
        visible().forEach((v) =>
          selectAll.checked ? selected.add(v) : selected.delete(v),
        );
        draw();
      };
      const apply = () => {
        if (searchText) selected = new Set(visible());
        if (all.every((v) => selected.has(v))) filters.delete(i);
        else filters.set(i, { values: new Set(selected) });
        render();
        d.close();
      };
      search.addEventListener("keydown", (e) => {
        if (e.key === "Enter") {
          e.preventDefault();
          apply();
        }
      });
      const footer = element("div", "filter-footer");
      footer.append(
        button("Clear", () => {
          selected.clear();
          search.value = "";
          searchText = "";
          draw();
        }),
        button("Cancel", () => d.close()),
        button("Apply", apply, "btn btn-primary btn-sm"),
      );
      d.append(search, selectLabel, list, footer);
      draw();
      d.showModal();
      search.focus();
    }
    headers.forEach((h, i) => {
      h.dataset.title = h.textContent.trim();
      if (h.hasAttribute("data-nosort")) return;
      h.textContent = "";
      const s = button(
        h.dataset.title,
        (e) => {
          const current = sorts.find((x) => x.i === i);
          const next = { i, dir: current ? -current.dir : 1 };
          sorts = e.shiftKey
            ? current
              ? sorts.map((x) => (x.i === i ? next : x))
              : [...sorts, next]
            : [next];
          render();
        },
        "sort-button",
      );
      const f = button("▾", () => openFilter(i), "filter-button");
      f.setAttribute("aria-label", `Filter ${h.dataset.title}`);
      h.append(s, f);
    });
    table.addEventListener("grid:refresh", () => {
      data.forEach((item) => (item.values = values(item.row)));
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

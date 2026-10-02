(() => {
  const sidebarStorageKey = "billing-control.sidebar-collapsed";
  const sidebarBreakpoint = 900;
  const navGroupNames = Object.freeze([
    "billing",
    "work",
    "documents",
    "directory",
    "administration",
  ]);
  const sidebarMode = (viewportWidth) =>
    viewportWidth <= sidebarBreakpoint ? "compact" : "desktop";
  const savedSidebarPreference = (value) => value === "true";
  const desktopSidebarLabel = (open) =>
    open ? "Hide navigation" : "Show navigation";
  const keepOneNavGroupOpen = (groups, selected) => {
    groups.forEach((group) => {
      if (group !== selected) group.open = false;
    });
  };
  const isGridEligible = (row) => row.dataset.gridEligible !== "false";
  const eligibleGridRows = (items) =>
    items.filter((item) => isGridEligible(item.row ?? item));
  const gridRowCountText = (shown, total) => `${shown} of ${total} Rows shown`;
  const defaultGridEmptyMessage =
    "No rows to show. Add a record or clear your filters.";
  const gridEmptyMessage = (eligibleCount, dataset) =>
    eligibleCount === 0 && dataset.emptyEligibleMessage
      ? dataset.emptyEligibleMessage
      : dataset.emptyMessage ?? defaultGridEmptyMessage;
  const versionedWorkflow = new Set([
    "QueriesSent",
    "DraftManagementReportSent",
  ]);
  const workflowVersionRequired = (status) => versionedWorkflow.has(status);
  const workflowSuggestedVersion = (status, candidates = []) =>
    workflowVersionRequired(status)
      ? Math.max(
          1,
          ...candidates
            .map(Number)
            .filter((value) => Number.isInteger(value) && value > 0),
        )
      : 0;

  if (typeof module === "object" && module.exports) {
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
      navGroupNames,
      keepOneNavGroupOpen,
      desktopSidebarLabel,
    };
  }
  if (typeof document === "undefined") return;

  const root = document.documentElement;
  const sidebar = document.getElementById("app-sidebar");
  const appShell = document.querySelector("[data-app-shell]");
  const appScroller = document.querySelector("[data-app-scroll]");
  const sidebarToggles = [...document.querySelectorAll("[data-sidebar-toggle]")];
  const searchToggle = document.querySelector("[data-search-toggle]");
  const globalSearch = document.querySelector("[data-global-search]");
  const navGroups = sidebar
    ? [...sidebar.querySelectorAll("[data-nav-group]")]
    : [];

  const readSidebarPreference = () => {
    try {
      return savedSidebarPreference(
        window.localStorage.getItem(sidebarStorageKey),
      );
    } catch {
      return false;
    }
  };
  const writeSidebarPreference = (collapsed) => {
    try {
      window.localStorage.setItem(sidebarStorageKey, String(collapsed));
    } catch {
      // Storage can be blocked without breaking navigation.
    }
  };
  const isCompactSidebar = () =>
    sidebarMode(window.innerWidth) === "compact";
  const sidebarIsOpen = () =>
    isCompactSidebar()
      ? root.classList.contains("sidebar-mobile-open")
      : !root.classList.contains("sidebar-collapsed");
  const syncSidebarToggle = () => {
    const open = sidebarIsOpen();
    sidebarToggles.forEach((toggle) => {
      toggle.setAttribute("aria-expanded", String(open));
      toggle.setAttribute(
        "aria-label",
        isCompactSidebar()
          ? open
            ? "Close navigation"
            : "Open navigation"
          : desktopSidebarLabel(open),
      );
    });
  };
  const applySidebarViewport = () => {
    if (isCompactSidebar()) {
      root.classList.remove("sidebar-collapsed", "sidebar-mobile-open");
    } else {
      root.classList.remove("sidebar-mobile-open", "compact-search-open");
      root.classList.toggle("sidebar-collapsed", readSidebarPreference());
    }
    root.dataset.sidebarMode = sidebarMode(window.innerWidth);
    syncSidebarToggle();
  };
  const closeCompactSidebar = () => {
    if (!isCompactSidebar()) return;
    root.classList.remove("sidebar-mobile-open");
    syncSidebarToggle();
  };

  if (sidebar && sidebarToggles.length) {
    applySidebarViewport();
    sidebarToggles.forEach((toggle) =>
      toggle.addEventListener("click", (event) => {
        event.stopPropagation();
        if (isCompactSidebar()) {
          root.classList.toggle("sidebar-mobile-open");
        } else {
          const collapsed = !root.classList.contains("sidebar-collapsed");
          root.classList.toggle("sidebar-collapsed", collapsed);
          writeSidebarPreference(collapsed);
        }
        syncSidebarToggle();
      }),
    );
    sidebar.querySelectorAll("a").forEach((link) =>
      link.addEventListener("click", closeCompactSidebar),
    );
    appShell?.addEventListener("click", (event) => {
      if (
        isCompactSidebar() &&
        root.classList.contains("sidebar-mobile-open") &&
        !event.target.closest("[data-sidebar-toggle]")
      ) {
        closeCompactSidebar();
      }
    });
    window.addEventListener("resize", () => {
      const next = sidebarMode(window.innerWidth);
      if (root.dataset.sidebarMode !== next) applySidebarViewport();
    });
  }

  navGroups.forEach((group) =>
    group.addEventListener("toggle", () => {
      if (group.open) keepOneNavGroupOpen(navGroups, group);
    }),
  );

  searchToggle?.addEventListener("click", (event) => {
    event.stopPropagation();
    root.classList.toggle("compact-search-open");
    if (root.classList.contains("compact-search-open")) globalSearch?.focus();
  });
  appScroller?.addEventListener("scroll", () =>
    appShell?.classList.toggle("scrolled", appScroller.scrollTop > 40),
  );

  const element = (tag, cls, text) => {
    const value = document.createElement(tag);
    if (cls) value.className = cls;
    if (text !== undefined) value.textContent = text;
    return value;
  };
  const button = (text, action, cls = "btn btn-sm btn-outline-secondary") => {
    const value = element("button", cls, text);
    value.type = "button";
    value.addEventListener("click", action);
    return value;
  };

  const normalizeValue = (value) => (value ?? "").trim();
  const numberValue = (value) => {
    const cleaned = normalizeValue(value).replace(/[^0-9.+-]/g, "");
    const parsed = Number(cleaned);
    return Number.isFinite(parsed) ? parsed : Number.NaN;
  };
  const dateValue = (value) => {
    const parsed = Date.parse(normalizeValue(value));
    return Number.isFinite(parsed) ? parsed : Number.NaN;
  };
  const compareValues = (a, b, type) => {
    if (type === "amount" || type === "number") {
      const av = numberValue(a);
      const bv = numberValue(b);
      if (Number.isFinite(av) && Number.isFinite(bv)) return av - bv;
    }
    if (type === "date") {
      const av = dateValue(a);
      const bv = dateValue(b);
      if (Number.isFinite(av) && Number.isFinite(bv)) return av - bv;
    }
    return normalizeValue(a).localeCompare(normalizeValue(b), undefined, {
      numeric: true,
      sensitivity: "base",
    });
  };
  const conditionMatches = (value, condition, type) => {
    const source = normalizeValue(value);
    const a = normalizeValue(condition.a);
    const b = normalizeValue(condition.b);
    if (["amount", "number", "date"].includes(type)) {
      const parse = type === "date" ? dateValue : numberValue;
      const x = parse(source);
      const first = parse(a);
      const second = parse(b);
      if (!Number.isFinite(x) || !Number.isFinite(first)) return false;
      switch (condition.op) {
        case "eq":
          return x === first;
        case "neq":
          return x !== first;
        case "gt":
          return x > first;
        case "gte":
          return x >= first;
        case "lt":
          return x < first;
        case "lte":
          return x <= first;
        case "between":
          return (
            Number.isFinite(second) &&
            x >= Math.min(first, second) &&
            x <= Math.max(first, second)
          );
        default:
          return true;
      }
    }
    const lower = source.toLowerCase();
    const first = a.toLowerCase();
    switch (condition.op) {
      case "equals":
        return lower === first;
      case "notequals":
        return lower !== first;
      case "contains":
        return lower.includes(first);
      case "notcontains":
        return !lower.includes(first);
      case "startswith":
        return lower.startsWith(first);
      case "endswith":
        return lower.endsWith(first);
      default:
        return true;
    }
  };

  const dataGridInstances = [];
  document.querySelectorAll("table.data-grid").forEach((table, tableIndex) => {
    const body = table.tBodies[0];
    const headerRow = table.tHead?.rows[0];
    if (!body || !headerRow) return;

    const originalRows = [...body.rows];
    const headers = [...headerRow.cells];
    const rowValues = (row) =>
      [...row.cells].map((cell) => normalizeValue(cell.textContent));
    const data = originalRows.map((row, index) => ({
      row,
      index,
      values: rowValues(row),
    }));
    const filters = new Map();
    let sort = null;
    let page = 1;
    let pageSize = 25;
    let query = "";
    const syncToUrl = tableIndex === 0;
    const params = new URLSearchParams(window.location.search);

    if (syncToUrl) {
      query = params.get("grid_q") ?? "";
      const size = Number(params.get("grid_size"));
      if ([25, 50, 100, 0].includes(size)) pageSize = size;
      const parsedPage = Number(params.get("grid_page"));
      if (Number.isInteger(parsedPage) && parsedPage > 0) page = parsedPage;
      const sortIndex = Number(params.get("grid_sort"));
      const sortDir = params.get("grid_dir");
      if (
        Number.isInteger(sortIndex) &&
        sortIndex >= 0 &&
        sortIndex < headers.length &&
        ["asc", "desc"].includes(sortDir)
      ) {
        sort = { index: sortIndex, dir: sortDir };
      }
      headers.forEach((_, index) => {
        const valueState = params.get(`grid_f_${index}`);
        const conditionState = params.get(`grid_c_${index}`);
        if (valueState) {
          try {
            const values = JSON.parse(valueState);
            if (Array.isArray(values)) filters.set(index, { values: new Set(values) });
          } catch {
            // Invalid bookmarked state falls back safely.
          }
        } else if (conditionState) {
          try {
            const condition = JSON.parse(conditionState);
            if (condition && typeof condition === "object")
              filters.set(index, { condition });
          } catch {
            // Invalid bookmarked state falls back safely.
          }
        }
      });
    }

    const toolbar = element("div", "grid-toolbar");
    const count = element("span", "grid-count");
    const tools = element("div", "grid-tools");
    const clear = button("Clear filters", () => {
      filters.clear();
      sort = null;
      page = 1;
      render();
    });
    tools.append(clear);
    toolbar.append(count, tools);
    table.before(toolbar);

    const scroll = element("div", "table-scroll");
    table.before(scroll);
    scroll.append(table);
    const empty = element(
      "div",
      "empty-state",
      gridEmptyMessage(1, table.dataset),
    );
    scroll.append(empty);

    const pagination = element("div", "grid-pagination");
    const pageSizeWrap = element("div", "page-size");
    pageSizeWrap.append(document.createTextNode("Rows"));
    const sizeSelect = element("select");
    [
      [25, "25"],
      [50, "50"],
      [100, "100"],
      [0, "All"],
    ].forEach(([value, label]) => {
      const option = element("option", "", label);
      option.value = String(value);
      option.selected = value === pageSize;
      sizeSelect.append(option);
    });
    pageSizeWrap.append(sizeSelect);
    const pageNumbers = element("div", "page-numbers");
    pagination.append(pageSizeWrap, pageNumbers);
    scroll.after(pagination);

    let menu = null;
    let menuButton = null;
    const closeMenu = (restoreFocus = false) => {
      if (!menu) return;
      menu.remove();
      menu = null;
      if (restoreFocus) menuButton?.focus();
      menuButton = null;
    };

    const uniqueValues = (column) =>
      [
        ...new Set(
          eligibleGridRows(data).map((item) => item.values[column]),
        ),
      ].sort((a, b) => compareValues(a, b, headers[column].dataset.type));

    const syncUrl = () => {
      if (!syncToUrl) return;
      const next = new URLSearchParams(window.location.search);
      [...next.keys()]
        .filter((key) => key.startsWith("grid_"))
        .forEach((key) => next.delete(key));
      if (query) next.set("grid_q", query);
      if (sort) {
        next.set("grid_sort", String(sort.index));
        next.set("grid_dir", sort.dir);
      }
      if (page !== 1) next.set("grid_page", String(page));
      if (pageSize !== 25) next.set("grid_size", String(pageSize));
      filters.forEach((filter, index) => {
        if (filter.values)
          next.set(`grid_f_${index}`, JSON.stringify([...filter.values]));
        else if (filter.condition)
          next.set(`grid_c_${index}`, JSON.stringify(filter.condition));
      });
      const nextQuery = next.toString();
      history.replaceState(
        null,
        "",
        `${location.pathname}${nextQuery ? `?${nextQuery}` : ""}${location.hash}`,
      );
    };

    const filteredRows = () => {
      const eligible = eligibleGridRows(data);
      const q = query.toLowerCase();
      const output = eligible.filter((item) => {
        if (q && !item.values.some((value) => value.toLowerCase().includes(q)))
          return false;
        return [...filters].every(([column, filter]) => {
          const value = item.values[column];
          if (filter.values) return filter.values.has(value);
          if (filter.condition)
            return conditionMatches(
              value,
              filter.condition,
              headers[column].dataset.type ?? "text",
            );
          return true;
        });
      });
      if (sort) {
        const type = headers[sort.index].dataset.type ?? "text";
        output.sort((a, b) => {
          const compared = compareValues(
            a.values[sort.index],
            b.values[sort.index],
            type,
          );
          return compared === 0
            ? a.index - b.index
            : compared * (sort.dir === "desc" ? -1 : 1);
        });
      }
      return output;
    };

    const setupRowOpen = (item) => {
      const row = item.row;
      const link =
        row.querySelector("a.record-link") ??
        row.querySelector('a[href*="/Details/"]') ??
        row.querySelector('a[href*="/details/"]');
      if (!link || row.dataset.recordLinkReady === "true") return;
      row.dataset.recordLinkReady = "true";
      row.dataset.recordLink = link.href;
      row.tabIndex = row.tabIndex >= 0 ? row.tabIndex : 0;
      row.setAttribute("role", "link");
      row.setAttribute("aria-label", `Open ${normalizeValue(row.cells[0]?.textContent) || "record"} in new tab`);
      const open = () => window.open(link.href, "_blank", "noopener");
      row.addEventListener("click", (event) => {
        if (event.target.closest("a,button,input,select,textarea,label,summary")) return;
        open();
      });
      row.addEventListener("keydown", (event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          open();
        }
      });
    };
    data.forEach(setupRowOpen);

    const renderPagination = (totalRows) => {
      pageNumbers.replaceChildren();
      const totalPages = pageSize ? Math.max(1, Math.ceil(totalRows / pageSize)) : 1;
      page = Math.min(Math.max(1, page), totalPages);
      const addPage = (label, target, active = false, disabled = false) => {
        const control = element("button", `page-button${active ? " active" : ""}`, label);
        control.type = "button";
        control.disabled = disabled;
        control.addEventListener("click", () => {
          page = target;
          render();
        });
        pageNumbers.append(control);
      };
      addPage("‹", Math.max(1, page - 1), false, page === 1);
      let start = Math.max(1, page - 2);
      let end = Math.min(totalPages, start + 4);
      start = Math.max(1, end - 4);
      for (let current = start; current <= end; current += 1)
        addPage(String(current), current, current === page);
      addPage("›", Math.min(totalPages, page + 1), false, page === totalPages);
      return totalPages;
    };

    const render = () => {
      data.forEach((item) => {
        item.values = rowValues(item.row);
        item.row.hidden = true;
      });
      const eligibleCount = eligibleGridRows(data).length;
      const matched = filteredRows();
      const totalPages = renderPagination(matched.length);
      if (page > totalPages) page = totalPages;
      const visible = pageSize
        ? matched.slice((page - 1) * pageSize, page * pageSize)
        : matched;
      visible.forEach((item) => {
        item.row.hidden = false;
        body.append(item.row);
      });
      headers.forEach((header, index) => {
        const filterButton = header.querySelector(".filter-button");
        filterButton?.classList.toggle("filtered", filters.has(index));
        filterButton?.setAttribute("aria-pressed", String(filters.has(index)));
        const sortButton = header.querySelector(".sort-button");
        if (sortButton) {
          sortButton.textContent =
            header.dataset.title +
            (sort?.index === index
              ? sort.dir === "asc"
                ? " ↑"
                : " ↓"
              : "");
        }
      });
      count.textContent = gridRowCountText(matched.length, eligibleCount);
      clear.hidden = filters.size === 0 && !sort && !query;
      empty.textContent = gridEmptyMessage(eligibleCount, table.dataset);
      empty.hidden = visible.length !== 0;
      syncUrl();
    };

    const openFilterMenu = (column, trigger) => {
      closeMenu();
      menuButton = trigger;
      const header = headers[column];
      const type = header.dataset.type ?? "text";
      const values = uniqueValues(column);
      const existing = filters.get(column);
      let selected = new Set(existing?.values ?? values);
      menu = element("div", "excel-filter-menu");
      menu.setAttribute("role", "menu");
      menu.setAttribute("aria-label", `Filter ${header.dataset.title}`);

      const sortAsc = button(
        type === "amount" || type === "number"
          ? "Sort Smallest to Largest"
          : type === "date"
            ? "Sort Oldest to Newest"
            : "Sort A to Z",
        () => {
          sort = { index: column, dir: "asc" };
          page = 1;
          render();
        },
        "menu-command",
      );
      const sortDesc = button(
        type === "amount" || type === "number"
          ? "Sort Largest to Smallest"
          : type === "date"
            ? "Sort Newest to Oldest"
            : "Sort Z to A",
        () => {
          sort = { index: column, dir: "desc" };
          page = 1;
          render();
        },
        "menu-command",
      );
      const clearColumn = button(
        `Clear Filter From ${header.dataset.title}`,
        () => {
          filters.delete(column);
          page = 1;
          render();
          drawValues();
        },
        "menu-command",
      );
      menu.append(sortAsc, sortDesc, clearColumn, element("div", "menu-separator"));

      const search = element("input", "filter-value-search");
      search.type = "search";
      search.placeholder = "Search values";
      search.setAttribute("aria-label", `Search ${header.dataset.title} values`);
      const valueList = element("div", "filter-values");
      menu.append(search, valueList);

      const applySelected = () => {
        selected = new Set(
          [...valueList.querySelectorAll("input[data-value]:checked")].map(
            (input) => input.dataset.value,
          ),
        );
        if (selected.size === values.length) filters.delete(column);
        else filters.set(column, { values: new Set(selected) });
        page = 1;
        render();
      };
      const drawValues = () => {
        valueList.replaceChildren();
        const needle = search.value.trim().toLowerCase();
        const visibleValues = values.filter((value) =>
          (value || "(Blanks)").toLowerCase().includes(needle),
        );
        const selectAllLabel = element("label");
        const selectAll = element("input");
        selectAll.type = "checkbox";
        selectAll.checked =
          visibleValues.length > 0 &&
          visibleValues.every((value) => selected.has(value));
        selectAll.indeterminate =
          visibleValues.some((value) => selected.has(value)) && !selectAll.checked;
        selectAll.addEventListener("change", () => {
          visibleValues.forEach((value) =>
            selectAll.checked ? selected.add(value) : selected.delete(value),
          );
          drawValues();
          if (selected.size === values.length) filters.delete(column);
          else filters.set(column, { values: new Set(selected) });
          page = 1;
          render();
        });
        selectAllLabel.append(selectAll, document.createTextNode("(Select All)"));
        valueList.append(selectAllLabel);
        visibleValues.forEach((value) => {
          const label = element("label");
          const input = element("input");
          input.type = "checkbox";
          input.dataset.value = value;
          input.checked = selected.has(value);
          input.addEventListener("change", applySelected);
          label.append(
            input,
            document.createTextNode(value === "" ? "(Blanks)" : value),
          );
          valueList.append(label);
        });
      };
      search.addEventListener("input", drawValues);
      drawValues();

      const conditionDetails = element("details");
      const conditionSummary = element(
        "summary",
        "",
        type === "amount" || type === "number"
          ? "Number Filters"
          : type === "date"
            ? "Date Filters"
            : "Text Filters",
      );
      const conditionGrid = element("div", "condition-grid");
      const operator = element("select", "form-select");
      const first = element("input", "form-control");
      const second = element("input", "form-control");
      second.hidden = true;
      if (type === "amount" || type === "number" || type === "date") {
        [
          ["eq", "Equals"],
          ["neq", "Does Not Equal"],
          ["gt", "Greater Than"],
          ["gte", "Greater Than or Equal"],
          ["lt", "Less Than"],
          ["lte", "Less Than or Equal"],
          ["between", "Between"],
        ].forEach(([value, label]) => {
          const option = element("option", "", label);
          option.value = value;
          operator.append(option);
        });
        first.type = second.type = type === "date" ? "date" : "number";
      } else {
        [
          ["equals", "Equals"],
          ["notequals", "Does Not Equal"],
          ["contains", "Contains"],
          ["notcontains", "Does Not Contain"],
          ["startswith", "Begins With"],
          ["endswith", "Ends With"],
        ].forEach(([value, label]) => {
          const option = element("option", "", label);
          option.value = value;
          operator.append(option);
        });
        first.type = second.type = "text";
      }
      const priorCondition = existing?.condition;
      if (priorCondition) {
        operator.value = priorCondition.op;
        first.value = priorCondition.a ?? "";
        second.value = priorCondition.b ?? "";
      }
      second.hidden = operator.value !== "between";
      operator.addEventListener("change", () => {
        second.hidden = operator.value !== "between";
      });
      const applyCondition = button(
        "Apply Custom Filter",
        () => {
          if (!first.value) return first.focus();
          if (operator.value === "between" && !second.value) return second.focus();
          filters.set(column, {
            condition: {
              op: operator.value,
              a: first.value,
              b: second.value,
            },
          });
          page = 1;
          render();
        },
        "btn btn-primary btn-sm",
      );
      conditionGrid.append(operator, first, second, applyCondition);
      conditionDetails.append(conditionSummary, conditionGrid);
      menu.append(conditionDetails);
      document.body.append(menu);

      const rect = trigger.getBoundingClientRect();
      const width = 280;
      const left = Math.min(
        Math.max(8, rect.right - width),
        window.innerWidth - width - 8,
      );
      const top = Math.min(rect.bottom + 4, window.innerHeight - 220);
      menu.style.left = `${left}px`;
      menu.style.top = `${Math.max(8, top)}px`;
      search.focus();
    };

    headers.forEach((header, index) => {
      header.dataset.title = normalizeValue(header.textContent);
      if (header.hasAttribute("data-nosort")) return;
      header.textContent = "";
      const sortButton = button(
        header.dataset.title,
        () => {
          sort = {
            index,
            dir: sort?.index === index && sort.dir === "asc" ? "desc" : "asc",
          };
          page = 1;
          render();
        },
        "sort-button",
      );
      const filterButton = button(
        "▾",
        (event) => {
          event.stopPropagation();
          openFilterMenu(index, filterButton);
        },
        "filter-button",
      );
      filterButton.setAttribute("aria-label", `Filter ${header.dataset.title}`);
      filterButton.setAttribute("aria-pressed", "false");
      header.append(sortButton, filterButton);
    });

    sizeSelect.addEventListener("change", () => {
      pageSize = Number(sizeSelect.value);
      page = 1;
      render();
    });
    table.addEventListener("grid:refresh", render);

    const instance = {
      setQuery(value) {
        query = normalizeValue(value).toLowerCase();
        page = 1;
        render();
      },
      getQuery() {
        return query;
      },
      render,
    };
    dataGridInstances.push(instance);
    render();

    document.addEventListener("click", (event) => {
      if (menu && !menu.contains(event.target) && event.target !== menuButton)
        closeMenu();
    });
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && menu) closeMenu(true);
    });
  });

  if (globalSearch) {
    if (dataGridInstances.length) globalSearch.value = dataGridInstances[0].getQuery();
    let searchTimer;
    const applySearch = () =>
      dataGridInstances.forEach((instance) =>
        instance.setQuery(globalSearch.value),
      );
    globalSearch.addEventListener("input", () => {
      clearTimeout(searchTimer);
      const value = globalSearch.value.trim();
      if (!value) return applySearch();
      if (value.length >= 2) searchTimer = setTimeout(applySearch, 300);
    });
    globalSearch.addEventListener("keydown", (event) => {
      if (event.key !== "Enter") return;
      event.preventDefault();
      clearTimeout(searchTimer);
      applySearch();
    });
  }

  const toastElements = [...document.querySelectorAll(".system-toast")];
  toastElements.forEach((toast) => {
    const close = () => toast.remove();
    toast.querySelector("[data-toast-close]")?.addEventListener("click", close);
    if (!toast.hasAttribute("data-auto-toast")) return;
    let timer = setTimeout(close, 4000);
    const pause = () => clearTimeout(timer);
    const resume = () => {
      clearTimeout(timer);
      timer = setTimeout(close, 1200);
    };
    toast.addEventListener("mouseenter", pause);
    toast.addEventListener("focusin", pause);
    toast.addEventListener("mouseleave", resume);
    toast.addEventListener("focusout", resume);
  });

  const guardedForms = [
    ...document.querySelectorAll(
      'form.form-panel, form.invoice-create-form, form[data-unsaved-guard]',
    ),
  ];
  let dirtyForm = null;
  let pendingNavigation = null;
  guardedForms.forEach((form) => {
    form.querySelectorAll("input,select,textarea").forEach((control) =>
      control.addEventListener("change", () => {
        dirtyForm = form;
      }),
    );
    form.addEventListener("submit", () => {
      dirtyForm = null;
      const submit = form.querySelector(
        'button[type="submit"], input[type="submit"], button:not([type])',
      );
      if (submit && !submit.disabled) {
        submit.disabled = true;
        submit.classList?.add("is-saving");
      }
    });
  });

  let dirtyModal = null;
  const ensureDirtyModal = () => {
    if (dirtyModal) return dirtyModal;
    dirtyModal = element("div", "enterprise-modal-backdrop");
    dirtyModal.hidden = true;
    dirtyModal.innerHTML = `
      <div class="enterprise-modal" role="dialog" aria-modal="true" aria-labelledby="dirty-title">
        <div class="enterprise-modal-head"><h2 id="dirty-title">Unsaved changes</h2></div>
        <div class="enterprise-modal-body">You have unsaved changes. Save or discard them before leaving this page.</div>
        <div class="enterprise-modal-foot">
          <button type="button" class="btn btn-outline-secondary" data-dirty-stay>Stay</button>
          <button type="button" class="btn btn-outline-secondary" data-dirty-discard>Discard &amp; Leave</button>
          <button type="button" class="btn btn-primary" data-dirty-save>Save &amp; Leave</button>
        </div>
      </div>`;
    document.body.append(dirtyModal);
    const stay = dirtyModal.querySelector("[data-dirty-stay]");
    stay.addEventListener("click", () => {
      dirtyModal.hidden = true;
      pendingNavigation = null;
    });
    dirtyModal
      .querySelector("[data-dirty-discard]")
      .addEventListener("click", () => {
        const target = pendingNavigation;
        dirtyForm = null;
        dirtyModal.hidden = true;
        pendingNavigation = null;
        if (target) window.location.href = target;
      });
    dirtyModal
      .querySelector("[data-dirty-save]")
      .addEventListener("click", () => {
        dirtyModal.hidden = true;
        pendingNavigation = null;
        dirtyForm?.requestSubmit();
      });
    dirtyModal.addEventListener("click", (event) => {
      if (event.target === dirtyModal) stay.click();
    });
    return dirtyModal;
  };

  document.addEventListener("click", (event) => {
    if (!dirtyForm) return;
    const link = event.target.closest('a[href]:not([target="_blank"])');
    if (!link || link.origin !== window.location.origin) return;
    if (link.closest(".table-scroll")) return;
    event.preventDefault();
    pendingNavigation = link.href;
    const modal = ensureDirtyModal();
    modal.hidden = false;
    modal.querySelector("[data-dirty-stay]")?.focus();
  });
  window.addEventListener("beforeunload", (event) => {
    if (!dirtyForm) return;
    event.preventDefault();
    event.returnValue = "";
  });

  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    if (dirtyModal && !dirtyModal.hidden) {
      dirtyModal.hidden = true;
      pendingNavigation = null;
      return;
    }
    closeCompactSidebar();
    root.classList.remove("compact-search-open");
  });

  const workflowDefaultVersion = (workflowRoot, status, rows = []) => {
    const attribute =
      status === "QueriesSent" ? "nextQueriesVersion" : "nextDraftVersion";
    const values = rows
      .map((row) => Number(row.dataset[attribute]))
      .filter((value) => Number.isInteger(value) && value > 0);
    const own = Number(workflowRoot.dataset[attribute]);
    return workflowSuggestedVersion(status, [...values, own]);
  };
  const syncWorkflowVersion = (workflowRoot, rows = [], reset = false) => {
    const status = workflowRoot.querySelector("[data-workflow-status]");
    const container = workflowRoot.querySelector(
      "[data-workflow-version-container]",
    );
    const input = workflowRoot.querySelector("[data-workflow-version-input]");
    if (!status || !container || !input) return;
    const required = workflowVersionRequired(status.value);
    container.hidden = !required;
    input.required = required;
    if (!required) input.value = "";
    else if (reset || input.value === "")
      input.value = String(
        workflowDefaultVersion(workflowRoot, status.value, rows),
      );
  };
  document.querySelectorAll("[data-workflow-form]").forEach((form) => {
    const status = form.querySelector("[data-workflow-status]");
    syncWorkflowVersion(form);
    status?.addEventListener("change", () =>
      syncWorkflowVersion(form, [], true),
    );
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
      if (changingWorkflow)
        syncWorkflowVersion(form, selectedRows(), reset);
    };
    form.querySelectorAll("[data-workflow-select]").forEach((input) =>
      input.addEventListener("change", () => sync()),
    );
    form
      .querySelector("[data-workflow-status]")
      ?.addEventListener("change", () => sync(true));
    action?.addEventListener("change", () => sync());
    form
      .querySelector("[data-workflow-select-all]")
      ?.addEventListener("change", (event) => {
        const checked = event.currentTarget.checked;
        form.querySelectorAll("[data-workflow-select]").forEach((input) => {
          if (!input.closest("tr").hidden) input.checked = checked;
        });
        sync();
      });
    sync();
  });
})();

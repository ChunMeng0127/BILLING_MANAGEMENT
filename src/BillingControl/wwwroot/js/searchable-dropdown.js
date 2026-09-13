(() => {
  const normalize = (value) => String(value ?? "").trim();
  const comparisonValue = (value) => normalize(value).toLowerCase();

  const uniqueOptions = (options) => {
    const seen = new Set();
    return [...(options ?? [])].map(normalize).filter((option) => {
      if (!option || seen.has(comparisonValue(option))) return false;
      seen.add(comparisonValue(option));
      return true;
    });
  };

  const orderedOptions = (options, currentValue) => {
    const available = uniqueOptions(options);
    const current = normalize(currentValue);
    if (!current) return available;
    return [current, ...available.filter((option) => comparisonValue(option) !== comparisonValue(current))];
  };

  const filterOptions = (options, query) => {
    const search = comparisonValue(query);
    if (!search) return uniqueOptions(options);
    return uniqueOptions(options).filter((option) => comparisonValue(option).includes(search));
  };

  const moveActiveIndex = (currentIndex, direction, optionCount) => {
    if (optionCount <= 0) return -1;
    const current = Number.isInteger(currentIndex) && currentIndex >= 0 ? currentIndex : 0;
    return (current + direction + optionCount) % optionCount;
  };

  const canonicalOption = (value, options) => {
    const normalized = comparisonValue(value);
    return uniqueOptions(options).find((option) => comparisonValue(option) === normalized) ?? null;
  };

  const isValidFixedChoice = (value, options) => canonicalOption(value, options) !== null;

  const searchableDropdownApi = {
    normalize,
    uniqueOptions,
    orderedOptions,
    filterOptions,
    moveActiveIndex,
    canonicalOption,
    isValidFixedChoice,
  };

  if (typeof module === "object" && module.exports) module.exports = searchableDropdownApi;
  if (typeof document === "undefined") return;

  let nextMenuId = 0;
  const instances = new Set();

  const readOptions = (input) => {
    if (input.dataset.options) {
      try {
        const parsed = JSON.parse(input.dataset.options);
        if (Array.isArray(parsed)) return uniqueOptions(parsed);
      } catch {
        // Invalid optional data attributes leave the component with no choices.
      }
    }

    const listId = input.getAttribute("list");
    const list = listId ? document.getElementById(listId) : null;
    return uniqueOptions(list ? [...list.options].map((option) => option.value) : []);
  };

  const setExpanded = (state, expanded) => {
    state.open = expanded;
    state.input.setAttribute("aria-expanded", String(expanded));
    state.menu.hidden = !expanded;
    if (!expanded) state.input.removeAttribute("aria-activedescendant");
  };

  const positionMenu = (state) => {
    if (!state.open) return;
    const inputRect = state.input.getBoundingClientRect();
    const width = Math.max(inputRect.width, 180);
    const left = Math.min(
      Math.max(inputRect.left, 8),
      Math.max(8, window.innerWidth - width - 8),
    );
    const menuHeight = Math.min(state.menu.scrollHeight || 240, 240);
    const roomBelow = window.innerHeight - inputRect.bottom - 8;
    const roomAbove = inputRect.top - 8;
    const openAbove = roomBelow < menuHeight && roomAbove > roomBelow;
    const top = openAbove
      ? Math.max(8, inputRect.top - menuHeight - 4)
      : Math.min(inputRect.bottom + 4, Math.max(8, window.innerHeight - menuHeight - 8));
    state.menu.style.left = `${left}px`;
    state.menu.style.top = `${top}px`;
    state.menu.style.width = `${Math.min(width, window.innerWidth - 16)}px`;
  };

  const updateActiveDescendant = (state) => {
    const active = state.menu.querySelector('[data-searchable-dropdown-option][data-active="true"]');
    if (active) state.input.setAttribute("aria-activedescendant", active.id);
    else state.input.removeAttribute("aria-activedescendant");
  };

  const render = (state, query = "") => {
    const current = normalize(state.input.value);
    const allOptions = normalize(query)
      ? uniqueOptions(state.options)
      : orderedOptions(state.options, current);
    let options = filterOptions(allOptions, query);
    const customValue = state.allowCustom && current && current !== state.customTrigger && !canonicalOption(current, state.options);

    if (state.allowCustom && normalize(query) && !options.length && customValue) {
      options = [current];
      state.customChoice = true;
    } else {
      state.customChoice = false;
    }

    state.visibleOptions = options;
    if (state.activeIndex < 0 || state.activeIndex >= options.length) state.activeIndex = options.length ? 0 : -1;
    state.menu.replaceChildren();

    if (!options.length) {
      const empty = document.createElement("div");
      empty.className = "searchable-dropdown-empty";
      empty.textContent = state.allowCustom ? "Type a document name or choose a suggestion." : "No matching options.";
      state.menu.append(empty);
    } else {
      options.forEach((option, index) => {
        const optionElement = document.createElement("button");
        optionElement.type = "button";
        optionElement.tabIndex = -1;
        optionElement.id = `${state.menu.id}-option-${index}`;
        optionElement.className = "searchable-dropdown-option";
        optionElement.setAttribute("role", "option");
        optionElement.setAttribute("aria-selected", String(comparisonValue(option) === comparisonValue(current)));
        optionElement.dataset.searchableDropdownOption = "true";
        optionElement.dataset.value = option;
        optionElement.dataset.active = String(index === state.activeIndex);
        if (index === state.activeIndex) optionElement.classList.add("is-active");

        const marker = document.createElement("span");
        marker.className = "searchable-dropdown-marker";
        marker.setAttribute("aria-hidden", "true");
        marker.textContent = comparisonValue(option) === comparisonValue(current) ? "✓" : "";
        const label = document.createElement("span");
        label.textContent = state.customChoice && index === 0 ? `Use “${option}”` : option;
        optionElement.append(marker, label);
        state.menu.append(optionElement);
      });
    }

    updateActiveDescendant(state);
    positionMenu(state);
  };

  const setActive = (state, index) => {
    state.activeIndex = index;
    [...state.menu.querySelectorAll('[data-searchable-dropdown-option]')].forEach((option, optionIndex) => {
      const active = optionIndex === index;
      option.dataset.active = String(active);
      option.classList.toggle("is-active", active);
    });
    updateActiveDescendant(state);
    state.menu.querySelector('[data-active="true"]')?.scrollIntoView({ block: "nearest" });
  };

  const dispatchChange = (input) => input.dispatchEvent(new Event("change", { bubbles: true }));

  const choose = (state, value) => {
    if (value === state.customTrigger) {
      state.input.value = "";
      state.selectedValue = "";
      state.input.placeholder = state.customPlaceholder;
      close(state);
      state.input.focus();
      state.input.dispatchEvent(new Event("input", { bubbles: true }));
      return;
    }

    const selected = state.customChoice ? value : canonicalOption(value, state.options) ?? value;
    state.input.value = selected;
    state.selectedValue = selected;
    state.input.placeholder = state.defaultPlaceholder;
    close(state);
    dispatchChange(state.input);
  };

  const close = (state, restoreFixedValue = false) => {
    if (restoreFixedValue && !state.allowCustom) {
      state.input.value = state.selectedValue;
      state.input.placeholder = state.defaultPlaceholder;
    }
    setExpanded(state, false);
    state.stopPositioning?.();
    state.stopPositioning = null;
  };

  const open = (state) => {
    state.activeIndex = 0;
    setExpanded(state, true);
    render(state, "");
    const reposition = () => positionMenu(state);
    window.addEventListener("resize", reposition);
    window.addEventListener("scroll", reposition, true);
    state.stopPositioning = () => {
      window.removeEventListener("resize", reposition);
      window.removeEventListener("scroll", reposition, true);
    };
  };

  const enhance = (root) => {
    const candidates = root?.matches?.("[data-searchable-dropdown]")
      ? [root]
      : [...(root?.querySelectorAll?.("[data-searchable-dropdown]") ?? [])];
    candidates.forEach((input) => {
      if (input.dataset.searchableDropdownReady === "true") return;
      input.dataset.searchableDropdownReady = "true";
      const mode = input.dataset.searchableDropdown || "select";
      const state = {
        input,
        mode,
        allowCustom: mode === "combobox" || input.dataset.allowCustom === "true",
        options: readOptions(input),
        selectedValue: "",
        defaultPlaceholder: input.getAttribute("placeholder") ?? "",
        customTrigger: normalize(input.dataset.customTrigger),
        customPlaceholder: input.dataset.customPlaceholder || "Type a document name",
        open: false,
        activeIndex: -1,
        visibleOptions: [],
        customChoice: false,
        menu: document.createElement("div"),
      };
      state.customTrigger = state.customTrigger || "Other Documents";
      const initialValue = normalize(input.value);
      state.selectedValue = state.allowCustom ? initialValue : canonicalOption(initialValue, state.options) ?? "";
      if (!state.allowCustom && initialValue !== state.selectedValue) input.value = state.selectedValue;
      state.menu.id = `searchable-dropdown-${++nextMenuId}`;
      state.menu.className = "searchable-dropdown-menu";
      state.menu.setAttribute("role", "listbox");
      state.menu.hidden = true;
      document.body.append(state.menu);
      input.setAttribute("role", "combobox");
      input.setAttribute("aria-expanded", "false");
      input.setAttribute("aria-autocomplete", "list");
      input.setAttribute("aria-haspopup", "listbox");
      input.setAttribute("aria-controls", state.menu.id);
      const listId = input.getAttribute("list");
      input.removeAttribute("list");
      const datalist = document.getElementById(listId ?? "");
      datalist?.setAttribute("hidden", "hidden");

      if (comparisonValue(input.value) === comparisonValue(state.customTrigger)) {
        input.value = "";
        state.selectedValue = "";
        input.placeholder = state.customPlaceholder;
      }

      input.addEventListener("focus", () => {
        if (!state.open) open(state);
      });
      input.addEventListener("click", () => {
        if (state.open) {
          state.activeIndex = 0;
          render(state, "");
        }
        else open(state);
      });
      input.addEventListener("input", () => {
        if (!state.open) open(state);
        else {
          state.activeIndex = 0;
          render(state, input.value);
        }
      });
      input.addEventListener("keydown", (event) => {
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
          event.preventDefault();
          if (!state.open) open(state);
          else setActive(state, moveActiveIndex(state.activeIndex, event.key === "ArrowDown" ? 1 : -1, state.visibleOptions.length));
        } else if (event.key === "Enter" && state.open && state.activeIndex >= 0) {
          event.preventDefault();
          choose(state, state.visibleOptions[state.activeIndex]);
        } else if (event.key === "Escape" && state.open) {
          event.preventDefault();
          close(state, true);
        }
      });
      input.addEventListener("blur", () => {
        window.setTimeout(() => {
          if (!state.wrapper.contains(document.activeElement) && document.activeElement !== state.menu) close(state, true);
        }, 0);
      });
      state.menu.addEventListener("mousedown", (event) => event.preventDefault());
      state.menu.addEventListener("mousemove", (event) => {
        const option = event.target.closest("[data-searchable-dropdown-option]");
        if (option) setActive(state, Number(option.id.split("-option-").pop()));
      });
      state.menu.addEventListener("click", (event) => {
        const option = event.target.closest("[data-searchable-dropdown-option]");
        if (option) choose(state, option.dataset.value);
      });
      state.wrapper = input.parentElement;
      if (!state.wrapper?.matches("[data-searchable-dropdown-wrapper]")) {
        const wrapper = document.createElement("div");
        wrapper.className = "searchable-dropdown";
        wrapper.dataset.searchableDropdownWrapper = "true";
        input.parentNode.insertBefore(wrapper, input);
        wrapper.append(input);
        state.wrapper = wrapper;
      }
      instances.add(state);

      input.form?.addEventListener("submit", () => {
        if (!state.allowCustom) {
          const selected = canonicalOption(input.value, state.options);
          input.value = selected ?? state.selectedValue;
        }
      });
    });
  };

  searchableDropdownApi.enhance = enhance;
  window.BillingControlSearchableDropdown = searchableDropdownApi;
  enhance(document);
  new MutationObserver((mutations) => {
    mutations.forEach((mutation) => mutation.addedNodes.forEach((node) => {
      if (node.nodeType === Node.ELEMENT_NODE) enhance(node);
    }));
  }).observe(document.body, { childList: true, subtree: true });
  document.addEventListener("click", (event) => {
    instances.forEach((state) => {
      if (state.open && !state.wrapper?.contains(event.target) && !state.menu.contains(event.target)) close(state, true);
    });
  });
})();

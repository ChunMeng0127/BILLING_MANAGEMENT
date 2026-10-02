(() => {
  if (typeof document === "undefined") return;

  // Razor keeps <details> markup simple; the active route opens its parent group here.
  const groups = [...document.querySelectorAll("[data-nav-group]")];
  const activeGroup = groups.find((group) => group.querySelector(".nav-link.active"));
  if (activeGroup) {
    groups.forEach((group) => {
      group.open = group === activeGroup;
    });
  }

  // Preserve the complete checkbox selection while searching Excel-style values.
  // site.js keeps every value checkbox in the DOM; this layer only hides non-matches.
  document.addEventListener(
    "input",
    (event) => {
      const search = event.target.closest?.(".filter-value-search");
      if (!search) return;
      event.stopImmediatePropagation();

      const menu = search.closest(".excel-filter-menu");
      const list = menu?.querySelector(".filter-values");
      if (!list) return;

      const needle = search.value.trim().toLowerCase();
      const labels = [...list.querySelectorAll("label")];
      labels.forEach((label) => {
        const valueInput = label.querySelector("input[data-value]");
        if (!valueInput) {
          // Avoid ambiguous Select All semantics while a search subset is shown.
          label.hidden = Boolean(needle);
          return;
        }
        const text = (valueInput.dataset.value || "(Blanks)").toLowerCase();
        label.hidden = Boolean(needle) && !text.includes(needle);
      });
    },
    true,
  );
})();

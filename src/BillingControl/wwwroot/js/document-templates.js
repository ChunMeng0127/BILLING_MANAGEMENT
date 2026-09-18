(() => {
  const otherDocuments = "Other Documents";

  const documentInputValue = (value) =>
    value?.trim() === otherDocuments ? "" : value;

  if (typeof module === "object" && module.exports)
    module.exports = { otherDocuments, documentInputValue };
  if (typeof document === "undefined") return;

  const addButtons = document.querySelectorAll("[data-template-add-row]");
  addButtons.forEach((button) => {
    button.addEventListener("click", () => {
      const target = document.querySelector(button.dataset.templateTarget);
      const prototype = document.getElementById("template-item-prototype");
      if (!target || !prototype) return;
      const index = Number(target.dataset.nextIndex || target.children.length);
      target.insertAdjacentHTML("beforeend", prototype.innerHTML.replaceAll("__index__", String(index)));
      const row = target.lastElementChild;
      const input = row?.querySelector("[data-document-selection]");
      if (input) {
        input.setAttribute("placeholder", otherDocuments);
        window.BillingControlSearchableDropdown?.enhance(row);
      }
      target.dataset.nextIndex = String(index + 1);
    });
  });

  document.addEventListener("click", (event) => {
    const button = event.target.closest("[data-template-remove-row]");
    if (!button) return;
    const row = button.closest("tr");
    if (row) row.remove();
  });
})();

(() => {
  const otherDocuments = "Other Documents";

  const documentInputValue = (value) =>
    value?.trim() === otherDocuments ? "" : value;

  const clearOtherDocumentHint = (input) => {
    const nextValue = documentInputValue(input.value);
    if (nextValue !== input.value) input.value = nextValue;
  };

  if (typeof module === "object" && module.exports)
    module.exports = { otherDocuments, documentInputValue };
  if (typeof document === "undefined") return;

  document.querySelectorAll("[data-document-selection]").forEach((input) => {
    input.placeholder = otherDocuments;
    clearOtherDocumentHint(input);
  });

  const addButtons = document.querySelectorAll("[data-template-add-row]");
  addButtons.forEach((button) => {
    button.addEventListener("click", () => {
      const target = document.querySelector(button.dataset.templateTarget);
      const prototype = document.getElementById("template-item-prototype");
      if (!target || !prototype) return;
      const index = Number(target.dataset.nextIndex || target.children.length);
      target.insertAdjacentHTML("beforeend", prototype.innerHTML.replaceAll("__index__", String(index)));
      const row = target.lastElementChild;
      row?.querySelector("[data-document-selection]")?.setAttribute("placeholder", otherDocuments);
      target.dataset.nextIndex = String(index + 1);
    });
  });

  document.addEventListener("input", (event) => {
    const input = event.target.closest("[data-document-selection]");
    if (input) clearOtherDocumentHint(input);
  });

  document.addEventListener("change", (event) => {
    const input = event.target.closest("[data-document-selection]");
    if (input) clearOtherDocumentHint(input);
  });

  document.addEventListener("click", (event) => {
    const button = event.target.closest("[data-template-remove-row]");
    if (!button) return;
    const row = button.closest("tr");
    if (row) row.remove();
  });
})();

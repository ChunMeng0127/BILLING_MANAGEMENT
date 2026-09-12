(() => {
  const otherDocuments = "Other Documents";

  const updateOtherDocumentField = (row) => {
    const select = row.querySelector("[data-document-selection]");
    const wrapper = row.querySelector("[data-other-document-wrapper]");
    const input = row.querySelector("[data-other-document-name]");
    if (!select || !wrapper || !input) return;

    const isOtherDocument = select.value === otherDocuments;
    wrapper.hidden = !isOtherDocument;
    input.disabled = !isOtherDocument;
    input.required = isOtherDocument;
    if (!isOtherDocument) input.value = "";
  };

  document.querySelectorAll("[data-document-selection]").forEach((select) => {
    const row = select.closest("tr") || select.parentElement;
    if (row) updateOtherDocumentField(row);
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
      if (row) updateOtherDocumentField(row);
      target.dataset.nextIndex = String(index + 1);
    });
  });

  document.addEventListener("change", (event) => {
    const select = event.target.closest("[data-document-selection]");
    if (!select) return;
    const row = select.closest("tr") || select.parentElement;
    if (row) updateOtherDocumentField(row);
  });

  document.addEventListener("click", (event) => {
    const button = event.target.closest("[data-template-remove-row]");
    if (!button) return;
    const row = button.closest("tr");
    if (row) row.remove();
  });
})();

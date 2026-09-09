(() => {
  const money = (value) => Number(value || 0);
  const calculateReceiptTotal = (values) =>
    values.reduce((total, value) => total + money(value), 0);
  const formatTotal = (value) => `RM ${value.toFixed(2)}`;
  const updateReceiptTotal = (root) => {
    const inputs = [...root.querySelectorAll("[data-receipt-allocation]")];
    const output = root.querySelector("[data-receipt-total]");
    if (output) output.textContent = formatTotal(calculateReceiptTotal(inputs.map((input) => input.value)));
  };
  if (typeof module === "object" && module.exports)
    module.exports = { calculateReceiptTotal, formatTotal };
  if (typeof document === "undefined") return;
  const root = document.querySelector(".receipt-edit-form");
  if (!root) return;
  root.querySelectorAll("[data-receipt-allocation]").forEach((input) => input.addEventListener("input", () => updateReceiptTotal(root)));
  updateReceiptTotal(root);
})();

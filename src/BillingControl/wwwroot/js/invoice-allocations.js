(function (root, factory) {
  const api = factory();
  root.InvoiceAllocationGrid = api;
  if (typeof module === "object" && module.exports) module.exports = api;
})(typeof globalThis === "undefined" ? this : globalThis, function () {
  const flowKeys = {
    AccountingFirmToCustomer: "customer",
    ManagerToAccountingFirm: "manager",
    LcmToManager: "lcm",
  };

  const stateFor = (dataset, flow) => {
    const key = flowKeys[flow];
    if (!key) throw new Error("Unknown invoice flow.");
    const cap = Number(dataset[`${key}Cap`]);
    const allocated = Number(dataset[`${key}Allocated`]);
    const remaining = Math.max(0, cap - allocated);
    return { cap, allocated, remaining, eligible: remaining > 0 };
  };

  const updateRow = (row, flow, format, clearInput = false) => {
    const state = stateFor(row.dataset, flow);
    row.querySelector("[data-flow-cap]").textContent = format(state.cap);
    row.querySelector("[data-flow-allocated]").textContent = format(
      state.allocated,
    );
    row.querySelector("[data-flow-remaining]").textContent = format(
      state.remaining,
    );
    row.dataset.gridEligible = String(state.eligible);
    const input = row.querySelector("input[type=number]");
    input.max = state.remaining.toFixed(2);
    input.disabled = !state.eligible;
    if (clearInput) input.value = "";
    return state;
  };

  const updateTable = (table, flow, format, clearInputs = false) => {
    const states = [...table.querySelectorAll(".invoice-allocation-row")].map(
      (row) => updateRow(row, flow, format, clearInputs),
    );
    return {
      states,
      eligibleCount: states.filter((state) => state.eligible).length,
    };
  };

  const initialize = (documentRef, windowRef) => {
    const flow = documentRef.getElementById("invoice-flow");
    const table = documentRef.getElementById("invoice-allocation-grid");
    if (!flow || !table) return;
    const numberFormat = new Intl.NumberFormat("en-MY", {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
    let selectedFlow = flow.value;
    const update = (clearInputs) => {
      updateTable(
        table,
        flow.value,
        (value) => numberFormat.format(value),
        clearInputs,
      );
      table.dispatchEvent(new windowRef.Event("grid:refresh"));
    };
    flow.addEventListener("change", () => {
      const flowChanged = flow.value !== selectedFlow;
      selectedFlow = flow.value;
      update(flowChanged);
    });
    windowRef.addEventListener("pageshow", () => update(false));
    update(false);
  };

  if (typeof document !== "undefined" && typeof window !== "undefined")
    initialize(document, window);

  return { stateFor, updateRow, updateTable, initialize };
});

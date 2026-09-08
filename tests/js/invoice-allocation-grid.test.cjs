const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");

const invoiceGrid = require(path.resolve(
  __dirname,
  "../../src/BillingControl/wwwroot/js/invoice-allocations.js",
));
const dataGrid = require(path.resolve(
  __dirname,
  "../../src/BillingControl/wwwroot/js/site.js",
));

const makeRow = (overrides = {}, value = "") => {
  const fields = {
    cap: { textContent: "" },
    allocated: { textContent: "" },
    remaining: { textContent: "" },
  };
  const input = { value, max: "", disabled: false };
  return {
    dataset: {
      customerCap: "900.00",
      customerAllocated: "900.00",
      managerCap: "225.00",
      managerAllocated: "0.00",
      lcmCap: "360.00",
      lcmAllocated: "0.00",
      ...overrides,
    },
    input,
    fields,
    querySelector(selector) {
      if (selector === "[data-flow-cap]") return fields.cap;
      if (selector === "[data-flow-allocated]") return fields.allocated;
      if (selector === "[data-flow-remaining]") return fields.remaining;
      if (selector === "input[type=number]") return input;
      throw new Error(`Unexpected selector: ${selector}`);
    },
  };
};

const format = (value) => value.toFixed(2);

test("a customer-fully-allocated row is hidden but remains eligible for Manager and LCM flows", () => {
  const row = makeRow();
  let state = invoiceGrid.updateRow(row, "AccountingFirmToCustomer", format);
  assert.equal(state.eligible, false);
  assert.equal(row.dataset.gridEligible, "false");
  assert.equal(row.input.disabled, true);
  assert.equal(row.input.max, "0.00");

  state = invoiceGrid.updateRow(row, "ManagerToAccountingFirm", format);
  assert.equal(state.eligible, true);
  assert.equal(row.dataset.gridEligible, "true");
  assert.equal(row.input.disabled, false);
  assert.equal(row.input.max, "225.00");

  state = invoiceGrid.updateRow(row, "LcmToManager", format);
  assert.equal(state.eligible, true);
  assert.equal(row.input.max, "360.00");
});

test("a partially allocated row remains visible and its input max equals the selected-flow remaining amount", () => {
  const row = makeRow({ customerAllocated: "400.00" });
  const state = invoiceGrid.updateRow(
    row,
    "AccountingFirmToCustomer",
    format,
  );
  assert.equal(state.remaining, 500);
  assert.equal(state.eligible, true);
  assert.equal(row.input.max, "500.00");
  assert.equal(row.input.disabled, false);
});

test("changing to a flow with no remaining amount clears and disables a stale allocation", () => {
  const row = makeRow(
    { customerAllocated: "0.00", managerAllocated: "225.00" },
    "125.00",
  );
  invoiceGrid.updateRow(row, "AccountingFirmToCustomer", format);
  assert.equal(row.input.value, "125.00");
  invoiceGrid.updateRow(row, "ManagerToAccountingFirm", format);
  assert.equal(row.dataset.gridEligible, "false");
  assert.equal(row.input.value, "");
  assert.equal(row.input.disabled, true);
});

test("the data-grid count excludes flow-ineligible rows", () => {
  const rows = [makeRow(), makeRow({ customerAllocated: "400.00" })];
  rows.forEach((row) =>
    invoiceGrid.updateRow(row, "AccountingFirmToCustomer", format),
  );
  const eligible = dataGrid.eligibleGridRows(rows);
  assert.equal(eligible.length, 1);
  assert.equal(dataGrid.gridRowCountText(eligible.length, eligible.length), "1 of 1 Rows shown");
});

test("an all-fully-allocated flow uses the invoice-specific empty-state message", () => {
  const rows = [makeRow(), makeRow()];
  const table = {
    querySelectorAll: () => rows,
  };
  const result = invoiceGrid.updateTable(
    table,
    "AccountingFirmToCustomer",
    format,
  );
  assert.equal(result.eligibleCount, 0);
  assert.equal(
    dataGrid.gridEmptyMessage(result.eligibleCount, {
      emptyEligibleMessage:
        "No billing records have a remaining amount for this invoice flow.",
    }),
    "No billing records have a remaining amount for this invoice flow.",
  );
});

test("external page initialization updates eligibility and refreshes the data grid on flow changes", () => {
  const row = makeRow();
  const flowListeners = {};
  const windowListeners = {};
  const events = [];
  const flow = {
    value: "AccountingFirmToCustomer",
    addEventListener(name, handler) {
      flowListeners[name] = handler;
    },
  };
  const table = {
    querySelectorAll: () => [row],
    dispatchEvent: (event) => events.push(event.type),
  };
  const documentRef = {
    getElementById: (id) =>
      id === "invoice-flow" ? flow : id === "invoice-allocation-grid" ? table : null,
  };
  const windowRef = {
    Event: class {
      constructor(type) {
        this.type = type;
      }
    },
    addEventListener(name, handler) {
      windowListeners[name] = handler;
    },
  };

  invoiceGrid.initialize(documentRef, windowRef);
  assert.equal(row.dataset.gridEligible, "false");
  assert.deepEqual(events, ["grid:refresh"]);
  flow.value = "ManagerToAccountingFirm";
  flowListeners.change();
  assert.equal(row.dataset.gridEligible, "true");
  assert.deepEqual(events, ["grid:refresh", "grid:refresh"]);
  assert.equal(typeof windowListeners.pageshow, "function");
});

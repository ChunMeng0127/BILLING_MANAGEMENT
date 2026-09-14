const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");

const dataGrid = require(path.resolve(
  __dirname,
  "../../src/BillingControl/wwwroot/js/site.js",
));

const header = (title, { hidden = false, noExport = false } = {}) => ({
  hidden,
  textContent: title,
  dataset: { title },
  hasAttribute(name) {
    return name === "data-noexport" && noExport;
  },
});

const row = (values, { hidden = false, eligible = true } = {}) => ({
  hidden,
  dataset: { gridEligible: String(eligible) },
  cells: values.map((textContent) => ({ textContent })),
});

test("CSV escapes commas, quotes, and embedded newlines with a UTF-8 BOM", () => {
  const csv = dataGrid.buildGridCsv(
    [header("Customer"), header("Notes")],
    [row(["ACME, Sdn. Bhd.", 'He said "hello"\r\nnext'])],
  );

  assert.equal(
    csv,
    '\uFEFFCustomer,Notes\r\n"ACME, Sdn. Bhd.","He said ""hello""\r\nnext"\r\n',
  );
});

test("export excludes hidden columns, data-noexport columns, and hidden/ineligible rows", () => {
  const csv = dataGrid.buildGridCsv(
    [
      header("Record"),
      header("Customer", { hidden: true }),
      header("Amount (RM)"),
      header("Actions", { noExport: true }),
    ],
    [
      row(["B-00001", "Visible customer", "1,234.50", "View"]),
      row(["B-00002", "Filtered customer", "2,345.50", "View"], {
        hidden: true,
      }),
      row(["B-00003", "Ineligible customer", "3,456.50", "View"], {
        eligible: false,
      }),
    ],
  );

  assert.equal(csv, "\uFEFFRecord,Amount (RM)\r\nB-00001,\"1,234.50\"\r\n");
});

test("export preserves the current DOM row order", () => {
  const csv = dataGrid.buildGridCsv(
    [header("Record")],
    [row(["B-00003"]), row(["B-00001"]), row(["B-00002"])],
  );

  assert.equal(csv, "\uFEFFRecord\r\nB-00003\r\nB-00001\r\nB-00002\r\n");
});

test("formula-like text is protected without changing numeric or date values", () => {
  assert.equal(dataGrid.protectCsvFormula("=SUM(A1:A2)"), "'=SUM(A1:A2)");
  assert.equal(
    dataGrid.protectCsvFormula("+cmd|' /C calc'!A0"),
    "'+cmd|' /C calc'!A0",
  );
  assert.equal(dataGrid.protectCsvFormula("-cmd"), "'-cmd");
  assert.equal(dataGrid.protectCsvFormula("@SUM(A1)"), "'@SUM(A1)");
  assert.equal(dataGrid.protectCsvFormula("-123.45"), "-123.45");
  assert.equal(dataGrid.protectCsvFormula("1,234.50"), "1,234.50");
  assert.equal(dataGrid.protectCsvFormula("2026-09-14"), "2026-09-14");
});

test("export is opt-in and unrelated grids have no export key", () => {
  assert.equal(
    dataGrid.exportGridKey({ exportCsv: " billing-records " }),
    "billing-records",
  );
  assert.equal(dataGrid.exportGridKey({}), null);
  assert.equal(dataGrid.exportGridKey({ exportCsv: "   " }), null);
});

test("empty current results disable export", () => {
  assert.equal(dataGrid.gridExportDisabled([]), true);
  assert.equal(dataGrid.gridExportDisabled([row(["B-00001"])]), false);
});

test("filename uses the local browser date and time format", () => {
  const localDate = new Date(2026, 8, 14, 7, 5);
  assert.equal(
    dataGrid.gridExportFilename("billing-records", localDate),
    "billing-records-20260914-0705.csv",
  );
});

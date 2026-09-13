const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");

const dropdown = require(path.resolve(
  __dirname,
  "../../src/BillingControl/wwwroot/js/searchable-dropdown.js",
));

const documents = [
  "Bank Statement",
  "Sales Invoice",
  "Purchase Invoice",
  "Expenses Invoice",
  "Staff Claim",
  "Payroll Report",
  "Payment Voucher",
  "Official Receipt",
  "Other Documents",
];

test("opening a populated field puts the current value first without duplicates", () => {
  const ordered = dropdown.orderedOptions(documents, "Expenses Invoice");

  assert.equal(ordered[0], "Expenses Invoice");
  assert.equal(ordered.filter((value) => value === "Expenses Invoice").length, 1);
  assert.deepEqual(ordered.slice(1), documents.filter((value) => value !== "Expenses Invoice"));
});

test("custom and historical current values remain visible before supplied choices", () => {
  assert.deepEqual(
    dropdown.orderedOptions(documents, "Loan Statement").slice(0, 3),
    ["Loan Statement", "Bank Statement", "Sales Invoice"],
  );
  assert.equal(dropdown.orderedOptions(documents, "Payment / Receipt")[0], "Payment / Receipt");
});

test("filtering is case-insensitive and uses contains matching", () => {
  assert.deepEqual(dropdown.filterOptions(documents, "invoice"), [
    "Sales Invoice",
    "Purchase Invoice",
    "Expenses Invoice",
  ]);
  assert.deepEqual(dropdown.filterOptions(documents, "pay"), ["Payroll Report", "Payment Voucher"]);
  assert.deepEqual(dropdown.filterOptions(documents, "RECEIPT"), ["Official Receipt"]);
});

test("keyboard movement cycles through visible options and selection is canonical", () => {
  assert.equal(dropdown.moveActiveIndex(0, 1, 3), 1);
  assert.equal(dropdown.moveActiveIndex(0, -1, 3), 2);
  assert.equal(dropdown.moveActiveIndex(-1, 1, 3), 1);
  assert.equal(dropdown.moveActiveIndex(0, 1, 0), -1);
  assert.equal(dropdown.canonicalOption("sales invoice", documents), "Sales Invoice");
  assert.equal(dropdown.isValidFixedChoice("Sales Invoice", documents), true);
  assert.equal(dropdown.isValidFixedChoice("Loan Statement", documents), false);
});

test("combobox values may be custom while the Other Documents trigger is not a persisted value", () => {
  assert.equal(dropdown.normalize("  Loan Statement  "), "Loan Statement");
  assert.equal(dropdown.canonicalOption("Loan Statement", documents), null);
  assert.equal(dropdown.canonicalOption("Other Documents", documents), "Other Documents");
});

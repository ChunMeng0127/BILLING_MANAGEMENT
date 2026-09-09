const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");

const receiptEdit = require(path.resolve(
  __dirname,
  "../../src/BillingControl/wwwroot/js/receipt-edit.js",
));

test("receipt total is calculated from corrected existing allocation amounts", () => {
  assert.equal(receiptEdit.calculateReceiptTotal(["1800.00", "1200.00"]), 3000);
  assert.equal(receiptEdit.calculateReceiptTotal(["2800.00"]), 2800);
  assert.equal(receiptEdit.formatTotal(4700), "RM 4700.00");
});

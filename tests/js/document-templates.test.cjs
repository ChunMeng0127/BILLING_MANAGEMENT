const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");

const documents = require(path.resolve(
  __dirname,
  "../../src/BillingControl/wwwroot/js/document-templates.js",
));

test("the Other Documents option is a same-field hint", () => {
  assert.equal(documents.documentInputValue("Other Documents"), "");
  assert.equal(documents.documentInputValue("  Other Documents  "), "");
  assert.equal(documents.documentInputValue("  Loan Statement  "), "  Loan Statement  ");
});

const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const site = require(path.resolve(
  __dirname,
  "../../src/BillingControl/wwwroot/js/site.js",
));

test("sidebar uses a stable preference key and separates desktop from drawer layout", () => {
  assert.equal(site.sidebarStorageKey, "billing-control.sidebar-collapsed");
  assert.equal(site.sidebarBreakpoint, 900);
  assert.equal(site.sidebarMode(1920), "desktop");
  assert.equal(site.sidebarMode(900), "desktop");
  assert.equal(site.sidebarMode(899), "mobile");
  assert.equal(site.sidebarMode(390), "mobile");
});

test("only an explicit saved preference collapses the desktop sidebar", () => {
  assert.equal(site.savedSidebarPreference("true"), true);
  assert.equal(site.savedSidebarPreference("false"), false);
  assert.equal(site.savedSidebarPreference(null), false);
  assert.equal(site.savedSidebarPreference("unexpected"), false);
  assert.equal(site.desktopSidebarLabel(true), "Hide navigation");
  assert.equal(site.desktopSidebarLabel(false), "Show navigation");
});

test("sidebar groups keep navigation compact", () => {
  assert.deepEqual(site.navGroupNames, [
    "home",
    "billing",
    "work",
    "documents",
    "directory",
    "reports",
    "administration",
  ]);
  const home = { open: true };
  const billing = { open: true };
  site.keepOneNavGroupOpen([home, billing], home);
  assert.equal(home.open, true);
  assert.equal(billing.open, false);
});


test("data grid pagination helpers produce stable DataTables-style ranges", () => {
  assert.equal(site.gridPageCount(0, 25), 1);
  assert.equal(site.gridPageCount(26, 25), 2);
  assert.equal(site.gridPageCount(200, 0), 1);
  assert.equal(site.gridRangeText(1, 25, 77), "Showing 1–25 of 77");
  assert.equal(site.gridRangeText(4, 25, 77), "Showing 76–77 of 77");
  assert.equal(site.gridRangeText(1, 25, 0), "Showing 0 records");
  assert.equal(site.gridRangeText(1, 0, 77), "Showing 1–77 of 77");
});


test("DataTables integration mode keeps transactional grids safe", () => {
  const source = fs.readFileSync(path.join(__dirname, "../../src/BillingControl/wwwroot/js/site.js"), "utf8");
  assert.match(source, /window\.DataTable/);
  assert.match(source, /dataTableTransactional/);
  assert.match(source, /paging: false/);
  assert.match(source, /searchPlaceholder: "Search records…"/);
});

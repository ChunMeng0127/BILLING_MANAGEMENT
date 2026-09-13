const test = require("node:test");
const assert = require("node:assert/strict");
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

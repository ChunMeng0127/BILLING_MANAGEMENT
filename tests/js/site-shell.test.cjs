const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");

const site = require(path.resolve(
  __dirname,
  "../../src/BillingControl/wwwroot/js/site.js",
));

test("sidebar uses the approved 900px compact breakpoint", () => {
  assert.equal(site.sidebarStorageKey, "billing-control.sidebar-collapsed");
  assert.equal(site.sidebarBreakpoint, 900);
  assert.equal(site.sidebarMode(1920), "desktop");
  assert.equal(site.sidebarMode(901), "desktop");
  assert.equal(site.sidebarMode(900), "compact");
  assert.equal(site.sidebarMode(390), "compact");
});

test("desktop collapse preference is explicit and reversible", () => {
  assert.equal(site.savedSidebarPreference("true"), true);
  assert.equal(site.savedSidebarPreference("false"), false);
  assert.equal(site.savedSidebarPreference(null), false);
  assert.equal(site.savedSidebarPreference("unexpected"), false);
  assert.equal(site.desktopSidebarLabel(true), "Hide navigation");
  assert.equal(site.desktopSidebarLabel(false), "Show navigation");
});

test("navigation groups match the rebuilt information architecture", () => {
  assert.deepEqual(site.navGroupNames, [
    "billing",
    "work",
    "documents",
    "directory",
    "administration",
  ]);
  const billing = { open: true };
  const work = { open: true };
  site.keepOneNavGroupOpen([billing, work], billing);
  assert.equal(billing.open, true);
  assert.equal(work.open, false);
});

const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");

const site = require(path.resolve(
  __dirname,
  "../../src/BillingControl/wwwroot/js/site.js",
));

const makeClassList = (...initial) => {
  const values = new Set(initial);
  return {
    add: (value) => values.add(value),
    contains: (value) => values.has(value),
    remove: (value) => values.delete(value),
    toggle: (value, force) => {
      const next = force === undefined ? !values.has(value) : force;
      if (next) values.add(value);
      else values.delete(value);
      return next;
    },
  };
};

const makeElement = (overrides = {}) => {
  const listeners = {};
  const attributes = {};
  return {
    classList: makeClassList(),
    addEventListener(name, handler) {
      listeners[name] = handler;
    },
    getAttribute(name) {
      return attributes[name] ?? null;
    },
    setAttribute(name, value) {
      attributes[name] = value;
    },
    listeners,
    ...overrides,
  };
};

test("the clean-room shell uses the responsive drawer breakpoint", () => {
  assert.equal(site.navigationBreakpoint, 1080);
  assert.equal(site.navigationMode(1081), "desktop");
  assert.equal(site.navigationMode(1080), "mobile");
  assert.equal(site.navigationMode(390), "mobile");
});

test("the clean-room shell opens and closes its mobile navigation drawer", () => {
  const shell = makeElement();
  const body = makeElement();
  const openButton = makeElement();
  const closeButton = makeElement();
  const scrim = makeElement();
  const listeners = {};
  const documentRef = {
    body,
    addEventListener(name, handler) {
      listeners[name] = handler;
    },
    querySelector(selector) {
      return {
        "[data-app-shell]": shell,
        "[data-sidebar-scrim]": scrim,
      }[selector] || null;
    },
    querySelectorAll(selector) {
      if (selector === "[data-sidebar-toggle]") return [openButton, closeButton];
      return [];
    },
  };
  const windowRef = {
    innerWidth: 390,
    addEventListener(name, handler) {
      listeners[name] = handler;
    },
  };

  site.initialize(documentRef, windowRef);
  openButton.listeners.click();
  assert.equal(shell.classList.contains("nav-open"), true);
  assert.equal(body.classList.contains("nav-is-open"), true);
  assert.equal(openButton.getAttribute("aria-expanded"), "true");
  assert.equal(closeButton.getAttribute("aria-expanded"), "true");

  scrim.listeners.click();
  assert.equal(shell.classList.contains("nav-open"), false);
  assert.equal(body.classList.contains("nav-is-open"), false);

  openButton.listeners.click();
  listeners.keydown({ key: "Escape" });
  assert.equal(shell.classList.contains("nav-open"), false);

  openButton.listeners.click();
  windowRef.innerWidth = 1200;
  listeners.resize();
  assert.equal(shell.classList.contains("nav-open"), false);
});

test("the shell initializer tolerates a missing browser DOM", () => {
  assert.doesNotThrow(() => site.initialize(undefined, undefined));
});

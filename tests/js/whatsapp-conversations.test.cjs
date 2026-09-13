const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");

const whatsapp = require(path.resolve(
  __dirname,
  "../../src/BillingControl/wwwroot/js/whatsapp-conversations.js",
));

test("only Direct conversations require an anchor field", () => {
  assert.equal(whatsapp.conversationKindNeedsAnchor("Direct"), true);
  assert.equal(whatsapp.conversationKindNeedsAnchor("Group"), false);
});

test("participant identity visibility is limited to typed mapping kinds", () => {
  assert.equal(whatsapp.participantKindsWithTypedIdentity.has("Contact"), true);
  assert.equal(whatsapp.participantKindsWithTypedIdentity.has("BusinessParty"), true);
  assert.equal(whatsapp.participantKindsWithTypedIdentity.has("Manager"), true);
  assert.equal(whatsapp.participantKindsWithTypedIdentity.has("AppUser"), true);
  assert.equal(whatsapp.participantKindsWithTypedIdentity.has("BusinessSender"), false);
  assert.equal(whatsapp.participantKindsWithTypedIdentity.has("UnknownExternal"), false);
});

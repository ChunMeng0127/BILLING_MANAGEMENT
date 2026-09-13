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

const typedIdentityFieldIds = [
  "ContactId",
  "ContactWhatsAppAddressId",
  "BusinessPartyId",
  "ManagerId",
  "AppUserId",
];

const createParticipantForm = (participantKind) => {
  const kind = { value: participantKind };
  const fields = new Map(
    typedIdentityFieldIds.map((id) => [id, { id, value: `${id}-selected`, disabled: false }]),
  );
  const evidence = new Map([
    ["ProviderParticipantKey", { id: "ProviderParticipantKey", value: "provider-key" }],
    ["NormalizedE164", { id: "NormalizedE164", value: "+60123456789" }],
    ["DisplayNameSnapshot", { id: "DisplayNameSnapshot", value: "External member" }],
  ]);
  const address = fields.get("ContactWhatsAppAddressId");
  address.options = [
    { dataset: { contactId: "contact-1" }, hidden: false },
    { dataset: { contactId: "contact-2" }, hidden: false },
  ];
  address.selectedOptions = [];
  const contact = fields.get("ContactId");
  const sections = [
    "Contact",
    "BusinessParty",
    "Manager",
    "AppUser",
    "BusinessSender",
    "UnknownExternal",
  ].map((identity) => ({
    dataset: { whatsappParticipantIdentity: identity },
    hidden: false,
  }));

  return {
    kind,
    fields,
    evidence,
    contact,
    address,
    querySelector(selector) {
      if (selector === "[data-whatsapp-participant-kind]") return kind;
      if (selector === "[data-whatsapp-contact-id]") return contact;
      if (selector === "[data-whatsapp-contact-address-id]") return address;
      if (selector.startsWith("#")) return fields.get(selector.slice(1)) ?? evidence.get(selector.slice(1));
      return null;
    },
    querySelectorAll(selector) {
      return selector === "[data-whatsapp-participant-identity]" ? sections : [];
    },
  };
};

const setParticipantKind = (form, participantKind) => {
  form.kind.value = participantKind;
  whatsapp.syncParticipantKind(form);
};

test("switching Contact to BusinessParty clears and disables stale Contact mappings", () => {
  const form = createParticipantForm("Contact");
  whatsapp.syncParticipantKind(form);
  form.fields.get("ContactId").value = "contact-1";
  form.fields.get("ContactWhatsAppAddressId").value = "address-1";
  form.fields.get("BusinessPartyId").value = "business-party-1";

  setParticipantKind(form, "BusinessParty");

  assert.equal(form.fields.get("ContactId").disabled, true);
  assert.equal(form.fields.get("ContactId").value, "");
  assert.equal(form.fields.get("ContactWhatsAppAddressId").disabled, true);
  assert.equal(form.fields.get("ContactWhatsAppAddressId").value, "");
  assert.equal(form.fields.get("BusinessPartyId").disabled, false);
  assert.equal(form.fields.get("BusinessPartyId").value, "business-party-1");
});

test("switching BusinessParty to Manager clears and disables the old mapping", () => {
  const form = createParticipantForm("BusinessParty");
  whatsapp.syncParticipantKind(form);
  form.fields.get("BusinessPartyId").value = "business-party-1";
  form.fields.get("ManagerId").value = "manager-1";

  setParticipantKind(form, "Manager");

  assert.equal(form.fields.get("BusinessPartyId").disabled, true);
  assert.equal(form.fields.get("BusinessPartyId").value, "");
  assert.equal(form.fields.get("ManagerId").disabled, false);
  assert.equal(form.fields.get("ManagerId").value, "manager-1");
});

test("switching a typed participant to BusinessSender disables and clears all typed IDs", () => {
  const form = createParticipantForm("Contact");
  whatsapp.syncParticipantKind(form);
  typedIdentityFieldIds.forEach((id) => {
    form.fields.get(id).value = `${id}-stale`;
  });

  setParticipantKind(form, "BusinessSender");

  typedIdentityFieldIds.forEach((id) => {
    assert.equal(form.fields.get(id).disabled, true, `${id} should be disabled`);
    assert.equal(form.fields.get(id).value, "", `${id} should be cleared`);
  });
});

test("switching to UnknownExternal clears typed IDs while preserving evidence fields", () => {
  const form = createParticipantForm("BusinessParty");
  whatsapp.syncParticipantKind(form);
  typedIdentityFieldIds.forEach((id) => {
    form.fields.get(id).value = `${id}-stale`;
  });
  const evidenceValues = [...form.evidence].map(([id, field]) => [id, field.value]);

  setParticipantKind(form, "UnknownExternal");

  typedIdentityFieldIds.forEach((id) => {
    assert.equal(form.fields.get(id).disabled, true, `${id} should be disabled`);
    assert.equal(form.fields.get(id).value, "", `${id} should be cleared`);
  });
  evidenceValues.forEach(([id, value]) => assert.equal(form.evidence.get(id).value, value));
});

test("switching back to Contact re-enables only Contact mappings", () => {
  const form = createParticipantForm("Contact");
  whatsapp.syncParticipantKind(form);
  setParticipantKind(form, "BusinessParty");

  assert.equal(form.fields.get("ContactId").disabled, true);
  assert.equal(form.fields.get("ContactWhatsAppAddressId").disabled, true);

  setParticipantKind(form, "Contact");

  assert.equal(form.fields.get("ContactId").disabled, false);
  assert.equal(form.fields.get("ContactWhatsAppAddressId").disabled, false);
  assert.equal(form.fields.get("BusinessPartyId").disabled, true);
  assert.equal(form.fields.get("ManagerId").disabled, true);
  assert.equal(form.fields.get("AppUserId").disabled, true);
});

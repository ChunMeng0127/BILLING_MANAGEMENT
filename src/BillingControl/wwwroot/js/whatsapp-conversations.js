(() => {
  const conversationKindNeedsAnchor = (kind) => kind === "Direct";
  const participantKindsWithTypedIdentity = new Set([
    "Contact",
    "BusinessParty",
    "Manager",
    "AppUser",
  ]);
  const participantTypedIdentityFieldIds = {
    Contact: ["ContactId", "ContactWhatsAppAddressId"],
    BusinessParty: ["BusinessPartyId"],
    Manager: ["ManagerId"],
    AppUser: ["AppUserId"],
    BusinessSender: [],
    UnknownExternal: [],
  };
  const allParticipantTypedIdentityFieldIds = [
    ...new Set(Object.values(participantTypedIdentityFieldIds).flat()),
  ];
  const syncConversationKind = (form) => {
    const kind = form?.querySelector("[data-whatsapp-conversation-kind]");
    const anchor = form?.querySelector("[data-whatsapp-direct-anchor]");
    const address = form?.querySelector("#DirectContactWhatsAppAddressId");
    if (!kind || !anchor) return;
    const direct = conversationKindNeedsAnchor(kind.value);
    anchor.hidden = !direct;
    if (!direct && address) address.value = "";
  };
  const syncParticipantKind = (form) => {
    const kind = form?.querySelector("[data-whatsapp-participant-kind]");
    if (!kind) return;
    const enabledFieldIds = new Set(participantTypedIdentityFieldIds[kind.value] ?? []);

    allParticipantTypedIdentityFieldIds.forEach((fieldId) => {
      const field = form.querySelector(`#${fieldId}`);
      if (!field) return;

      const enabled = enabledFieldIds.has(fieldId);
      field.disabled = !enabled;
      if (!enabled) field.value = "";
    });

    form.querySelectorAll("[data-whatsapp-participant-identity]").forEach((section) => {
      section.hidden = section.dataset.whatsappParticipantIdentity !== kind.value;
    });
    const contactId = form.querySelector("[data-whatsapp-contact-id]")?.value;
    const address = form.querySelector("[data-whatsapp-contact-address-id]");
    if (address) {
      const selected = address.selectedOptions[0];
      [...address.options].forEach((option) => {
        if (!option.dataset.contactId) return;
        option.hidden = kind.value !== "Contact" || !contactId || option.dataset.contactId !== contactId;
      });
      if (kind.value !== "Contact" || (selected?.dataset.contactId && selected.dataset.contactId !== contactId))
        address.value = "";
    }
  };
  if (typeof module === "object" && module.exports)
    module.exports = {
      conversationKindNeedsAnchor,
      participantKindsWithTypedIdentity,
      syncConversationKind,
      syncParticipantKind,
    };
  if (typeof document === "undefined") return;

  document.querySelectorAll("[data-whatsapp-create-form]").forEach((form) => {
    const kind = form.querySelector("[data-whatsapp-conversation-kind]");
    syncConversationKind(form);
    kind?.addEventListener("change", () => syncConversationKind(form));
  });
  document.querySelectorAll("[data-whatsapp-participant-form]").forEach((form) => {
    const kind = form.querySelector("[data-whatsapp-participant-kind]");
    const contact = form.querySelector("[data-whatsapp-contact-id]");
    syncParticipantKind(form);
    kind?.addEventListener("change", () => syncParticipantKind(form));
    contact?.addEventListener("change", () => syncParticipantKind(form));
  });
})();

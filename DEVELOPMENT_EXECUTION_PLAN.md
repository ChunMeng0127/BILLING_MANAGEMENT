# Development Execution Plan

**Repository:** `ChunMeng0127/BILLING_MANAGEMENT`  
**Scope:** In-progress development only  
**Current development branch:** `codex/phase10c-manual-send`  
**Current development HEAD:** `c6d7af36c45c9783fe40b66eae975af549498f03`  
**Stable development baseline:** `codex/accounting-mvp` at `5d6e3772a97dad078b0a13a75609c2cc9a3a2d1f`  
**Production/deployed application baseline:** `6677680feb12141eefe97dd5d21b37fd3b6b4bf1`  
**Created:** 2026-09-17

---

## 1. Purpose

This document is the short execution roadmap for development work.

`WHATSAPP_DOCUMENT_COLLECTION_MASTER_PLAN.md` remains the detailed architecture/history source of truth. This file exists only to show the current baseline, open risks, immediate milestones and execution order.

Production hardening is managed separately through `PRODUCTION_HARDENING_PLAN.md`. Development work must not be merged into production merely because a development phase is complete.

---

## 2. Development Rules

1. Do not develop directly on production `master`.
2. Do not merge production hardening wholesale into development.
3. Review production changes individually and sync only applicable fixes.
4. Any production financial, schema, authentication, permission, security or data-integrity change must be explicitly reviewed before development continues past the next integration checkpoint.
5. Preserve the existing financial/accounting core and regression coverage.
6. External provider calls must remain duplicate-safe and reconcilable.
7. Manual UAT is required for staff-facing workflow completion; automated tests alone do not close a phase.
8. Real Meta and SharePoint verification must happen before production-readiness approval.
9. Production database migration rehearsal must use an isolated restored copy, never the live database.

---

## 3. Current Position

### Stable development

`codex/accounting-mvp` — `5d6e3772a97dad078b0a13a75609c2cc9a3a2d1f`

Phase 10A and 10B are promoted to this stable development line.

### Current WIP

`codex/phase10c-manual-send` — `c6d7af36c45c9783fe40b66eae975af549498f03`

Phase 10C implementation and accepted-result reconciliation correction are implemented. The application-code correction is at `c5f1b9a395b8b8e45a7652886fbf85e02c39d70e`; the current branch tip adds only roadmap/status documentation.

### Open acceptance debt

- Phase 2 repeat manual UI acceptance
- Phase 3 manual UI acceptance
- Phase 4 manual UI acceptance
- Phase 10C technical closure
- Phase 10D staff UI and manual UAT
- Real Meta account/provider verification
- Real SharePoint tenant/permission verification
- Production-database-to-development migration rehearsal

---

## 4. Milestone 1 — Complete Outbound Request Workflow

### D1.1 — Phase 10C Technical Closure

Review and validate:

- provider is never called twice for the same durable attempt
- concurrent Send operations remain safe
- Accepted result is persisted durably
- Rejected result is persisted durably
- Ambiguous result blocks unsafe resend
- application/process cancellation after provider response cannot discard the provider result
- replay of a completed attempt returns the durable result without another provider call
- accepted message/document activation conflicts are surfaced as reconciliation instead of overwriting later staff state
- no financial/accounting workflow is mutated
- focused tests and full CI remain green

**Exit:** Phase 10C approved and promoted to the stable development line.

### D1.2 — Phase 10D Staff UI

Provide staff-facing operational states and actions for:

- Preview
- Queue
- Ready to Send
- Sending
- Accepted
- Rejected
- Ambiguous / delivery uncertain
- Reconciliation required

The UI must use operational language. Staff must not need to interpret HTTP/provider internals.

**Safety rule:** ambiguous delivery must never encourage ordinary resend before reconciliation.

### D1.3 — Phase 10 Manual UAT

Run the full business journey:

Customer → Engagement → Checklist → Contact → Conversation → Document Request → Preview → Queue → Send → Provider Result.

**Exit:** Phase 10 closed only after technical checks and manual staff acceptance both pass.

---

## 5. Milestone 2 — Development Stabilisation Gate

Do not start broad new automation until this gate passes.

### D2.1 — Clear UI/UAT debt

Complete the outstanding Phase 2, 3 and 4 manual acceptance items in the integrated application rather than testing each page in isolation.

### D2.2 — Production database migration rehearsal

1. Restore a current production backup into an isolated PostgreSQL environment.
2. Apply all development migrations in order.
3. Run schema/migration validation.
4. Run financial/data-integrity audit.
5. Start the development application against the migrated clone.
6. Run core accounting regression plus document/WhatsApp workflow tests.
7. Record migration duration, failures and rollback/recovery procedure.

### D2.3 — Real Meta verification

Verify with non-production/test-safe configuration:

- credentials and account eligibility
- actual outbound message acceptance
- rejection behavior
- timeout/network/5xx handling
- provider message ID and timestamp behavior
- service-window/template rules applicable to the account

### D2.4 — Real SharePoint verification

Verify:

- tenant/site/library configuration
- least-privilege permissions
- upload/download
- duplicate/conflict handling
- resumable upload behavior where applicable
- access failure and retry behavior

### D2.5 — Architecture boundary review

Do not perform cosmetic refactoring. Split responsibilities only where needed before inbound/webhook work so that conversation, outbound send, delivery reconciliation, inbound processing and document evidence do not grow into one service boundary.

**Exit:** integrated development baseline is suitable for the inbound automation phase.

---

## 6. Milestone 3 — Complete the Automation Loop

### D3 — WhatsApp Inbound

Implement provider webhook reception with:

- signature/authenticity validation
- durable raw/provider event identity
- unique constraint / duplicate-event protection
- idempotent processing
- conversation/contact/request mapping
- auditable persistence

### D4 — Delivery & Reconciliation

Process provider delivery evidence and resolve:

- Accepted
- Delivered
- Read
- Failed
- Ambiguous
- Reconciliation required

An ambiguous outbound attempt must be resolved by durable provider/webhook evidence or controlled staff reconciliation, not blind resend.

### D5 — Incoming Documents

Receive supported attachments, validate them safely, store evidence, persist metadata and upload permanent copies to SharePoint. Prevent duplicate processing and incorrect request/customer linkage.

### D6 — Document Matching

Start deterministic. Use request context, expected checklist items, conversation context and file metadata. Require staff confirmation where classification is uncertain.

AI assistance may be added later as a suggestion layer; it must not silently decide financial status or destroy auditability.

### D7 — Follow-Up Automation

After document receipt/matching is reliable:

- identify missing requirements
- generate staff-reviewable reminder content
- support controlled resend/reminder workflow
- later add due dates, escalation and no-response policies

---

## 7. Development-to-Production Readiness Gate

Development must not be promoted to production until all of the following are true:

- Phase 10 technical and manual acceptance is complete
- outstanding integrated UI/UAT debt is closed or explicitly accepted
- real Meta verification passes
- real SharePoint verification passes
- current production database migration rehearsal passes
- production financial/data-integrity audit remains clean after migration
- required production hardening controls are compatible with the release path
- exact release SHA/image is identified
- pre-deploy backup and rollback procedure are documented

---

## 8. Immediate Execution Order

1. **D1.1 — Review and close Phase 10C**
2. Promote approved Phase 10C into stable development
3. **D1.2 — Build Phase 10D staff UI**
4. **D1.3 — Run Phase 10 manual UAT**
5. **D2 — Development Stabilisation Gate**
6. **D3 — WhatsApp Inbound**
7. **D4 — Delivery/Reconciliation**
8. **D5 — Incoming Documents**
9. **D6 — Document Matching**
10. **D7 — Follow-Up Automation**

---

## 9. Current Next Action

**Now:** perform the Phase 10C technical closure review against `codex/phase10c-manual-send` at `c6d7af36c45c9783fe40b66eae975af549498f03`, treating `c5f1b9a395b8b8e45a7652886fbf85e02c39d70e` as the latest application-code correction checkpoint.

Do not start Phase 10D until Phase 10C has a clear approval or bounded correction list.

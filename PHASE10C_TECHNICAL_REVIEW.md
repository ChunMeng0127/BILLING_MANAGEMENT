# Phase 10C Technical Closure Review

**Reviewed:** 2026-09-17  
**Branch:** `codex/phase10c-manual-send`  
**Application-code checkpoint:** `c5f1b9a395b8b8e45a7652886fbf85e02c39d70e`  
**Review status:** **BOUNDED CORRECTION REQUIRED BEFORE CLOSURE**

---

## 1. Scope

Technical review of Phase 10C manual WhatsApp send, provider-result persistence, duplicate/concurrency controls, Accepted/Rejected/Ambiguous behavior and DocumentRequest activation reconciliation.

Production `master` and production hardening are out of scope.

---

## 2. Accepted Findings

The following Phase 10C design points are technically acceptable and should not be weakened:

- durable send attempt is committed before the provider call
- provider call occurs outside the database transaction/row locks
- an open attempt blocks a second ordinary send
- concurrent sends claim at most one provider attempt
- provider/network exceptions are handled conservatively rather than assumed rejected
- Ambiguous outcome blocks ordinary resend
- DefinitelyRejected supports only explicit later retry subject to revalidation/timing
- provider result persistence retries database work only and does not call the provider again
- caller cancellation after a provider result has been obtained does not cancel durable result persistence
- Accepted result writes the durable provider outcome even when document activation encounters a concurrent staff lifecycle change
- concurrent document/request changes are preserved instead of being overwritten
- financial/accounting rows remain outside the Phase 10C mutation boundary

The correction CI at `c5f1b9a395b8b8e45a7652886fbf85e02c39d70e` completed successfully.

---

## 3. Blocking Finding — Repeated Accepted Result Can Produce False Reconciliation

### Current behavior

`RequiresDocumentActivationReconciliationAsync` currently treats a request/item as reconciled only when its present status is exactly:

- `DocumentRequestStatus.Requested`
- `DocumentRequestItemStatus.Requested`

This is correct immediately after successful activation, but it is not durable as a replay/reconciliation test.

The domain already contains legitimate later lifecycle states such as:

- request: `PartiallyReceived`, `Complete`
- item: `PartiallyReceived`, `Received`, `NotRequired`, `Waived`

After a successful provider acceptance and correct `Requested` activation, later workflow can legitimately advance those rows. A later replay/read of the already-Accepted outbound result would then incorrectly report `RequiresDocumentActivationReconciliation = true` merely because the workflow progressed.

### Required correction

Repeated/replayed Accepted-result reconciliation must distinguish between:

1. **activation never completed / conflicted at provider-result time** — reconciliation required; and
2. **activation completed correctly and the request/item later advanced through a legitimate lifecycle** — no activation reconciliation required.

Use the existing durable lifecycle/audit evidence and current domain rules to make that distinction. Do not simply accept every later status: the existing post-claim race tests must continue to detect cases where staff changed request/item state before the provider result was persisted.

### Must not break

- no second provider call for replay
- existing request-race and item-race protection
- open-attempt and Ambiguous resend blocking
- immutable outbound snapshot behavior
- Accepted/Rejected/Ambiguous result durability
- current financial/workflow isolation
- schema/migration unless demonstrably necessary (a migration is not expected for this bounded correction)

### Required tests

Add focused real-PostgreSQL coverage proving both directions:

- successful Accepted activation → later legitimate request/item progression → repeated Accepted result does **not** report activation reconciliation
- request/item changed between durable claim and provider-result persistence → repeated Accepted result **continues** to report activation reconciliation

Run the full existing PostgreSQL-backed .NET suite, JavaScript tests, Release build, EF pending-model check, Docker build and HTTPS smoke.

---

## 4. Closure Decision

Phase 10C is **not yet closed**.

No broad redesign is required. Complete only the bounded replay/reconciliation correction above, rerun the full validation suite, then perform a final diff/review. Do not start Phase 10D until this correction is approved and Phase 10C is promoted to stable development.

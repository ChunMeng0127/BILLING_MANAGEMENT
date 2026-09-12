# WhatsApp Document Collection & Follow-Up — Master Plan

> **Repository:** `ChunMeng0127/BILLING_MANAGEMENT`  
> **Planning baseline:** `6677680feb12141eefe97dd5d21b37fd3b6b4bf1`  
> **Primary implementation model:** Codex Luna Max only, split into small bounded execution phases  
> **Permanent document storage:** SharePoint  
> **Status:** Phase 0 architecture frozen — implement phase-by-phase, do not build everything in one Codex run.

---

## Current Project Status

**Current Phase:** Phase 3A — Document Request application / internal workflow service
**Status:** Phase 1 is approved/closed and complete. Phase 1A and Phase 1B are approved/closed. Phase 1C is approved/closed against `78dd826e2d23941e59677836a9693180ae71f2fe`. Phase 2A is approved/closed against `f9f81bb22ab78e5986a102f071b9f7f5d03f471a`; its final GitHub Actions run `34663809281` passed. Phase 2B is technically approved against `09208aba77477db413fdf31dbf387864ca2773dd`; GitHub Actions run `34667389872` passed. Phase 2C technical regression/review is approved/complete. Phase 2 remains pending final manual user UI acceptance because the user will access the application later; the user explicitly authorized continuing development before that check. Phase 3A is complete and awaiting ChatGPT final approval. Phase 3B/3C and Phase 4 have not started.
**Last Reviewed:** 2026-09-12

### Completed

- Master roadmap and phased implementation plan drafted.
- Core business rules, architecture direction, integrations, testing expectations, and rollout sequence documented.
- AI/Codex working rules added so the repository and this plan remain the source of truth.
- Roadmap adjusted for Codex Luna Max only, with small reviewable execution units.
- Phase 0A repository inventory completed and approved.
- Phase 0B deterministic core data model completed, corrected, and approved.
- Phase 0C contact/PIC, WhatsApp conversation, authorization/confidentiality, SharePoint storage, and permission boundaries completed and approved.
- Phase 0D automation, workflow, inbox/outbox reliability, retry/idempotency, audit, security, and AI boundaries completed and approved.
- Phase 0 external-platform verification was refreshed on 2026-09-12: Meta Cloud API remains the WhatsApp Business Platform integration boundary; group capability remains account/eligibility dependent; Microsoft Graph supports Selected permission scopes for SharePoint/OneDrive and resumable upload sessions.
- No production code, migration, provider integration, SharePoint integration, background worker, or Phase 1 implementation was started during Phase 0.
- Phase 1 execution split recorded: 1A core domain types, 1B EF Core persistence/relationships/constraints/indexes/migration, and 1C PostgreSQL integration/invariant/migration tests.
- Phase 1A core document domain types are approved/closed in `src/BillingControl/Models/DocumentCollectionDomain.cs`.
- Phase 1B EF Core persistence and final correction was approved/closed against `745ec1c7e5f69890530514209a3a7e7309cfcaf9`: document DbSets, explicit restrictive relationships, immutable-field allowlists, used-template protection, enum/value checks, lineage constraints, referenced-row template/service consistency triggers, received-document duplicate/replacement guards, concurrency mapping, history protection, and one additive migration are present. No document-collection data was backfilled.
- Phase 1C PostgreSQL persistence/invariant coverage is approved/closed against `78dd826e2d23941e59677836a9693180ae71f2fe`; GitHub Actions run `34662113750` executed all 60 .NET tests against PostgreSQL 17/`billing_test` with 0 failures and 0 skips. Phase 1 is complete.
- Phase 2 execution split recorded: 2A template application/service layer, 2B internal template management UI, and 2C Phase 2 business/integration tests and review closure.
- Phase 2A template application/service layer is approved/closed against `f9f81bb22ab78e5986a102f071b9f7f5d03f471a`; final GitHub Actions run `34663809281` passed.
- Phase 2B internal template management UI is technically approved against `09208aba77477db413fdf31dbf387864ca2773dd`; GitHub Actions run `34667389872` passed. It adds staff-only MVC routes, service-backed mutations, version/item forms, lifecycle/default controls, immutable-used-version presentation, staff navigation, and focused PostgreSQL-backed MVC authorization/regression coverage.
- Phase 2C technical regression/review closure is complete. Additional high-value coverage verifies family/version filtering and ordering, inactive historical visibility, repeated item reorder/edit/activation flows, used-template lifecycle operations, foreign-ID rejection, authorization/antiforgery, and financial/workflow isolation. Manual user UI acceptance is intentionally deferred until the user can access the application.
- Phase 3 execution split recorded: 3A document request application/internal workflow service, 3B internal document request/checklist UI, and 3C Phase 3 integration/regression and manual acceptance.
- Phase 3A document request application/internal workflow service is complete and awaiting ChatGPT final approval. It adds provider-neutral request creation, active-template snapshotting, WorkItem revision allocation, pre-provider request/item transitions, append-only status histories, untracked read models, and PostgreSQL locking/concurrency handling without schema changes or financial/workflow side effects.

### Current Work

- Phase 0A is closed and approved.
- Phase 0B is closed and approved.
- Phase 0C is closed and approved.
- Phase 0D is closed and approved.
- Phase 1A is approved and closed.
- Phase 1B is approved and closed against `745ec1c7e5f69890530514209a3a7e7309cfcaf9`.
- Phase 1C PostgreSQL persistence/invariant tests are approved and closed against `78dd826e2d23941e59677836a9693180ae71f2fe`; local execution remains skip-only without an isolated PostgreSQL connection, while CI execution is green.
- Phase 1 is approved/closed and complete.
- Phase 2A template application/service layer is approved/closed against `f9f81bb22ab78e5986a102f071b9f7f5d03f471a`; final GitHub Actions run `34663809281` passed.
- Phase 2B internal template management UI is technically approved against `09208aba77477db413fdf31dbf387864ca2773dd`; GitHub Actions run `34667389872` passed.
- Phase 2C technical regression/review closure is complete; Phase 2 remains pending final manual user UI acceptance.
- The user explicitly authorized continuing Phase 3 development before the outstanding Phase 2 manual UI acceptance.
- Phase 3A is complete and awaiting ChatGPT final approval; Phase 3B and 3C have not started.

### Outstanding

- Final manual user UI acceptance for Phase 2 remains outstanding and will be completed when the user can access the application. Phase 2 is not yet marked fully approved/closed.
- ChatGPT review/approval of Phase 3A remains outstanding before Phase 3B may begin.
- Current Meta account eligibility, current messaging/template/service-window rules, and actual SharePoint tenant permissions still require environment verification when the relevant integration phase begins.

### Next Step

Review Phase 3A, then authorize Phase 3B separately. Complete final manual user UI acceptance for Phase 2 when the user can access the application; do not mark Phase 2 fully closed or start Phase 3B automatically.

---

## 1. Objective

Extend Billing Control into a mature **WhatsApp-first document collection and follow-up system** for accounting work.

The system should:

- Request documents through WhatsApp rather than email.
- Prefer a WhatsApp group where the client, accounting firm, manager, and LCM MGT can monitor the same authorised conversation when the provider/account supports groups.
- Keep Direct WhatsApp as a fully supported fallback.
- Support one PIC handling multiple companies without spamming them.
- Consolidate multiple company requests only when the PIC, conversation, active participant set, and approved confidentiality scope permit it.
- Make requested work feel manageable by showing only the next small set of useful items, not a huge outstanding list.
- Receive client replies and documents from WhatsApp.
- Store permanent files in SharePoint.
- Track every request, item, message, received artifact, evidence link, follow-up, snooze, promise, override, and workflow transition in Billing Control.
- Stop or delay reminders when the client has promised a later date.
- Integrate safely with the existing Work Item / Worker Assignment / Workflow Status model without driving financial state.
- Use AI later for suggestions such as classification, promise-date detection, and wording while deterministic business rules remain authoritative.

---

## 2. Core Operational Flow

```text
Billing Record
    ↓
Work Item
    ↓
Document Request generated from service template
    ↓
Eligible requests grouped into a reviewed Document Request Batch
    ↓
Authorisation + consent + conversation scope revalidated
    ↓
Outbound message snapshot queued in durable outbox
    ↓
WhatsApp Group / Direct message accepted by provider
    ↓
Client sends text / PDF / image / document
    ↓
Webhook signature verified on raw body
    ↓
Provider event durably recorded in inbox
    ↓
ReceivedDocument raw intake record created
    ↓
Media validated / scanned / stored once in SharePoint
    ↓
Staff/authorised worker classifies artifact against request item
    ↓
DocumentRequestItemEvidence created explicitly
    ↓
Request/item state recalculated deterministically
    ↓
Follow-up engine evaluates PIC + conversation + outstanding scope
    ↓
All mandatory items confirmed
    ↓
Human confirms collection complete
    ↓
Optional human-approved assignment workflow transition
```

---

## 3. Non-Negotiable Design Principles

1. **WhatsApp-first. No email workflow is required.**
2. **Group conversation preferred when available and authorised; Direct remains fully supported.**
3. **One company does not automatically equal one WhatsApp message.**
4. A PIC may represent several companies.
5. Requests may be consolidated only when the **same PIC + same WhatsApp conversation + current approved participant/scope version** apply.
6. Never mix unrelated companies or engagements with different authorised participants/scope in one batch/message.
7. Follow-up frequency is controlled mainly at the **PIC + conversation** level; each outbound batch is an immutable send snapshot.
8. Automated follow-ups should normally show only **3–5 next-action items**, even if many more are outstanding internally.
9. Client can send documents gradually.
10. Promised dates / snooze / pause / stop-automation rules must suppress automated reminders where their scope applies.
11. A received attachment is **not automatically an accepted document** and is **not automatically classified to a WorkItem**.
12. SharePoint is the authoritative permanent binary repository; Billing Control is the authoritative workflow/audit/evidence-link store.
13. Raw `ReceivedDocument` intake is artifact-centric and may remain unclassified with zero evidence links.
14. AI may suggest; deterministic database rules decide authorization, send eligibility, status, completeness, and financial isolation.
15. AI must not silently change operational records during initial rollout.
16. Every inbound/outbound/provider/human override action must be auditable.
17. Staff must always be able to pause, snooze, resume, stop, correct, or manually recover automation within authorization rules.
18. Do not auto-drive financial status, `WorkItem.Status`, invoice/receipt/payment state, revenue-share state, or worker entitlement from document collection.
19. A phone-number or provider-ID match is identity evidence only; it is never sufficient authorization by itself.
20. Provider/API eligibility and changing external rules are capability-checked at implementation/runtime rather than hard-coded into the domain model.

---

## 4. Existing Billing Control Integration

Current operational chain:

```text
Billing Record
    ↓
Work Item
    ↓
Worker Assignment
    ↓
Weekly Progress / Detailed Workflow
```

Document request/checklist state sits primarily under the **Work Item**, because the request belongs to the job rather than to one individual worker. Raw `ReceivedDocument` intake is separate and may remain unclassified until explicit evidence linkage.

Approved core relationship:

```text
WorkItem
├── WorkerAssignment A
├── WorkerAssignment B
└── DocumentRequest
    ├── DocumentRequestItem
    │   └── DocumentRequestItemEvidence ──→ ReceivedDocument
    └── DocumentRequestBatch membership

ReceivedDocument may exist with zero evidence links before classification.
```

Existing detailed workflow statuses include:

1. Document Requested
2. Document Received
3. Assigned / Not Started
4. Assignment Started
5. Start Preparing
6. Queries Sent
7. Draft Management Report Sent
8. Pending Review
9. Amendment / Revision
10. Final Management Report Sent
11. Completed

Approved integration rule:

- Collection state remains authoritative inside the new document-collection aggregate.
- A successful provider-accepted first request makes the collection request `Requested`; merely queueing a send does not.
- Assignment workflow is not automatically changed because one WorkItem may have multiple assignments.
- UI may show **Ready to mark Document Requested** after the first successful outbound request.
- When all mandatory request items are satisfied and a human confirms completion, show **Ready to mark Document Received**.
- Any assignment workflow transition requires an authorised human action against explicitly selected eligible assignment(s) and must pass the existing assignment workflow rules.
- No collection action automatically changes financial or WorkItem state.

---

## 5. Proposed / Approved Data Model Direction

The Phase 0B, 0C, and 0D specifications below are authoritative. Earlier examples in Sections 5–13 are explanatory only and must not override the frozen architecture.

### Document requirement templates

- `DocumentRequirementTemplate`
- `DocumentRequirementTemplateItem`

Template items support:

- Name
- Description
- Required / Optional
- Priority / Wave
- Display order
- Active / Inactive
- Service association

Suggested priority/wave values:

```text
Start Work
Normal
Later
Optional
```

### Document requests

- `DocumentRequest`
- `DocumentRequestItem`
- `DocumentRequestBatch`
- `DocumentRequestBatchMember`
- `DocumentRequestItemEvidence`

Item statuses:

```text
Missing
Requested
PartiallyReceived
Received
NotRequired
Waived
```

Request statuses:

```text
Draft
ReadyToSend
Requested
PartiallyReceived
Complete
Paused
Cancelled
Superseded
```

### Contact / PIC and WhatsApp boundary

Approved concepts include:

- `Contact`
- `ContactWhatsAppAddress`
- `ContactCustomerLink`
- `WhatsAppConversation`
- `WhatsAppConversationParticipant`
- `WhatsAppConversationEngagementScope`
- immutable participant/scope snapshot for every queued outbound batch/message

### Received documents

- `ReceivedDocument`
- provider-neutral storage reference/state for the SharePoint object
- explicit evidence links to request items

Raw `ReceivedDocument` contains no direct WorkItem/BillingRecord/Request/RequestItem foreign key. Permanent SharePoint metadata is artifact-centric; request-specific classification remains authoritative in Billing Control evidence links.

### Automation / integration state

Approved concepts include:

- conversation/PIC follow-up control
- scoped holds/snoozes/promised dates
- immutable outbound batch/message snapshot
- durable provider-event inbox
- durable outbound-message outbox
- durable storage/integration work state
- append-only provider/message/delivery/audit histories

---

## 6. Multiple Companies per PIC — Anti-Spam / Confidentiality Design

Example:

```text
Mr Tan
├── ABC Sdn Bhd
├── XYZ Sdn Bhd
├── DEF Sdn Bhd
└── GHI Sdn Bhd
```

Do not send four independent reminders when they can be safely consolidated.

Eligibility is not based only on a shared phone number. A request may join one outbound batch only when all of these are true:

```text
Same approved Contact/PIC
+
Same WhatsAppConversation
+
Contact is active PIC for each request customer
+
Each request Engagement is active in the conversation's approved Engagement scope
+
Conversation participant set has not changed since scope approval
+
Consent / do-not-contact / provider-policy gates pass
+
No confidentiality or hold rule blocks the request
=
Eligible to consolidate
```

If any condition differs, split the batch.

Every outbound batch snapshots:

- Contact/PIC identity
- Conversation identity
- conversation authorization version
- current participant set / participant-set hash
- included Engagement/request memberships
- exact next-action items
- exact message/template body or template parameters

If the participant set, engagement scope, request revision, or authorization version changes after preview and before queue/send, the batch is invalidated and must be rebuilt. Never silently send a stale preview.

### Client-facing workload rule

Billing Control may know there are many outstanding items, but an automated message should normally show only the **next 3–5 useful items**. Deterministic ranking/waves select the facts; AI may only rewrite wording later.

---

## 7. Follow-Up Engine Rules

The scheduler evaluates the **Contact/PIC + Conversation** as the global anti-spam boundary and selects eligible outstanding requests into a fresh immutable batch for each send.

Initial policy remains configurable rather than hard-coded:

```text
Initial request
Follow-up 1 after configured business-day interval
Follow-up 2 after configured business-day interval
Escalation after configured business-day interval
Then manual attention unless explicitly configured otherwise
```

Default planning cadence may start from the earlier `Day 0 / +3 / +4 / +5 business days` proposal, but implementation must store policy/config rather than bury those values in controllers or provider code.

Global rules:

- No automated reminder may bypass the configured Contact+Conversation cooling-off window.
- Use existing business timezone/business-calendar abstractions where possible.
- Automated sending is allowed only inside configured business send windows.
- After the configured escalation/max automated stage, automation stops and the thread requires manual attention.
- A manual **Follow up now** may override the cooling-off timer only for authorised staff and with an audited reason; it may not bypass do-not-contact, invalid conversation scope, missing consent, provider-policy, or security blocks.

Actions:

```text
Follow up now
Snooze until
Record promised date/time
Pause
Resume
Stop automation
Re-enable automation
```

### Promised dates and holds

A promised date/hold has an explicit scope:

```text
one DocumentRequest
one current batch
or Contact + Conversation
```

Rules:

- Human confirmation is required during initial rollout.
- If scope is ambiguous, default to the narrowest safe/current batch scope rather than all companies.
- A date-only promise suppresses automated reminders through that local business date; next eligibility begins after the promise window according to the business calendar.
- Completing all scoped requests closes the hold naturally.
- Receiving an unrelated document does not silently clear a promise/hold.
- Snooze is temporary; Pause is indefinite but resumable; Stop automation remains blocked until explicit re-enable.
- Contact `DoNotWhatsApp` / revoked consent has higher precedence than all follow-up state.

---

## 8. WhatsApp Conversation Strategy

### Provider-neutral rule

Business/domain code depends on an `IWhatsAppProvider`-style boundary rather than Meta request/response classes.

The adapter must expose provider capability information such as:

```text
Direct messaging supported
Group messaging supported for this account/number
Template/policy constraints
Media capability
Provider health / rate-limit state
```

Do not encode current Meta eligibility/participant limits as database invariants.

### Group

Group is preferred only when:

- the actual WhatsApp business account/number is currently eligible;
- the provider adapter reports group capability;
- the active group participant set is known;
- the participant set is approved for every Engagement in the conversation scope;
- consent/policy requirements are satisfied.

Any participant join/leave/remove or unknown membership change increments the conversation authorization version, marks the conversation/scope as **NeedsAuthorizationReview**, and blocks new outbound batches until authorised staff re-approve the engagement scope.

### Direct

Direct is always a supported architectural fallback. A direct conversation is anchored to one active `ContactWhatsAppAddress` plus the configured business sender endpoint. If the provider does not expose a native conversation ID, the provider adapter may produce an opaque stable thread key; business code never parses provider-specific key structure.

### Inbound identity rule

- Provider `wa_id`, phone number, sender name, group ID, or conversation ID are routing/identity evidence only.
- They do not grant application authorization.
- Inbound messages may be stored even when the sender is not yet mapped, but untrusted/unmapped content cannot be used to mutate a request or satisfy an item until an authorised classification/action occurs.

### Consent / opt-out

Consent is associated with the actual WhatsApp destination endpoint, not merely the person name.

`ContactWhatsAppAddress` records at least:

```text
ContactId
Normalized E.164 number
Provider wa_id when known
Active / inactive
Primary flag
Opt-in state
Opt-in timestamp/source/evidence reference
Opt-out timestamp/reason
DoNotWhatsApp
Version/audit fields
```

An active normalized WhatsApp destination maps to one Contact in the initial model. If a shared departmental number is genuinely used, model it as one explicit Contact/PIC identity rather than mapping one number ambiguously to several contacts.

Inbound communication alone does not automatically grant broad consent or confidentiality scope.

---

## 9. SharePoint Storage Architecture

SharePoint is the **permanent authoritative binary store**. Billing Control stores the received-artifact record, storage state/reference, classification/evidence links, and audit trail.

Approved flow:

```text
Verified provider event
    ↓
ReceivedDocument raw intake metadata created
    ↓
Private temporary media download / stream
    ↓
Size + MIME + magic-byte + filename validation
    ↓
Malware/safety scan gate
    ↓
SharePoint upload job
    ↓
Store DriveId + ItemId + ETag + WebUrl + StoredAt
    ↓
Delete temporary copy
    ↓
Later classification creates DB evidence links only
```

### Storage abstraction

Use an `IDocumentStorage`-style interface with a SharePoint implementation. Domain/controllers must not contain Microsoft Graph mechanics.

The normal abstraction should provide operations equivalent to:

```text
Store new immutable object
Read/open object through server-side authorization
Read metadata/reference
Verify object still exists / reconcile metadata
```

Do **not** expose unrestricted delete/replace as an ordinary application operation. Corrections create lifecycle/audit changes; they do not overwrite the original received artifact.

### SharePoint site/library strategy

Use a dedicated SharePoint site and dedicated document library for Billing Control client documents where tenant administration allows it.

Prefer app-only least-privilege access restricted to that site/library using Microsoft Graph Selected permissions where supported and operationally practical. Selected scopes require both tenant/admin consent and an explicit resource permission assignment; consent alone must not be treated as access.

If the required upload/read operation cannot be achieved with the selected scope in the actual tenant, broader application permission requires an explicit security decision rather than silent escalation to tenant-wide access.

Prefer certificate-based app credentials on the VPS when practical; if a client secret is used, it is server-only, rotated, and never exposed to the browser/repository/logs.

### Permanent storage layout

Because one raw `ReceivedDocument` can remain unclassified or can later support multiple WorkItems/companies, permanent storage is **artifact-centric**, not WorkItem-folder-centric.

Recommended core layout:

```text
Client Documents
└── Intake
    └── YYYY
        └── MM
            └── server-generated stable filename
```

Rules:

- Upload the binary once.
- Use a server-generated stable filename containing a non-sensitive identifier; preserve `OriginalFilename` as metadata.
- `DriveId + ItemId` is the canonical SharePoint identity. Path/WebUrl may change and is not a database key.
- Do not automatically move/copy the permanent object into one customer/WorkItem folder after classification, because one artifact may support multiple evidence links.
- Do not create duplicate permanent SharePoint copies merely to reflect multiple classifications.
- A future archival/export feature may create human-friendly copies/shortcuts only as a separately approved feature.

Artifact-level SharePoint metadata may include:

```text
ReceivedDocumentId
Source = WhatsApp
Sender snapshot / provider sender key
ReceivedAt
OriginalFilename
MIME type
Byte length
SHA-256
ReceivedDocument lifecycle status
Provider message/media reference where appropriate
```

Do not store a singular `WorkItemId`, `DocumentRequestId`, or `DocumentRequestItemId` as authoritative SharePoint metadata, because the same raw artifact may have zero or multiple evidence links. Those relationships live in Billing Control.

### Storage reference/state

One received artifact has one active permanent-storage reference in the initial SharePoint design.

Provider-neutral state must distinguish storage from document validity, for example:

```text
Pending
Stored
RetryPending
FailedPermanent
Missing / NeedsReconciliation
```

Storage state does not replace `ReceivedDocumentStatus` (`PendingReview`, `Quarantined`, `Accepted`, etc.).

If an external user deletes/moves an item unexpectedly, reconciliation marks the storage reference missing/needs-reconciliation and raises an operational failure; the database history is not deleted or rewritten.

### Temporary media handling

Temporary media is not permanent storage:

- private non-webroot location or streaming buffer;
- random/non-user-controlled filename;
- restrictive OS permissions;
- never backed up as business document storage;
- bounded TTL with cleanup worker;
- removed promptly after successful SharePoint persistence;
- if retry retention expires before successful permanent storage, mark manual recovery required instead of pretending the document is safely stored.

Exact maximum file size, spool TTL, scanner product, and retention duration are implementation/configuration values to be approved in the relevant phase.

---

## 10. File Safety and Reliability Requirements

Incoming media handling must include:

- configured maximum file size;
- MIME allowlist;
- file-signature / magic-byte validation rather than trusting MIME headers/extensions;
- safe filename handling;
- SHA-256 hash;
- duplicate detection without global hash uniqueness;
- malware/safety scan before an artifact can become accepted evidence;
- retry-safe SharePoint storage;
- storage failure visibility;
- immutable sender/receipt metadata;
- no credentials, binaries, access tokens, or unredacted sensitive payloads in normal logs.

A scan failure or suspicious file remains quarantined/non-satisfying. Scanner unavailability must not silently treat the file as safe.

Webhook handling must include:

- provider authenticity/signature verification against the **raw request body before JSON processing**;
- constant-time signature comparison;
- durable inbox persistence before heavy processing;
- provider event/message idempotency;
- replay-safe processing;
- structured redacted logging;
- retry/dead-letter/manual-recovery visibility.

---

## 11. Human Classification Before AI

Before AI, manual classification is the deterministic baseline.

```text
ReceivedDocument raw artifact
    ↓
Authorised user chooses request item
    ↓
Authorization/confidentiality checks run
    ↓
DocumentRequestItemEvidence created
    ↓
Artifact acceptance / item completeness handled explicitly
```

File arrival/classification alone does not make a request item `Received`; the Phase 0B human completeness rules remain authoritative.

### Access to unclassified intake

Initial rollout is deliberately conservative:

- Admin/InternalUser may manage the global unclassified intake queue.
- Worker does not receive global unclassified access merely because a conversation might contain one of the worker's jobs.
- Worker may view/use an artifact after it is explicitly linked to a WorkItem/RequestItem the worker can access, or after a later approved scoped-classification mechanism narrows the artifact to work the worker is authorised to handle.
- Manager/AccountingFirm do not receive raw unclassified-file access.

Cross-WorkItem evidence reuse is staff-controlled initially and requires an audited reason plus authorization checks for every target WorkItem.

---

## 12. Mature AI Strategy

Principle:

> **Database decides WHAT. AI may suggest HOW TO CLASSIFY or HOW TO SAY IT.**

AI must never be the authority for:

- contact identity or consent;
- conversation/participant authorization;
- batch confidentiality;
- send eligibility;
- provider-policy compliance;
- document acceptance/completeness;
- waived/not-required status;
- financial status;
- WorkItem status;
- worker entitlement/revenue share.

### Initial-rollout AI rules

- AI classification is a suggestion requiring human confirmation.
- AI-detected promised dates, non-applicable statements, or exceptions require human confirmation.
- AI message wording is generated only after deterministic code supplies the approved Contact, conversation, eligible requests, next-action items, hold state, and policy context.
- AI output does not bypass the outbox send-eligibility gate.
- Do not send raw client documents or full conversation history to an AI provider until the AI phase explicitly approves provider, data-processing, retention, region, and access controls.
- Store model/provider/version, prompt/template version, input references, output, confidence where applicable, and human accept/correct/reject result for audit/evaluation.
- Apply the same server-side authorization scope to AI retrieval that applies to the human user/action.

---

## 13. Mature Document Collection Dashboard

Target operational dashboard:

```text
DOCUMENT COLLECTION

Awaiting documents
Partially received
Overdue
Snoozed / promised
Complete this week
Needs classification
Needs authorization review
WhatsApp failures
SharePoint/storage failures
Dead-letter/manual recovery
```

Useful filters:

```text
Worker
Manager
Firm
Customer
PIC
Conversation
Service
Period
Request status
Overdue
Snoozed
Needs classification
Needs authorization review
Integration failure
```

The dashboard must not expose file/message data merely because a count is visible; every drill-down uses server-side access scope.

---

## 14. Phased Codex Luna Max Implementation Roadmap

Use **Codex Luna Max only** for this project.

The numbered phases below are project milestones. A milestone does **not** automatically equal one Codex run.

### Luna Max execution-unit rule

1. Each execution task has one primary objective and a clearly reviewable deliverable.
2. Prefer small sub-phases that can be understood and reviewed without mixing several architectural concerns.
3. Split database, provider, security, concurrency, migration, background processing, and UI work aggressively.
4. Do not ask Luna Max to design, implement, migrate, integrate external providers, harden security, and build UI in the same run.
5. After every coding sub-phase, ChatGPT reviews the actual GitHub diff/tests and updates this master plan before the next sub-phase.
6. Never begin the next phase silently.

### Phase 0 — Architecture Freeze — APPROVED

#### Phase 0A — Repository & Architecture Inventory — APPROVED

No production coding. Repository facts, existing workflow/role/database conventions, tests/CI, integration gaps, and constraints were inventoried and reviewed.

Key facts retained:

- ASP.NET Core 10 MVC/Razor, EF Core 10, Identity, PostgreSQL 17.
- Existing business graph: `Engagement → BillingRecord → WorkItem → WorkerAssignment`.
- Existing workflow is assignment-level and separate from financial/WorkItem state.
- Current five roles: Admin, InternalUser, AccountingFirm, Manager, Worker.
- Existing `AccessScope` is the server-side authorization pattern.
- Record entities use audit fields + `long Version`; restrictive deletes and migration safety conventions are established.
- No WhatsApp/SharePoint/provider/background-job abstraction existed at inventory time.
- Real PostgreSQL integration tests and CI conventions already exist.

#### Phase 0B — Core Data Model Freeze — APPROVED

**No production coding.**

### Phase 0B — Final Core Data Model Specification (2026-09-12)

The deterministic core is frozen as follows. The exact previously approved transition semantics and review history are also preserved by reference in Section 22 and remain binding if a condensed statement below is less specific.

#### Core entities and cardinalities

1. **`DocumentRequirementTemplate`** — versioned checklist definition for one `Service`.
   - `Service` 1 → many templates.
   - Stable template key + positive version.
   - `IsDefault ⇒ IsActive`.
   - At most one active/default version per service using a partial unique rule.
   - Used versions become immutable; historical versions remain addressable.

2. **`DocumentRequirementTemplateItem`** — one requirement inside one template version.
   - `(DocumentRequirementTemplateId, RequirementKey)` unique.
   - Snapshot fields include requirement key, name/description, required/optional, wave/priority, display order.
   - Used template items are immutable; change creates a new template version.

3. **`DocumentRequest`** — collection aggregate for one `WorkItem`.
   - One WorkItem → many request revisions; at most one current non-terminal request.
   - Selected template Service must transactionally match `WorkItem → BillingRecord → Engagement → ServiceId`.
   - Positive `Revision` unique within WorkItem.
   - Existing request retains selected template/version and snapshots.
   - No direct WorkerAssignment ownership.

4. **`DocumentRequestItem`** — frozen requirement instance.
   - One request → many items.
   - `(DocumentRequestId, RequirementKey)` unique.
   - Request reaches BillingRecord only through WorkItem.

5. **`ReceivedDocument`** — immutable raw received-artifact/intake record.
   - No direct WorkItemId/BillingRecordId/RequestId/RequestItemId.
   - May exist unclassified with zero evidence links.
   - Original receipt metadata immutable.
   - `DuplicateOfReceivedDocumentId` is assigned only once during an explicit `PendingReview → Duplicate` classification and then remains immutable; a duplicate points directly to a canonical artifact.
   - `SupersedesReceivedDocumentId` is creation-time replacement lineage only; replacement chains are acyclic and have at most one direct successor per artifact.
   - Lifecycle status and evidence links are separately mutable/audited.
   - Replacement creates a new row; old artifact remains history.

6. **`DocumentRequestItemEvidence`** — explicit auditable many-to-many evidence link.
   - `(DocumentRequestItemId, ReceivedDocumentId)` unique.
   - Active/inactive lifecycle; unlink/reactivate are explicit audited actions.
   - Satisfying evidence requires active link + Accepted artifact.
   - Cross-WorkItem reuse is never automatic and requires explicit audited classification plus 0C authorization checks.

7. **`DocumentRequestBatch` / membership** — provider-neutral grouping scaffold.
   - Request may appear in multiple historical batches.
   - Batch membership does not own request completion state.

8. **Append-only histories** for request, item, received artifact, and evidence actions.

#### Frozen linkage

```text
DocumentRequest → WorkItem → BillingRecord → Engagement
DocumentRequestItem → DocumentRequest
ReceivedDocument → zero or more DocumentRequestItemEvidence
DocumentRequestItemEvidence → DocumentRequestItem + ReceivedDocument
```

#### Request statuses

```text
Draft
ReadyToSend
Requested
PartiallyReceived
Complete
Paused
Cancelled
Superseded
```

- `Cancelled` and `Superseded` terminal.
- Completion requires all mandatory items satisfied plus human confirmation.
- Reopen/correction is explicit and audited.
- Request status is transactionally recalculated; callers cannot set arbitrary inconsistent state.

#### Item statuses

```text
Missing
Requested
PartiallyReceived
Received
NotRequired
Waived
```

- `Received` requires qualifying evidence plus explicit human completeness confirmation.
- `NotRequired` and `Waived` are reasoned/audited reversible decisions.
- Optional items do not block request completion.

#### Received-document statuses

```text
PendingReview
Quarantined
Accepted
Rejected
Duplicate
Superseded
```

- Accepted may be explicitly invalidated to Rejected or safety-held to Quarantined.
- Correction deactivates all affected active evidence links and recalculates every impacted item/request transactionally.
- Rejected/Duplicate/Superseded do not satisfy request items.

#### Key invariants

- Restrictive foreign keys; no ordinary deletes.
- `(ServiceId, TemplateKey, Version)`, `(DocumentRequirementTemplateId, RequirementKey)`, `(WorkItemId, Revision)`, `(DocumentRequestId, RequirementKey)`, and evidence pair uniqueness.
- One current request per WorkItem via PostgreSQL partial unique index.
- One active/default template per Service via PostgreSQL partial unique index.
- Request/template Service consistency checked transactionally from authoritative relationships.
- Hash is detection aid, not globally unique identity.
- optimistic concurrency + serializable/locking where needed; no silent overwrite.
- historical `DocumentRequested`/`DocumentReceived` workflow rows are not backfilled into synthetic document records.

#### Cancellation isolation

Cancelling one request freezes that request/items and inactivates only its evidence memberships. It never deletes/rejects/quarantines/supersedes the shared raw artifact or another request's memberships.

#### Phase 0C — Contact, WhatsApp & SharePoint Boundaries — APPROVED

**No production coding.**

### Phase 0C — Final Integration Boundary Specification (2026-09-12)

#### Contact/PIC model

1. **`Contact`** is the stable human/operational PIC identity.
   - Name, preferred language, active/inactive and audit fields live here.
   - Contact identity alone is not authorization.

2. **`ContactWhatsAppAddress`** represents an actual WhatsApp destination endpoint.
   - One Contact may have multiple historical/current endpoints; one active normalized endpoint maps to one Contact in the initial model.
   - Store normalized E.164 number, provider `wa_id` when known, primary/active flags, opt-in state/evidence, opt-out/do-not-contact state, and audit/version fields.
   - Consent/opt-out is evaluated at the address/channel level.

3. **`ContactCustomerLink`** maps one Contact/PIC to one or many Customers.
   - Many-to-many with active/effective state and optional role/note.
   - This confirms the PIC/customer relationship but does **not** by itself authorize every Engagement/conversation.

#### Conversation model

4. **`WhatsAppConversation`** is provider-neutral.
   - Type: `Direct` or `Group`.
   - Stores provider/account/business-sender references and one opaque provider thread/conversation key.
   - Status includes at least `Active`, `Inactive`, and `NeedsAuthorizationReview`.
   - Has a monotonically increasing `AuthorizationVersion`.
   - Provider-specific identifier structure is opaque to domain code.

5. **`WhatsAppConversationParticipant`** records active/historical membership.
   - Strongly map known participants to Contact/BusinessParty/Manager/AppUser/business sender where applicable; do not use an unvalidated generic entity-id string as the authorization source.
   - Snapshot provider participant key/phone/display information for audit.
   - Join/leave/remove/change is append-only/audited.

6. **`WhatsAppConversationEngagementScope`** is the explicit confidentiality grant for the **current participant set**.
   - Scope is Engagement-level, not merely Customer-level, because accounting firm/manager/service relationships may differ by Engagement.
   - Stores approval actor/time/reason and the conversation authorization version it approved.
   - Any participant-set change increments AuthorizationVersion and invalidates prior scope for new sends until authorised staff re-approve.

#### Conversation authorization rules

- No send/classification/reuse authorization comes from a phone-number match alone.
- Direct send requires active ContactWhatsAppAddress + ContactCustomerLink + approved conversation Engagement scope + consent/policy gates.
- Group send requires current group capability + known participant set + approved Engagement scope for that exact authorization version.
- Provider/webhook identity is not an ASP.NET Identity principal.
- Unknown inbound senders/events may be ingested/audited but cannot mutate business state until mapped/authorised.

#### Batch confidentiality rules

Before `DocumentRequestBatch` can become send-ready:

- all requests have the same Contact/PIC and conversation;
- Contact is linked to each request Customer;
- each request Engagement is approved in current conversation scope;
- conversation is Active and not NeedsAuthorizationReview;
- current participant set/version matches approval;
- destination consent/do-not-contact/policy gates pass;
- no request-level confidentiality block exists.

The batch snapshots ContactId, ConversationId, AuthorizationVersion, participant set/hash, engagement/request memberships, and exact message facts. Any relevant change before queueing invalidates the batch.

#### Provider boundary

Use an `IWhatsAppProvider`-style adapter. Business/domain logic must not depend on Meta DTOs/endpoints.

Provider capability checks cover at least Direct, Group, media, template/policy, account eligibility, rate-limit/health. Group capability is optional and runtime/account dependent; Direct fallback remains valid.

As of 2026-09-12, Meta Cloud API remains the official WhatsApp Business Platform integration surface. Current Meta materials also expose group capabilities, but exact eligibility, participant limits, pricing, templates and service-window rules remain implementation-time verification items and are **not** frozen as domain invariants.

#### SharePoint boundary

- Dedicated SharePoint site + dedicated document library preferred.
- App-only least privilege preferred; use Microsoft Graph Selected permission scopes where supported/operationally practical, with explicit resource assignment.
- Do not silently escalate to tenant-wide permissions if selected scope fails; require explicit security approval.
- Prefer certificate credential where practical; otherwise secure/rotate server-side secret.
- No credentials in browser, repo, generated links, or normal logs.

#### Artifact-centric permanent storage

- Upload one permanent binary per ReceivedDocument.
- Store under intake/date hierarchy using server-generated stable filename.
- Preserve original filename as metadata.
- Canonical identity is `DriveId + ItemId`, not path/WebUrl.
- Do not auto-move/copy into one customer/WorkItem folder after classification.
- Permanent SharePoint metadata is artifact-level; DB evidence links own zero/many request relationships.

#### Storage reference / temporary media

- Keep provider-neutral storage state/reference separate from ReceivedDocument lifecycle.
- Temporary media is private, bounded, non-webroot and deleted after permanent storage.
- Scanner/validation failure prevents artifact acceptance.
- Missing/deleted SharePoint object becomes reconciliation failure; DB history remains.

#### Application access boundary

Initial access policy:

- **Admin/InternalUser:** full collection operations subject to server-side scope; manage contacts/conversation scope; classify; accept/reject; cross-WorkItem reuse; send/pause/recover.
- **Worker:** no global unclassified inbox. After explicit evidence/scoping to an accessible WorkItem, may view/use documents according to worker assignment scope. Future scoped worker-classification must be explicit, not inferred from conversation alone.
- **Manager:** read-only collection status for matching manager engagements; raw file download disabled by default unless a later explicit permission policy grants it.
- **AccountingFirm:** read-only collection status for matching firm engagements; raw file download disabled by default unless explicitly approved later.
- **Customer/PIC WhatsApp sender:** not an Identity application user by default.

Direct SharePoint URLs/anonymous sharing are not the application authorization mechanism. File open/download from Billing Control requires server-side access checking and is audited.

#### Phase 0D — Automation, Reliability & Final Freeze — APPROVED

**No production coding.**

### Phase 0D — Final Automation / Reliability / Security Specification (2026-09-12)

#### Outbound batch lifecycle

`DocumentRequestBatch` communication lifecycle is separate from request completeness:

```text
Draft
Ready
Queued
Sent
Failed
Cancelled
Invalidated
```

- Draft membership may be prepared/reviewed.
- Ready means deterministic eligibility checks passed at preview time.
- Ready → Queued revalidates authorization version, participants, request revisions, consent/holds and provider policy; then freezes the exact outbound snapshot and commits an outbox row in the same transaction.
- Queued content/membership is immutable.
- Sent means provider accepted the logical message and provider message/reference is persisted.
- Failed is permanent/manual-attention after retry policy.
- Invalidated means preview became stale before queue/send; build a new batch.
- Never resend an already Sent batch as a follow-up; each follow-up is a new batch/snapshot.

#### Request activation timing

A `DocumentRequest` becomes `Requested` only when at least one outbound message containing that request is successfully accepted by the provider. Queueing alone does not claim the client was requested. Failed/invalidated batches do not fabricate request-send history.

#### Follow-up control

Maintain durable Contact+Conversation follow-up control containing at least last automated send, next eligibility, stage, pause/stop state and concurrency/audit fields. Scoped promised-date/snooze holds may target one request, one batch, or Contact+Conversation.

Automation eligibility is evaluated in this precedence order:

1. security/provider health block;
2. ContactWhatsAppAddress do-not-contact / consent block;
3. conversation Active + current authorization scope/version;
4. request/batch validity and outstanding deterministic facts;
5. promised-date/snooze/pause/stop holds;
6. global Contact+Conversation cooling-off/business-time window;
7. provider template/service-window/rate-limit rules;
8. create fresh reviewed batch/outbox work.

No later rule overrides an earlier safety/confidentiality block.

After configured escalation/max automatic stage, automation stops and moves to manual attention rather than sending indefinitely.

#### Workflow integration

- Document collection does not auto-change WorkItem/Billing/invoice/receipt/payment/revenue-share/worker-entitlement state.
- Assignment `WorkflowStatus` remains separate.
- First successful outbound request creates UI state **Ready to mark Document Requested**.
- Human chooses eligible assignment(s) and existing workflow service validates transition.
- Human-confirmed request completion creates **Ready to mark Document Received**.
- Human chooses eligible assignment(s); no blind transition of all assignments.
- Multiple assignments are never silently synchronized merely because they share one WorkItem.

#### Durable inbound webhook pattern

Webhook endpoint performs minimal synchronous work:

```text
receive raw request
→ validate provider verification/signature on raw body
→ derive provider-specific stable EventKey
→ insert ProviderEventInbox if new
→ commit
→ return success/duplicate acknowledgement
```

Heavy parsing, media retrieval, storage, classification assistance and status propagation occur asynchronously after durable inbox commit.

Unique `(Provider, EventKey)` (or equivalent authoritative provider scope) prevents duplicate processing. For inbound messages/media also enforce stable provider message/media identity so replay cannot create duplicate ReceivedDocument rows.

Invalid signatures fail before business parsing/mutation.

#### Durable outbound outbox pattern

All outbound WhatsApp sends originate from a DB outbox row created transactionally with the frozen batch/message snapshot.

Outbox records at least:

```text
LogicalMessageKey / idempotency key
Conversation / batch
Exact content or template+parameters snapshot
AuthorizationVersion / participant snapshot reference
State
Attempt count
NextAttemptAt
ProviderMessageId/reference
Last error classification
CorrelationId
Version/audit
```

Initial deployment may use a PostgreSQL-backed hosted background worker inside the single app process. Work claiming must use durable row leasing/locking (`SKIP LOCKED`/lease-equivalent) so restart/concurrency cannot double-process. Architecture must allow later worker extraction without changing domain semantics.

#### Ambiguous-send safety

Exactly-once delivery cannot be assumed from an external provider.

- If a request definitely failed before provider acceptance, normal retry is permitted.
- If timeout/network failure leaves acceptance ambiguous, do **not** blindly create/send a second logical message.
- Reconcile with provider status/id where possible; otherwise move to manual/ambiguous-send review.
- Provider Retry-After/rate-limit signals take precedence over local backoff.

#### SharePoint/storage job reliability

One ReceivedDocument has one logical permanent-storage operation/reference.

- Retry must resume/reconcile the same logical storage object, not create duplicate permanent files.
- Use a simple upload path for files within the supported safe threshold and a resumable upload-session path for larger permitted files.
- Graph currently supports resumable upload sessions; application maximum size may be lower and remains configuration.
- Replacement/correction never overwrites the original binary as the normal path.

#### Retry / dead-letter policy

All durable integration work distinguishes:

```text
Pending
Processing / leased
RetryPending
Succeeded
FailedPermanent / DeadLetter
Cancelled where applicable
```

Rules:

- exponential backoff + jitter for transient failures;
- provider Retry-After respected;
- configurable max attempts;
- permanent validation/auth/config errors do not spin forever;
- dead-letter/manual retry visible in dashboard;
- manual retry requires authorised actor/reason and appends history;
- retry never bypasses current authorization/consent when the operation is an outbound send.

#### Idempotency keys

At minimum:

- provider event inbox: provider-scoped EventKey unique;
- inbound message: provider business endpoint + ProviderMessageId unique;
- inbound media/artifact: stable provider message/media/attachment identity unique enough to prevent duplicate ReceivedDocument creation;
- outbound logical message: immutable LogicalMessageKey unique;
- SharePoint storage: ReceivedDocument has one active logical permanent-storage reference;
- human commands that may be double-submitted use request/idempotency key where materially harmful.

Never use SHA-256 alone as business idempotency identity.

#### Audit requirements

Append-only audit/history must cover:

- contact/WhatsApp endpoint consent changes;
- conversation creation/type/provider binding;
- participant join/leave/remove and authorization-version changes;
- Engagement-scope approvals/revocations;
- batch preview/validation/invalidations;
- exact outbound message snapshot and provider statuses;
- inbound provider events/messages/media identity;
- storage attempts/failures/reconciliation;
- classification/evidence link/unlink/reuse;
- accept/reject/quarantine/correction;
- request/item status transitions;
- promises/snoozes/pause/resume/stop/re-enable;
- manual follow-up/cooldown override;
- dead-letter/manual retry;
- workflow-transition confirmation;
- document open/download by application users;
- AI suggestion and human disposition when AI is introduced.

Use correlation IDs across webhook → inbox → media → ReceivedDocument → storage → classification and batch → outbox → provider delivery chains.

#### Security constraints

- Extend existing server-side `AccessScope` pattern; do not authorize from client-hidden fields or CreatedBy.
- UI state-changing endpoints retain authentication/authorization + CSRF protections.
- Provider webhook is not CSRF-authenticated; it requires provider verification/signature, strict body/size limits, rate limiting and replay/idempotency controls.
- Verify webhook signature over original raw bytes with constant-time comparison before processing JSON.
- Credentials/tokens/certificates stay server-side and out of logs.
- Structured logs redact/minimize phone numbers, message text, filenames, payloads and document metadata; binaries are never logged.
- File download is authorized at request time; do not issue anonymous/public SharePoint sharing links as the normal mechanism.
- Cross-WorkItem reuse is staff-only initially and must pass target WorkItem authorization plus audited reason.
- Participant changes block outbound communication until engagement scope is re-approved.
- Scanner unavailable/failed means not safe, not implicitly accepted.
- No automatic permanent deletion/retention purge is introduced until legal/business retention policy is explicitly approved.

#### AI boundaries

- No AI is used for authorization, consent, policy enforcement, deterministic status, financial state, or send eligibility.
- Initial AI features remain suggestion-only with human confirmation.
- Outbound AI wording passes the same deterministic eligibility gate as manual wording.
- Raw files/conversations are not sent to an AI provider until Phase 17/18 explicitly approves data-processing/retention/security conditions.
- Record enough AI provenance to evaluate errors and reproduce the operational decision context without making the model output authoritative.

#### Test / CI acceptance gates for implementation

Relevant later phases must add tests for:

- ContactWhatsAppAddress normalization/uniqueness/consent precedence;
- participant-change authorization invalidation;
- Engagement-scope and cross-company batch confidentiality;
- stale batch rejection at queue time;
- unclassified artifact access boundaries;
- cross-WorkItem evidence reuse authorization;
- request activation only after provider-accepted send;
- valid/invalid webhook signatures;
- duplicate/replayed provider events/messages/media;
- outbox worker concurrency/restart/idempotency;
- ambiguous-send handling;
- retry/backoff/dead-letter/manual retry;
- SharePoint single-object retry/reconciliation and temporary-file cleanup;
- role/access tampering and file-download authorization;
- workflow human-confirmation isolation from financial/WorkItem state.

CI uses deterministic provider/storage fakes and fixtures without production credentials. Live Meta/SharePoint smoke tests, when added, run only in a controlled environment with explicitly provisioned test resources.

#### External configuration prerequisites / intentionally dynamic items

Before relevant integration coding, verify the actual environment:

- Meta Business Portfolio / WhatsApp Business Account / business phone number and Cloud API access.
- Whether the actual Meta account/number currently supports Groups API; exact current eligibility and limits.
- Current template, service-window, opt-in, pricing and rate-limit rules.
- Webhook app secret/verification configuration.
- Microsoft Entra app registration and tenant-admin consent path.
- Dedicated SharePoint site/library IDs and ability to grant Selected app permissions.
- App credential method and rotation plan.
- Approved scanner/file-size/temp-spool settings.
- Business/legal retention policy for permanent client documents and message/audit data.

If an external prerequisite is unavailable, implementation stops at the approved abstraction/fake boundary rather than weakening security or inventing credentials.

### Phase 0 Final Architecture Readiness

**Phase 0 is approved and frozen.**

No blocking core entity, authorization boundary, storage boundary, workflow boundary, automation rule, reliability pattern, security constraint, or AI authority question remains for beginning the staged implementation roadmap.

The following remain intentionally dynamic and are not architecture blockers: current provider account eligibility, numeric API limits/pricing, tenant grants, exact implementation class/table names, file-size/temp TTL settings, scanner product, and legal retention duration. Those are verified/configured in their implementation phase without violating the frozen boundaries above.

Phase 1 may begin only after explicit instruction and must not silently implement Phase 4/7/8/9/14+ concerns ahead of schedule.

### Phase 1 — Core Database Model

Add only the Phase 0B structural entities/enums/migration/tests required for the deterministic core model.

No Contact/PIC implementation yet. No WhatsApp API. No SharePoint API. No background provider jobs. No AI.

### Phase 1 execution split

Phase 1 is deliberately split into the following bounded execution units:

- **1A — Core Document Domain Types:** add the approved CLR enums and `Record`-derived document-collection entities, snapshots, self-references, evidence lifecycle, batch scaffold, and append-only history types. **Approved and closed.**
- **1B — EF Core Persistence:** add `AppDbContext` entity discovery/relationships, restrictive foreign keys, PostgreSQL constraints/indexes, service/template consistency enforcement, immutable-field/template guards, lineage checks, and the EF migration. **Approved/closed against `745ec1c7e5f69890530514209a3a7e7309cfcaf9`.**
- **1C — PostgreSQL Tests:** add integration coverage for persistence invariants, migration application, template/service consistency, request and artifact lineage, evidence/history rules, and optimistic concurrency. **Approved/closed against `78dd826e2d23941e59677836a9693180ae71f2fe`; CI run `34662113750` passed.**

1A and 1B are complete only within their stated boundaries. Do not start 1C automatically; it requires explicit review/approval of 1B.

### Phase 2 — Document Requirement Templates

Add reusable service-based templates with Required/Optional, Priority/Wave, display order, version/default/active rules and service consistency.

### Phase 2 execution split

Phase 2 is deliberately split into the following bounded execution units:

- **2A — Template application/service layer:** provide staff-side application operations and DTO/read models for creating the first version, cloning the next version, editing unused versions/items, explicit activation/default management, deterministic ordering, and service-scoped queries. **Approved/closed against `f9f81bb22ab78e5986a102f071b9f7f5d03f471a`; final GitHub Actions run `34663809281` passed.**
- **2B — Internal template management UI:** wire the service into internal Razor/MVC UI and staff-only endpoint authorization using the existing `AppRoles.Staff` boundary. **Technically approved against `09208aba77477db413fdf31dbf387864ca2773dd`; GitHub Actions run `34667389872` passed.**
- **2C — Phase 2 business/integration tests and review closure:** add the focused service/UI/integration regression coverage and close the technical review. **Technical regression/review complete; final manual user UI acceptance remains pending.**

2A does not create document requests, change financial/WorkItem/WorkerAssignment state, add provider integrations, or alter the approved Phase 1 schema/migration.

### Phase 3 — Document Request Application and Internal Workflow

Build deterministic request/checklist UI and manual state transitions before external integrations.

### Phase 3 execution split

Phase 3 is deliberately split into the following bounded execution units:

- **3A — Document Request application/service layer:** create Draft requests from active service templates, snapshot active checklist items, allocate WorkItem revisions transactionally, support only the approved pre-provider request/item transitions, append status histories, and expose untracked read models. **Complete and awaiting ChatGPT final approval.**
- **3B — Internal Document Request/checklist UI:** wire the 3A service into staff-authorized MVC pages for request creation, checklist administration, and the approved pre-provider transitions. **Not started.**
- **3C — Phase 3 integration/regression and manual acceptance:** add focused PostgreSQL/MVC regression coverage and complete the relevant technical/manual review. **Not started.**

Phase 3A deliberately does not set `Requested`, `PartiallyReceived`, `Complete`, or `Superseded`, create received artifacts/evidence/batches/outbox rows, send through a provider, or change WorkItem, WorkerAssignment, BillingRecord, invoice, payment, or revenue-share state. Those operations remain deferred to the later provider/evidence/workflow phases.

### Phase 4 — Contact / PIC Model

Implement approved Contact, ContactWhatsAppAddress, ContactCustomerLink, consent/opt-out and management UI. No WhatsApp sending yet.

### Phase 5 — Request Batching

Implement deterministic batch eligibility/confidentiality and preview using Contact + conversation/scope placeholders or approved data available by that phase. No provider send yet.

### Phase 6 — Request Waves / Client Workload Control

Implement Start Work / Normal / Later / Optional ranking and default 3–5 client-facing next-action selection.

### Phase 7 — SharePoint Integration Foundation

Implement storage abstraction/reference model first, then SharePoint provider, then retry/reconciliation/test path as separate focused sub-phases.

### Phase 8 — WhatsApp Infrastructure

Implement provider abstraction/config, webhook authenticity/inbox, outbound outbox/worker and provider fakes in small sub-phases. Manual/test sending only.

### Phase 9 — WhatsApp Group / Direct Conversation Management

Implement conversation/participants/Engagement scope/authorization-version management and runtime provider capability handling.

### Phase 10 — Manual WhatsApp Document Request

Implement Preview → revalidate → Queue → Send flow. Persist exact immutable outbound snapshot/provider status. Request becomes `Requested` only after provider acceptance. Assignment workflow remains human-confirmed.

### Phase 11 — Incoming WhatsApp Messages

Implement inbound event/message timeline and provider-event processing. Split webhook ingestion/persistence from timeline/media handling.

### Phase 12 — Incoming Document → SharePoint

Implement safe media handling, validation/scanning, one-object SharePoint persistence, retry/reconciliation and temporary cleanup in small sub-phases.

### Phase 13 — Manual Document Classification

Build fast staff/authorised-worker classification UI using explicit evidence links and Phase 0C authorization rules.

### Phase 14 — Automatic Follow-Up Engine

Implement deterministic Contact+Conversation scheduling, holds, cooling-off, batch creation/outbox integration and operational controls in separate sub-phases.

### Phase 15 — Promised Date Handling

Implement scoped promised-date/snooze behaviour and precedence rules.

### Phase 16 — Workflow Automation

Implement **human-confirmed** assignment workflow integration for Document Requested / Document Received readiness. Do not auto-drive financial or WorkItem state.

### Phase 17 — AI Document Classification

Implement suggestion-only classification with approved provider/data controls and human confirmation.

### Phase 18 — AI Conversation Understanding

Implement suggestion-only promise/not-applicable intent detection with human confirmation.

### Phase 19 — Smart Follow-Up Composition

Use deterministic approved facts and AI only for wording. Same deterministic send gate applies.

### Phase 20 — Mature Document Collection Dashboard

Add operational KPIs, PIC/conversation view, outstanding/holds/classification/auth-review/integration-failure queues and scoped drill-down.

### Phase 21 — Audit / Reliability / Security Hardening

Verify the frozen Phase 0D controls across integration paths; split into focused reliability/security sub-phases rather than one mega-run.

### Phase 22 — Controlled Production Rollout

```text
Stage 1 — Internal test company
Stage 2 — 2–3 friendly customers
Stage 3 — ~10 customers
Stage 4 — Normal production
```

Monitor reminder complaints, completion time, authorization-review blocks, provider failures, storage failures, dead letters, classification corrections, workflow overrides and support load.

---

## 15. Codex Luna Max Capacity Planning

Rules:

- **Codex Luna Max is the only Codex model used for this project.**
- Prefer one cohesive, reviewable execution unit at a time.
- Any milestone requiring independent schema, domain logic, integration, concurrency, security, UI and tests must be split.
- External-integration, database, concurrency, security, migration and reliability work are split more aggressively.
- Never combine sub-phases merely to reduce the number of Codex runs.
- ChatGPT defines/controls architecture and reviews actual diffs; Codex implements the approved small unit; GitHub remains the source of truth.

---

## 16. Testing Expectations Per Phase

Every coding phase includes tests appropriate to the change.

At minimum where relevant:

- .NET Release build
- PostgreSQL integration tests
- JavaScript tests
- authorization/security tests
- concurrency/idempotency tests
- migration/model consistency checks
- GitHub Actions
- Docker/Compose smoke where applicable

Integration phases additionally use provider/storage fakes and controlled smoke evidence without exposing production secrets.

Do not deploy automatically unless explicitly instructed for rollout.

---

## 17. Codex Working Rules

For every implementation phase/sub-phase:

1. Use **Codex Luna Max only**.
2. Start from the latest approved SHA and pull any ChatGPT GitHub updates first.
3. Read this master plan and relevant current repository code.
4. Implement **only** the approved current sub-phase plus directly required support.
5. If scope expands, stop at a safe boundary and propose the next sub-phase.
6. Do not silently begin the next phase.
7. Preserve current Billing Control business logic unless explicitly changed by an approved rule.
8. Do not modify the existing 35/25/40 revenue-share logic.
9. Do not weaken role-based access, historical workflow, audit, concurrency or financial isolation.
10. Add migrations only when the phase explicitly requires schema changes.
11. Report files changed, migration(s), tests, CI and final SHA.
12. Stop when Meta/SharePoint/tenant configuration blocks safe work rather than bypassing controls.
13. Synchronise deployment/default branches only after review/approval according to the project workflow.

---

## 18. Deferred / Verify-at-Implementation Items

These are intentionally dynamic and are not hard-coded as business invariants:

- current Meta WhatsApp Groups API eligibility and numeric participant/account limits;
- current WhatsApp message/template pricing;
- current template category/classification/service-window/opt-in rules;
- current provider rate limits;
- current Microsoft Graph permission/endpoint details and tenant support;
- exact SharePoint site/library resource grants;
- exact app credential method/rotation;
- maximum accepted file size and temporary spool TTL;
- malware-scanner product;
- business/legal retention duration;
- current AI model/provider choice, pricing and data-processing terms.

Verify current official documentation/configuration in the relevant implementation phase.

---

## 19. Definition of the Mature End State

The feature is mature when Billing Control can reliably do this:

```text
1. A Work Item identifies required documents from a versioned service template.
2. PIC/contact + conversation authorization determines which requests may be batched.
3. Staff preview only the next manageable 3–5 items.
4. Authorization/consent/policy is revalidated before every queued send.
5. Billing Control sends to an eligible Group or Direct conversation through a durable outbox.
6. Firm/manager/client/LCM MGT see only the conversation/scope they are authorised for.
7. Inbound webhook events are signature-verified, deduplicated and durably processed.
8. Raw received files are safely validated/scanned and stored once in SharePoint.
9. Explicit evidence links classify artifacts to zero/one/multiple request items without duplicating the binary.
10. Follow-ups are consolidated, rate-limited and respect promised dates/snooze/pause/stop.
11. All mandatory request items require deterministic state plus human completeness confirmation.
12. Assignment workflow changes remain human-confirmed and separate from financial/WorkItem state.
13. Dashboard exposes classification, authorization review, storage/provider failure and dead-letter queues.
14. AI assists classification/understanding/wording without becoming the source of truth.
15. Every sensitive action has authorization, concurrency protection, audit and safe retry semantics.
```

---

## 20. Next Action

**Phase 0 is approved and complete. Phase 1A is approved/closed. Phase 1B is approved/closed against `745ec1c7e5f69890530514209a3a7e7309cfcaf9`. Phase 1C is approved/closed against `78dd826e2d23941e59677836a9693180ae71f2fe`; Phase 1 is complete. Phase 2A is approved/closed against `f9f81bb22ab78e5986a102f071b9f7f5d03f471a`; final GitHub Actions run `34663809281` passed. Phase 2B is technically approved against `09208aba77477db413fdf31dbf387864ca2773dd`; GitHub Actions run `34667389872` passed. Phase 2C technical regression/review is approved/complete. Phase 2 remains pending final manual user UI acceptance, and the user authorized continuing development before that acceptance. Phase 3A is complete and awaiting ChatGPT final approval; Phase 3B/3C and Phase 4 have not started.**

The approved Phase 1 split is: 1A — Core Document Domain Types; 1B — EF Core persistence, relationships, constraints, indexes and migration; 1C — PostgreSQL integration, invariant and migration tests. Phase 1A, 1B and 1C are approved/closed; Phase 1 is complete.

Phase 1A implemented only the deterministic Phase 0B CLR domain types. Phase 1B added only their EF Core persistence mapping, PostgreSQL constraints/indexes, immutable-field/template guards, request/artifact lineage checks, referenced-row service/template consistency triggers, duplicate classification and received-document acyclicity guards, concurrency/history persistence conventions, and one additive migration. Phase 1C added only PostgreSQL-backed persistence/invariant coverage; Contact/PIC, WhatsApp, SharePoint, follow-up automation and AI remain outside this Phase 1 split even though their architecture is now frozen.

Phase 2A adds only the deterministic `DocumentRequirementTemplateService` and its application DTO/read models. First versions are allocated as version 1 for a new service/template key; new versions clone the prior definition and use the previous maximum plus one. Version allocation, cloning and default switching use PostgreSQL transactions and a `SELECT ... FOR UPDATE` lock on the authoritative Service row; unique/serialization conflicts become clear retryable business errors. Default switching clears existing defaults before setting the target, and operational/default templates must retain at least one active item. Used versions are rejected by the service before edits, existing Phase 1 persistence guards remain authoritative, item keys are stable, and no hard-delete operation is provided. Queries use untracked read models and expose version/default/active/used state plus display-ordered items. Phase 2A is approved/closed against `f9f81bb22ab78e5986a102f071b9f7f5d03f471a`; final GitHub Actions run `34663809281` passed.

### Phase 2B internal template management UI record

- `src/BillingControl/Controllers/DocumentRequirementTemplatesController.cs` adds the staff-only `/DocumentRequirementTemplates` MVC route family. Every read and mutation is protected by `[Authorize(Roles = AppRoles.Staff)]`; mutations use antiforgery-protected POST actions and call `DocumentRequirementTemplateService` rather than changing template entities in the controller. Only Service names/options are read directly for supporting lookup/display data.
- `src/BillingControl/Views/DocumentRequirementTemplates/Index.cshtml`, `Create.cshtml`, and `Edit.cshtml` provide service filtering, Service → TemplateKey → version history, first-version creation with initial requirements, unused-version definition/item editing, server-rendered reorder actions, new-version cloning, explicit activation/default actions, and clear Default/Active/Used/Unused indicators. Used versions remain read-only for definition/checklist fields while lifecycle controls remain available. The existing `DocumentRequirementWave` enum is presented through human-friendly labels; no second wave/status definition was introduced.
- `src/BillingControl/Views/Shared/_Layout.cshtml` adds a staff-only Document templates navigation entry. `src/BillingControl/wwwroot/js/document-templates.js` only supports adding/removing initial form rows and does not perform mutations or bypass MVC/antiforgery.
- `tests/BillingControl.Tests/DocumentRequirementTemplateUiTests.cs` provides PostgreSQL-backed MVC coverage for anonymous/external-role denial, Admin/InternalUser access, validation display, create/edit/add/reorder/activation/version/default flows, inactive history/filtering, used-version read-only presentation and lifecycle operations, foreign-ID rejection, POST/antiforgery enforcement, and financial/WorkItem/WorkerAssignment isolation.

### Phase 2C technical regression/review record

- `tests/BillingControl.Tests/DocumentRequirementTemplateServiceTests.cs` adds coverage for multiple template families, Service/TemplateKey filtering, deterministic family/version ordering, inactive historical visibility, and repeated item reordering.
- `tests/BillingControl.Tests/DocumentRequirementTemplateUiTests.cs` extends the existing UI flow without duplicating the Phase 2A/2B suites: it exercises unused-version add/edit/activate/deactivate/reorder operations, clone contents, cross-family default switching, inactive historical display, service/key filters, used-template lifecycle operations, malformed/foreign IDs, and unchanged financial/workflow counts.
- Code review confirms all template mutations remain behind `DocumentRequirementTemplateService`; the controller only performs supporting Service lookup reads. All state-changing actions remain POST-only and antiforgery-protected, while the controller class applies `[Authorize(Roles = AppRoles.Staff)]` to every action.
- The first CI run for the expanded Phase 2C coverage exposed one real UI defect: the existing-item edit form did not post the immutable `RequirementKey` required by the shared form model, so valid edits returned the form instead of saving. The form now posts that key as a hidden identity field, the controller/service still ignore caller changes to it, and the corrected regression path passes locally and in final CI run `34670654693`.
- Local checks pass with 67 total .NET tests: 19 passed and 48 PostgreSQL-backed tests skipped because no isolated `BILLING_TEST_CONNECTION` is configured; 12 JavaScript tests passed. GitHub Actions run `34670654693` executed the PostgreSQL 17 suite with 67 passed, 0 failed, and 0 skipped, including Docker image build and production Compose HTTPS smoke checks. The local machine has no Docker executable, so the Compose check was verified in CI.
- No Phase 2 production defect, Phase 1 defect, migration, or new feature was found or introduced. Phase 2 is technically ready but deliberately remains pending final manual user UI acceptance; Phase 3A is complete and Phase 3B/3C have not started.

### Phase 3A document request service record

- `src/BillingControl/Services/DocumentRequestService.cs` adds deterministic provider-neutral application operations and DTO/read models. The service resolves the authoritative Service only through `WorkItem → BillingRecord → Engagement`, requires an active matching template with at least one active item, snapshots active template items into a new `Draft` request, starts every item as `Missing`, writes initial request/item histories with explicit actor/source, and allocates the first or next available positive WorkItem revision.
- Request creation uses a PostgreSQL serializable transaction plus `SELECT ... FOR UPDATE` locks on the authoritative Service and WorkItem rows. Current-request and revision uniqueness remain database safety nets; expected unique/concurrency/serialization conflicts become `BusinessException` retry messages. Request/item transitions use the `Record.Version` token and row locks, append histories with actor/source/reason, and do not modify financial, WorkItem, WorkerAssignment, or assignment workflow state.
- Only these pre-provider request transitions are implemented: `Draft → ReadyToSend/Paused/Cancelled`, `ReadyToSend → Paused/Cancelled`, and `Paused → Draft/ReadyToSend/Cancelled`. Only `Missing → NotRequired/Waived` and `NotRequired/Waived → Missing` item actions are implemented. `Requested`, `PartiallyReceived`, `Received`, `Complete`, and `Superseded` remain deferred to provider/evidence/replacement operations.
- `tests/BillingControl.Tests/DocumentRequestServiceTests.cs` covers active-item snapshots, service/template validation, current-request blocking, terminal-history revision allocation, available-template/default read models, initial histories, pre-provider transitions, stale request/item versions, terminal cancellation, deterministic request/item history ordering, cancellation freeze, concurrent creation, and financial/workflow isolation. No migration or schema change was made; Phase 3B/3C and provider/evidence work have not started.
- Local checks passed with 72 total .NET tests: 19 non-PostgreSQL tests passed and 53 PostgreSQL-backed tests skipped because no isolated `BILLING_TEST_CONNECTION` is configured; 12 JavaScript tests passed; EF reported no pending model changes. The first CI run exposed only a test-fixture assertion that assumed one item where the fixture intentionally created two; the assertion was narrowed to the transitioned item. Final Phase 3A validation is GitHub Actions run `34671877581` for commit `6578765eaabb1a66d5c929e6c0f9dc751cddfe85`; it executed all 72 tests against PostgreSQL 17 with 0 failures and 0 skips, and passed the Docker image and production Compose HTTPS smoke checks.

---

## 21. AI Development Rules

This file and the current repository are the authoritative source of truth. Chat history or AI memory may provide context but cannot override the current repository/plan.

For every development phase/sub-phase:

1. ChatGPT owns architecture/design review and project control; Codex implements approved small units.
2. Use **Codex Luna Max only** for Codex work.
3. Read this master plan before changing code.
4. Review current repository implementation relevant to the unit.
5. Keep execution small/focused; split broad work first.
6. Work only on the current approved sub-phase.
7. Do not silently change architecture, business rules, database behaviour, security, external permissions, or financial logic.
8. Review actual GitHub diffs, not only Codex summaries.
9. Run/review relevant tests including regression, authorization, concurrency, idempotency and data-integrity checks.
10. Update this master plan when requirements/implementation decisions/status/risks change.
11. Preserve project history and previously approved decisions.
12. Do not begin the next phase/sub-phase until the current one has been reviewed and approved.

---

## 22. Preserved Phase 0A / 0B Approval History and Exactness

The architecture was refined across several reviewed commits. The current file is the operational source of truth, but it intentionally preserves the stricter previously approved Phase 0A/0B semantics rather than weakening them through the later condensed restatement.

Historical approval anchors:

- `d8149bfe2350b97a9cbcb4f7092963cf32fa83a8` — Phase 0A review approved.
- `7ef2b4ee26e92b9a3955201c66dc1f6aa4225045` — initial Phase 0B core-model freeze.
- `7116cbc061421a37188a29ece14f4a471def5e64` — raw-intake and accepted-document correction amendments.
- `3fb6601cd8b7cc65bc36f5a99e31ae3874e0db33` — final Phase 0B data-integrity corrections.
- `a417225b3829db3b78687e5eca39099ebb302598` — final pre-0C/0D master plan with detailed Phase 0A inventory and full Phase 0B specification.

If a later condensed sentence is less specific than those approved rules, implementation must follow the stricter approved semantics below and must not infer permission to broaden access, relax invariants, or skip audit/concurrency controls.

### Preserved Phase 0A constraints

- Existing document-requested/document-received workflow values are assignment workflow labels, not document/file evidence.
- Existing roles and `AccessScope` must not be weakened; a phone number is never authorization.
- Financial/Billing/WorkItem state remains isolated from document collection.
- Existing database conventions remain applicable: restrictive foreign keys, protected historical fields, audit fields, `long Version`, safe migrations and isolated `_test` PostgreSQL integration databases.
- Existing production architecture had no WhatsApp/SharePoint/background worker; integration reliability therefore requires explicit new boundaries rather than hidden controller-side network calls.
- Existing historical `DocumentRequested`/`DocumentReceived` rows remain legacy workflow context and are never synthetically backfilled into new document entities.

### Preserved exact request-level transitions

```text
Draft              → ReadyToSend, Paused, Cancelled
ReadyToSend        → Requested, Paused, Cancelled
Requested          → PartiallyReceived, Complete (human confirmation), Paused, Cancelled, Superseded (replacement only)
PartiallyReceived  → Complete (human confirmation), Requested (after qualifying evidence withdrawn/rejected), Paused, Cancelled, Superseded (replacement only)
Complete           → PartiallyReceived or Requested through explicit reopen, Cancelled, Superseded (replacement only)
Paused             → Draft, ReadyToSend, Requested, or PartiallyReceived through explicit resume/recalculation; or Cancelled
Cancelled          → terminal
Superseded         → terminal
```

Additional preserved request rules:

- No direct jump to Complete from an unissued request.
- Paused preserves prior state/history and resume recalculates safely.
- Cancelled never reopens; a new request revision is required.
- Replacement supersedes the old request and creates the new revision in one transaction.
- Reopening Complete is explicit/audited and never reopens financial/WorkItem/assignment state automatically.

### Preserved exact request-item transitions

```text
Missing            → Requested, PartiallyReceived, NotRequired, Waived
Requested          → PartiallyReceived, Received, NotRequired, Waived
PartiallyReceived  → Received, Requested, NotRequired, Waived
Received           → PartiallyReceived, Requested, NotRequired, Waived by explicit correction/reopen only
NotRequired        → Missing or Requested by explicit reactivation only
Waived             → Missing or Requested by explicit reactivation only
```

Additional preserved item rules:

- `Received` requires at least one active Accepted evidence link plus human completeness confirmation.
- Rejected, Duplicate, Quarantined, or Superseded artifacts never satisfy an item.
- Removing/correcting the last qualifying evidence explicitly reopens/recalculates the item/request with history.
- `NotRequired` and `Waived` require reason/actor and remain reversible/audited.
- Optional items do not block request completion.
- Child items under Cancelled/Superseded requests remain historical and frozen for ordinary changes.

### Preserved exact received-document transitions

```text
PendingReview  → Accepted, Rejected, Duplicate, Quarantined
Quarantined    → PendingReview or Rejected after safety issue resolution
Accepted       → Superseded by explicit replacement, Rejected by explicit correction/invalidation, or Quarantined by explicit safety hold
Rejected       → terminal
Duplicate      → terminal
Superseded     → terminal
```

Accepted-document correction remains atomic:

- record actor/reason/source/time/correlation history;
- deactivate every active evidence link for that artifact with evidence history;
- recalculate every affected request item and request in the same transaction;
- a completed request is explicitly reopened/recalculated rather than silently left Complete;
- quarantined recovery returns through PendingReview and does not automatically reactivate old evidence links;
- no financial, WorkItem, or assignment workflow state changes automatically.

### Preserved Phase 0B database rules

- `DocumentRequest.WorkItemId`, template, request-item and evidence FKs are restrictive.
- Raw `ReceivedDocument` has no required WorkItem FK and zero evidence links is valid.
- `(ServiceId, TemplateKey, Version)` unique.
- `(DocumentRequirementTemplateId, RequirementKey)` unique.
- `(WorkItemId, Revision)` unique.
- `(DocumentRequestId, RequirementKey)` unique.
- `(DocumentRequestItemId, ReceivedDocumentId)` unique.
- One current request per WorkItem through a partial unique index over non-terminal request states.
- `IsDefault ⇒ IsActive`; one active/default template per Service through a partial unique index equivalent to `UNIQUE (ServiceId) WHERE IsActive = TRUE AND IsDefault = TRUE`.
- Request template Service must match authoritative `WorkItem → BillingRecord → Engagement → ServiceId` at creation/replacement.
- Replacement/duplicate artifact references are non-self-referential and acyclic; they never infer WorkItem ownership.
- Hash is an indexed detection aid, not a globally unique business identity.
- Aggregate state changes, evidence changes and histories are transactionally consistent with optimistic concurrency plus serializable/locking patterns where needed.
- History rows are append-only.
- Request cancellation never mutates the shared raw artifact; it affects only the cancelled request/items and its evidence memberships.

### Phase 1B persistence implementation record

- The Phase 1A entities are mapped through `AppDbContext` with PostgreSQL integer enum columns, the existing `Record.Version` optimistic-concurrency token, audit-field handling, and restrictive delete behavior for every document-collection foreign key.
- The selected template/service invariant is enforced by PostgreSQL `DEFERRABLE INITIALLY DEFERRED` constraint triggers on `DocumentRequests` and on updates to `DocumentRequirementTemplates.ServiceId`, `Engagement.ServiceId`, `BillingRecord.EngagementId`, and `WorkItem.BillingRecordId`. At transaction completion the triggers compare the authoritative `Engagement.ServiceId` with the selected template's `ServiceId`; request creation/replacement and relevant referenced-row changes are covered without trusting a caller-supplied service value.
- `DocumentRequirementTemplate.TemplateVersion` is the physical business-version property. This intentionally avoids conflating it with the inherited `Record.Version` concurrency token; the approved `(ServiceId, TemplateKey, Version)` rule means `TemplateVersion` in the CLR/SQL model.
- `SaveChangesAsync` now explicitly protects request identity/revision/supersession fields, request-item identity and frozen snapshots, raw received-document intake metadata and creation-time replacement lineage, evidence identity pairs, and batch membership identity pairs. It leaves only approved status/lifecycle fields mutable. A used template allows only `IsActive`/`IsDefault` lifecycle changes; its existing checklist definition cannot be rewritten or extended, and a used template item has no mutable definition fields, including `IsActive`.
- The database migration adds the approved composite uniques, enum/value checks, positive byte-length and non-blank text checks, partial unique indexes for one active/default template per service, one current request per WorkItem, one direct request successor, and one direct received-document replacement successor, plus non-self replacement/duplicate checks, SHA-256 detection indexing (not uniqueness), and explicit restrictive self/reference foreign keys. A deferred request-lineage trigger enforces same-WorkItem, next-revision replacement links; the strict `+1` revision rule makes request-supersession cycles impossible. A received-document relationship trigger permits `DuplicateOfReceivedDocumentId` only once during `PendingReview → Duplicate`, keeps duplicate artifacts terminal, prevents duplicate-of-duplicate links, and protects raw metadata/replacement lineage. A deferred recursive trigger enforces acyclic received-document replacement chains. Duplicate links remain many-to-one to canonical artifacts.
- The exact Phase 0B status values are persisted as integer enum columns; transition legality remains an explicit domain-service rule and is not broadened by this persistence-only phase. No request/item/artifact transition changes financial, WorkItem, or assignment state automatically.
- Raw `ReceivedDocument` rows remain valid without evidence and have no WorkItem, BillingRecord, DocumentRequest, or DocumentRequestItem foreign key. Evidence is the only classification link and is retained through its active/inactive lifecycle.
- The four new history entities are append-only through the existing `SaveChangesAsync` protection: ordinary modification is rejected and the existing record-deletion guard rejects deletion. PostgreSQL template/item guards provide the same used-template immutability boundary for direct database writes, including full immutability of used template items. Received-document duplicate status requires a canonical link, the canonical target cannot itself be a duplicate, duplicate classification is terminal after its one-time assignment, and inactive evidence requires a non-blank reason.
- The migration is additive and deliberately does not backfill legacy `DocumentRequested`/`DocumentReceived` workflow rows or create synthetic requests/artifacts.

### Phase 1C PostgreSQL persistence test record

- `tests/BillingControl.Tests/DocumentCollectionPersistenceTests.cs` reuses the existing `IntegrationTests` partial class, `PostgresFact`, `Connection`, `Db()`, and `Fresh()` helpers. It verifies migration application and schema shape, template/default/version/text rules, used-template and used-item immutability, template/service consistency across authoritative relationship changes, request/revision/current-lineage rules, frozen request-item snapshots, raw artifact metadata/hash/byte-length rules, duplicate classification, replacement/duplicate reference rules, evidence lifecycle/identity, all four append-only histories, and `Record.Version` concurrency for requests and received artifacts.
- GitHub Actions run `34662113750` for commit `fd49071c469e5a0221e9b84dd2481c16a32d5eb1` executed the full suite against PostgreSQL 17 and `billing_test`: 60 .NET tests passed, 0 failed, and 0 skipped. Local PostgreSQL tests remain skip-only when `BILLING_TEST_CONNECTION` is absent; no non-test database is used.
- Phase 1C intentionally does not implement or test future business-service orchestration: request cancellation, human completion, accepted-document correction with evidence recalculation, carry-forward/reuse workflows, and status-transition services remain deferred to their later implementation phases. Phase 2A is the separate template application/service unit; Phase 2B is the focused internal management UI; Phase 2C technical regression/review is complete, while manual UI acceptance remains pending.

### Phase 2A template application service record

- `src/BillingControl/Services/DocumentRequirementTemplateService.cs` adds staff-side application operations without changing the Phase 1 domain schema: first-version creation, next-version cloning, unused-version definition edits, item add/edit/activation/reordering, explicit template activation/deactivation, explicit default switching, and untracked template/version/item read models.
- The service does not accept a caller-supplied business `TemplateVersion`; a new `(ServiceId, TemplateKey)` starts at version `1`, while cloning allocates the previous maximum plus `1`. Clones preserve stable requirement keys and snapshots in new rows, leave historical versions unchanged, and start non-default.
- Version allocation, cloning and default switching run in PostgreSQL transactions. A `SELECT ... FOR UPDATE` on the authoritative `Service` row serializes same-service version/default operations; the existing unique indexes and deterministic handling of unique/serialization/concurrency conflicts remain the final safety net. Default switching clears the old default and persists that change before setting the target, so the partial unique index is never temporarily violated.
- The service rejects edits to any version already referenced by a `DocumentRequest`, rejects duplicate requirement keys, keeps existing item keys stable, provides no hard-delete path, requires active/default templates to have an active item, and refuses deactivation of the current default. No financial, WorkItem, WorkerAssignment, request, provider, or integration state is changed.
- `tests/BillingControl.Tests/DocumentRequirementTemplateServiceTests.cs` adds PostgreSQL-backed coverage for first version `1`, clone contents/immutability, unused/used edits, duplicate keys, default/activation rules, concurrent version allocation, read behavior, and the financial/workflow boundary. Phase 2A was approved/closed against `f9f81bb22ab78e5986a102f071b9f7f5d03f471a`; final GitHub Actions run `34663809281` passed. Phase 2C technical regression/review is recorded above; final manual user UI acceptance remains pending.

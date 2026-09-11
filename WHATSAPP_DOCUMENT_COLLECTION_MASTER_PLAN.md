# WhatsApp Document Collection & Follow-Up — Master Plan

> **Repository:** `ChunMeng0127/BILLING_MANAGEMENT`  
> **Planning baseline:** `6677680feb12141eefe97dd5d21b37fd3b6b4bf1`  
> **Primary implementation model:** Codex Luna Max only, split into small bounded execution phases  
> **Permanent document storage:** SharePoint  
> **Status:** Architecture / roadmap only — implement phase-by-phase, do not build everything in one Codex run.

---

## Current Project Status

**Current Phase:** Phase 0B — Core Data Model Freeze
**Status:** Complete — the Phase 0B core data-model specification and requested correction amendments are recorded and ready for review/approval. Phase 0C has not started.
**Last Reviewed:** 2026-09-12

### Completed

- Master roadmap and phased implementation plan drafted.
- Core business rules, architecture direction, integrations, testing expectations, and rollout sequence documented.
- AI/Codex working rules added so the repository and this plan remain the source of truth.
- Roadmap adjusted for Codex Luna Max only, with small reviewable execution units.
- Phase 0A repository inventory completed against the current `codex/accounting-mvp` implementation, including entities, workflow/statuses, authorization, database/migration conventions, integration/job abstractions, tests/CI, conflicts, constraints, and unresolved architecture questions.
- No production code, business logic, schema, migration, provider integration, or Phase 0B design was changed during Phase 0A.
- ChatGPT review approved Phase 0A against commit `de9ee10a0d884978dce79cb2e6ce0fffbd8e0869`; sampled repository claims matched the current source, and no blocking omission was found for this inventory phase.
- Phase 0B core data-model freeze completed: entities, cardinalities, lifecycle/status transitions, WorkItem linkage, invariants, concurrency/audit rules, cancellation/reopen/replacement behaviour, and historical workflow treatment are specified below.
- Phase 0B correction review completed: raw received artifacts may remain unclassified without a guessed WorkItem, and accepted-document correction/invalidation now has an explicit audited path with transactional evidence and request-state recalculation.
- No production code, migration, provider integration, PIC/contact model, SharePoint design, permission implementation, or Phase 0C work was started.

### Current Work

- Phase 0A is closed and approved.
- Phase 0B, including the requested correction amendments, is complete and awaiting review/approval; no Phase 0C work has started.

### Outstanding

- Phase 0C — PIC, WhatsApp conversation, SharePoint storage/metadata, and permission boundaries.
- Phase 0D — Follow-up/workflow rules, failure/retry/idempotency, audit, AI boundaries, and final architecture freeze.

### Next Step

Review and approve the completed **Phase 0B — Core Data Model Freeze**. Do not begin Phase 0C until this result is approved and this file is updated.

---

## 1. Objective

Extend Billing Control into a mature **WhatsApp-first document collection and follow-up system** for accounting work.

The system should:

- Request documents through WhatsApp rather than email.
- Prefer a WhatsApp group where the client, accounting firm, manager, and LCM MGT can monitor the same conversation.
- Keep Direct WhatsApp as a fallback.
- Support one PIC handling multiple companies without spamming them.
- Consolidate multiple company requests into one communication where authorised participants are the same.
- Make the requested work feel manageable by showing only the next small set of useful items, not a huge outstanding list.
- Receive client replies and documents from WhatsApp.
- Store permanent files in SharePoint.
- Track every request, item, message, received file, follow-up, snooze, and workflow transition in Billing Control.
- Stop or delay reminders when the client has promised a later date.
- Integrate with the existing Work Item / Worker Assignment / Workflow Status model.
- Use AI later for classification, promise-date detection, and message wording, while deterministic business rules remain authoritative.

---

## 2. Core Operational Flow

```text
Billing Record
    ↓
Work Item
    ↓
Document Request generated from service template
    ↓
Document Request Batch groups requests for the same PIC/conversation
    ↓
Worker reviews preview
    ↓
WhatsApp Group / Direct message sent
    ↓
Client sends text / PDF / image / document in WhatsApp
    ↓
Billing Control receives webhook
    ↓
File validated and uploaded to SharePoint
    ↓
Worker/AI classifies file against Company + Period + Document Item
    ↓
Document Request Item status updated
    ↓
Follow-up engine asks only for the next outstanding items
    ↓
All mandatory documents confirmed
    ↓
Worker confirms complete
    ↓
Workflow → Document Received
```

---

## 3. Non-Negotiable Design Principles

1. **WhatsApp-first. No email workflow is required.**
2. **Group conversation preferred; Direct conversation remains supported.**
3. **One company does not automatically equal one WhatsApp message.**
4. A PIC may represent several companies.
5. Requests may be consolidated only when the **same PIC + same WhatsApp conversation + same authorised participants** apply.
6. Never mix unrelated companies or companies with different authorised participants in one group/message.
7. Follow-up frequency is controlled at the **PIC/conversation/batch level**, not independently by every Document Request.
8. Automated follow-ups should normally show only **3–5 next-action items**, even if many more are outstanding internally.
9. Client can send documents gradually.
10. Promised dates / snooze must suppress reminders until the agreed time.
11. A received attachment is **not automatically an accepted document**.
12. SharePoint is the authoritative permanent file repository.
13. Billing Control stores workflow state, metadata, audit records, and SharePoint references.
14. AI may suggest; deterministic database rules decide what is outstanding.
15. AI should not silently change operational records during initial rollout.
16. Every inbound/outbound WhatsApp action must be auditable.
17. Worker/staff must always be able to pause, snooze, resume, or override automation.
18. Do not auto-drive financial status from document collection unless separately approved.

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

Document collection should sit primarily under the **Work Item**, because documents belong to the job rather than to one individual worker.

Recommended relationship:

```text
WorkItem
├── WorkerAssignment A
├── WorkerAssignment B
└── DocumentRequest
    ├── DocumentRequestItem
    ├── WhatsApp communication links
    └── ReceivedDocument
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

Recommended integration:

- Successful first request send may move the relevant workflow to **Document Requested**.
- When all mandatory items are confirmed, show **Ready for Document Received**.
- Initially require a human **Confirm documents complete** action before moving to **Document Received**.

---

## 5. Proposed Core Data Model

This section preserves the original mature-direction planning input. The Phase 0B specification below is now authoritative for the core entities, cardinalities, statuses, invariants, and WorkItem linkage. The contact/PIC, WhatsApp, and follow-up concepts remain later-phase roadmap guidance.

For the core model, the Phase 0B freeze supersedes the earlier direct-link suggestions: `ReceivedDocument` is a raw intake artifact with no direct `WorkItemId`, `BillingRecordId`, request, or request-item foreign key; a classified artifact reaches a WorkItem only through explicit `DocumentRequestItemEvidence` links. The existing WorkItem relationship still provides the authoritative path to `BillingRecord`.

### Document requirement templates

- `DocumentRequirementTemplate`
- `DocumentRequirementTemplateItem`

Template items should support:

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

Suggested item statuses:

```text
Missing
Requested
PartiallyReceived
Received
NotRequired
Waived
```

Suggested request-level states may include:

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

### Contact / PIC model

A proper contact layer is required because one PIC may handle many companies.

Suggested fields/concepts:

```text
Name
WhatsAppNumber
PreferredLanguage
WhatsAppOptIn
OptInDate
OptInSource
DoNotWhatsApp
PreferredFollowUpDays
PreferredFollowUpTime
Paused
```

A Contact/PIC must be linkable to multiple customers/engagements.

### WhatsApp model

- `WhatsAppConversation`
- `WhatsAppParticipant`
- `WhatsAppMessage`

Conversation type:

```text
Group
Direct
```

Track at least:

```text
Provider Conversation / Group ID
Participants
Authorised customer/engagement scope
Active / Inactive
Message direction
Provider message ID
Template name/version where applicable
SentAt
DeliveredAt
ReadAt
FailedAt
Failure reason
Exact message body / snapshot
```

### Received documents

- `ReceivedDocument`

Store metadata such as:

```text
Sender
ReceivedAt
OriginalFilename
MIME type
File size
SHA-256 hash
Classification status
SharePoint DriveId
SharePoint ItemId
SharePoint WebUrl
SharePoint ETag
```

`WorkItemId`, `BillingRecordId`, `DocumentRequestId`, and `DocumentRequestItemId` were early conceptual suggestions and are not direct Phase 0B columns on the raw received artifact. An unclassified artifact may have zero evidence links; after explicit classification, evidence links reach the relevant request item and therefore its WorkItem. SharePoint fields remain Phase 0C storage metadata.

Do not store permanent document binaries in PostgreSQL.

### Follow-up state

- `DocumentFollowUpState` or equivalent

Support:

```text
NextFollowUpAt
Paused
SnoozedUntil
PromisedDate
Reason
LastAutomatedFollowUpAt
FollowUpStage
StopAutomation
```

---

## 6. Multiple Companies per PIC — Anti-Spam Design

This is a major requirement.

Example:

```text
Mr Tan
├── ABC Sdn Bhd
├── XYZ Sdn Bhd
├── DEF Sdn Bhd
└── GHI Sdn Bhd
```

Do not send four independent reminders when they can be safely consolidated.

Instead:

```text
PIC
  ↓
WhatsApp Conversation
  ↓
Document Request Batch
  ├── ABC Request
  ├── XYZ Request
  ├── DEF Request
  └── GHI Request
```

Eligibility to consolidate:

```text
Same PIC
+
Same WhatsApp conversation
+
Same authorised participants
=
Can consolidate
```

If the participants differ, the requests must remain separate for confidentiality.

### Client-facing workload rule

Billing Control may know there are 17 outstanding items, but an automated message should usually show only the **next 3–5 useful items**.

Example:

```text
Hi Mr Tan, to keep things moving, could you send these next?

1. ABC — Maybank statement
2. XYZ — CIMB statement
3. DEF — Payroll

You can send the remaining documents later.
```

Internal system still tracks the complete outstanding list.

### Progress-oriented communication

Prefer:

```text
ABC  5/6 received
XYZ  Complete
DEF  3/5 received
```

rather than repeatedly presenting a large missing-document count.

---

## 7. Follow-Up Engine Rules

Do not create a separate uncontrolled reminder timer for every Document Request.

The scheduler should mainly evaluate:

```text
PIC
+
Conversation
+
Request Batch
```

Initial suggested cadence, configurable later:

```text
Initial request          Day 0
Follow-up 1              +3 business days
Follow-up 2              +4 business days
Escalation               +5 business days
```

Recommended global anti-spam rule:

> Normally no more than one automated document reminder to the same PIC/conversation within the configured cooling-off period (initially around 2–3 business days).

Actions required:

```text
Follow up now
Snooze
Pause
Resume
Stop automation
```

### Promised dates

If a client says:

> I will send on Friday.

The system should support:

```text
Promised date: Friday
Scope: selected request / current batch / all relevant requests for this PIC
```

No automated reminder should be sent before the promised date where the scope applies.

AI may later detect promised dates, but a human should initially confirm them.

---

## 8. WhatsApp Conversation Strategy

### Preferred: Group

Typical participants:

```text
Client contact(s)
Accounting Firm / X Group
Manager / Signitive
LCM MGT WhatsApp business number
Optional authorised internal participant
```

Benefits:

- Firm and manager can monitor the same request/follow-up history.
- Delays and client responses are visible to authorised parties.
- Less dependence on private staff phones.

### Fallback: Direct

The system must retain Direct conversation support in case:

- Group API is unavailable for the account/customer.
- Group eligibility changes.
- Customer prefers direct communication.
- Participant confidentiality makes a group inappropriate.

### Important implementation prerequisite

Before WhatsApp Group development begins, verify current Meta WhatsApp Business Platform / Groups API availability and eligibility for the actual business account. Do not assume current API limits or eligibility from an old specification.

---

## 9. SharePoint Storage Architecture

SharePoint is the **permanent authoritative document store**.

Flow:

```text
WhatsApp media
    ↓
Meta API temporary download
    ↓
Validation / safe temporary processing
    ↓
SharePoint upload
    ↓
ReceivedDocument metadata saved in Billing Control
    ↓
Temporary local file removed
```

Do not permanently store client documents in:

- PostgreSQL `byte[]`
- normal web root
- permanent VPS folders

### Storage abstraction

Use an interface such as:

```text
IDocumentStorage
```

with:

```text
SharePointDocumentStorage
```

Controllers/services should not directly contain Microsoft Graph implementation details.

### Suggested SharePoint organisation

Example only; final folder/metadata design is Phase 0 work:

```text
Client Documents
├── ABC001 - ABC SDN BHD
│   └── 2026
│       ├── MA-2026-08
│       └── MA-2026-09
└── XYZ002 - XYZ SDN BHD
```

Do not rely only on folders. Use SharePoint metadata where practical.

Useful metadata:

```text
CustomerId
WorkItemId
BillingRecordId
DocumentRequestId
DocumentRequestItemId
PeriodStart
PeriodEnd
Service
Source = WhatsApp
Sender
ReceivedAt
OriginalFilename
ClassificationStatus
```

These are post-classification storage metadata suggestions. Raw intake may omit `WorkItemId`, `BillingRecordId`, and request identifiers until an explicit evidence link is confirmed; the storage/provider mapping remains Phase 0C.

### SharePoint security direction

Prefer least-privilege app-only access, ideally restricted to the specific SharePoint site/library rather than broad tenant-wide file access.

Before implementation, verify the tenant/admin can grant the required Microsoft Graph / SharePoint permissions.

---

## 10. File Safety and Reliability Requirements

Incoming media handling should include:

- File-size limits
- Allowed MIME types
- File-name sanitisation
- SHA-256 hash
- Duplicate detection
- Retry-safe uploads
- SharePoint upload failure state
- Malware/antivirus scanning strategy
- Audit record of original sender and receipt time
- Never expose SharePoint app credentials to the browser

Webhook processing should include:

- Signature/authenticity verification
- Idempotency
- Provider message-ID deduplication
- Retry handling
- Dead-letter / failed-event visibility
- Structured logging

---

## 11. Human Classification Before AI

Before adding AI, make manual classification fast and reliable.

The intake step first records a `ReceivedDocument` artifact without a guessed WorkItem or request-item link. The worker/AI classification confirmation then creates the explicit evidence link to the selected `DocumentRequestItem`; until that happens, the artifact remains unclassified and does not satisfy any request item.

Example:

```text
New WhatsApp Document

Maybank_Aug.pdf
From: Mr Tan

Company:   [ABC SDN BHD]
Type:      [Bank Statement]
Period:    [Aug 2026]

[Confirm]
```

After confirmation:

```text
DocumentRequestItem: Bank Statement
→ Received
```

This is the deterministic baseline against which later AI features are measured.

---

## 12. Mature AI Strategy

### AI document classification

AI may suggest:

```text
Company: ABC SDN BHD
Document: Maybank Bank Statement
Period: Aug 2026
Confidence: 96%
```

Initial rollout:

```text
[Confirm]
[Correct]
```

Do not silently auto-accept low/unknown confidence results.

### AI conversation understanding

AI may detect:

```text
"I'll send tomorrow"
→ Suggested promised date

"Payroll not applicable this month"
→ Suggested Not Required
```

Human confirms before operational state changes during initial rollout.

### AI message composition

Deterministic system provides facts:

```text
PIC
Companies
Outstanding next-action items
Last contact
Promised date
Follow-up stage
```

AI may write natural wording.

Principle:

> **Database decides WHAT. AI decides HOW TO SAY IT.**

---

## 13. Mature Document Collection Dashboard

Target operational dashboard:

```text
DOCUMENT COLLECTION

Awaiting documents          24
Partially received          11
Overdue                      7
Snoozed                      4
Complete this week          18
Needs classification         6
WhatsApp failures            1
```

PIC-centric view example:

```text
Mr Tan
4 companies
72% collected
Next follow-up: 15 Sep 2026

ABC       5/6
XYZ       COMPLETE
DEF       3/5
GHI       2/4

Next requested:
• ABC — CIMB statement
• DEF — Payroll
• GHI — Maybank statement

[Send now]
[Snooze PIC]
[Pause automation]
[View all outstanding]
```

Useful filters:

```text
Worker
Manager
Firm
Customer
PIC
Service
Period
Status
Overdue
Snoozed
```

---

## 14. Phased Codex Luna Max Implementation Roadmap

Use **Codex Luna Max only** for this project.

The numbered phases below are project milestones. A milestone does **not** automatically equal one Codex run.

### Luna Max execution-unit rule

Before giving work to Codex:

1. Each execution task should have **one primary objective** and a clearly reviewable deliverable.
2. Prefer small sub-phases that can be understood and reviewed without mixing several architectural concerns.
3. If a milestone touches multiple major concerns, requires broad repository changes, or looks too large/risky for one focused Luna Max run, split it into `A/B/C` sub-phases first.
4. Do not ask Luna Max to design, implement, migrate, integrate external providers, harden security, and build UI in the same execution task.
5. After every sub-phase, ChatGPT reviews the actual diff/tests and updates this master plan before the next sub-phase starts.
6. A sub-phase may be split again if repository inspection shows it is still too broad. Correctness and reviewability take priority over keeping the phase count small.

### Phase 0 — Architecture Freeze

Phase 0 is intentionally split into four small non-coding review units.

#### Phase 0A — Repository & Architecture Inventory

**No production coding.**

Inspect the current repository and document only the existing facts relevant to this feature:

- Existing entities and relationships
- Current Work Item / Worker Assignment / workflow implementation
- Existing status enums/state transitions
- Authorization/role model
- Existing database conventions and migration patterns
- Existing background-job/integration abstractions, if any
- Existing tests and CI relevant to this feature
- Constraints/conflicts between the repository and this master plan

Deliverable: update this file with confirmed repository facts and unresolved architecture questions only.

Do not design all final entities yet and do not implement code.

### Phase 0A — Confirmed Repository Findings (2026-09-12)

This is an inventory, not the Phase 0B data-model freeze. The mature direction in Sections 4–10 remains roadmap guidance; no proposed entity, provider, storage, or workflow design below is approved for implementation by this Phase 0A review.

That statement records the position at Phase 0A close. The Phase 0B specification below now freezes the deterministic core model only; it does not approve the later provider, contact, storage, follow-up, or permission designs retained in Sections 4–10.

#### Application and runtime architecture

- The repository is an ASP.NET Core 10 MVC/Razor application using Entity Framework Core 10, ASP.NET Core Identity, PostgreSQL 17, and locally bundled Bootstrap. The README describes it as an internal billing/work MVP for fewer than 10 users and states that there are no external accounting integrations (`README.md:1-3`).
- `Program.cs` configures one web application, one `AppDbContext`, Identity, authorization, the existing scoped domain services, `TimeProvider`/`BusinessClock`, and rate limiting. There is no `IHostedService`, `BackgroundService`, scheduler, queue consumer, or separate worker process (`src/BillingControl/Program.cs:13-78`).
- The production topology is PostgreSQL, a one-shot migration container, one long-running application container, and a reverse proxy (`compose.yaml`, `docker-compose.yaml`). Persistent volumes cover PostgreSQL and data-protection keys; there is no application media/file-storage volume. The Hostinger-oriented `docker-compose.yaml` sources `master`, so `codex/accounting-mvp` is not its automatic deployment source.
- The request/response boundary is MVC controller → scoped domain service → EF Core/PostgreSQL. `AppController` converts business, database, and serialization errors into a redirect with a user-facing message (`src/BillingControl/Controllers/AppController.cs:9-44`).
- The project has no WhatsApp SDK/provider package, typed external client, SharePoint/Graph SDK, object-storage adapter, queue package, malware scanner, OCR library, or document-management package (`src/BillingControl/BillingControl.csproj`).

#### Existing entities and relationships

The current graph is financial/work-oriented:

```text
Customer / Service / BusinessParty / Manager
                         ↓
                    Engagement
                         ↓
                   BillingRecord
                         ↓ one-to-one
                      WorkItem
                         ↓ one-to-many
                  WorkerAssignment → Worker
```

- `Engagement` links one customer, service, accounting firm (`BusinessParty`), and manager, with service dates, billing amount, revenue-share percentages, status, and one `BillingSchedule` (`src/BillingControl/Models/Domain.cs:63-89`).
- `BillingRecord` belongs to an engagement, stores the service period, billing status, customer/service snapshots, customer amount, revenue-share base snapshot, and share allocations. Each billing record has exactly one `WorkItem` and may have invoice lines (`Domain.cs:91-110`, `src/BillingControl/Data/AppDbContext.cs:64-88`).
- `WorkItem` stores `WorkStatus`, notes, and worker assignments. Billing generation creates the billing record and its work item together (`src/BillingControl/Services/BillingService.cs:13-60`).
- `WorkerAssignment` belongs to a work item and worker. It stores worker-name, percentage, LCM-gross snapshot, entitlement, cancellation/visibility state, current workflow state/version/progress, and links to worker payments, weekly reports, progress-update history, and workflow history (`Domain.cs:131-154`).
- `WeeklyProgressReport` is unique per assignment/week and stores the latest weekly summary and workflow snapshot. `WeeklyProgressUpdateHistory` is an append-only timeline linked to both the report and assignment. `WorkerAssignmentWorkflowHistory` records assignment-level workflow/visibility transitions (`Domain.cs:156-198`, `AppDbContext.cs:95-120`).
- `Invoice`, `InvoiceLine`, `CustomerReceipt`, and `CustomerReceiptAllocation` are existing financial documents and allocations. They are not uploaded source documents and have no file, media, message, or SharePoint metadata (`Domain.cs:220-265`).
- There is no entity/table for a document request, requirement template item, received document, attachment/media, PIC/contact, WhatsApp conversation/participant/message, provider event, webhook delivery, consent, or file-retention state. The only contact-like fields are optional email fields on `Customer` and `Worker`; no WhatsApp number exists (`Domain.cs:26-37`).

The repository therefore confirms the existing relationship path relevant to the plan as:

`Engagement → BillingRecord → WorkItem → WorkerAssignment → Worker`

The repository does not yet establish the exact cardinality or attachment level for a future `DocumentRequest`/`DocumentRequestItem`/`ReceivedDocument` structure.

#### Work Item, Worker Assignment, Billing Record, and workflow behaviour

- `WorkItem.Status` is only `Upcoming`, `InProgress`, or `Completed`; `WorkItem.Notes` is free text. Only Admin/InternalUser may edit shared work status/notes. Workers submit weekly progress instead (`src/BillingControl/Models/Domain.cs:38-42`, `src/BillingControl/Controllers/WorkController.cs:45-60`).
- Assignment is separate from billing generation. Multiple active assignments are allowed, combined percentages/rounded entitlements are capped against the immutable LCM gross share, and 0% assignments remain active operational assignments with RM0.00 entitlement (`BillingService.cs:141-158`).
- Assignment cancellation is a business-state operation, not row deletion; active worker payments must be cancelled first. Assignment edits use the `Version` token, and changing the worker after progress exists is blocked (`BillingService.cs:160-190`, `:235-247`).
- The existing detailed workflow has eleven stages: `DocumentRequested`, `DocumentReceived`, `AssignedNotStarted`, `AssignmentStarted`, `StartPreparing`, `QueriesSent`, `DraftManagementReportSent`, `PendingReview`, `AmendmentRevision`, `FinalManagementReportSent`, and `Completed` (`Domain.cs:44-56`). The two report-related stages require positive versions.
- Assignment workflow is deliberately separate from `WorkItem.Status`, `BillingStatus`, invoicing, and payments. `AssignmentWorkflowService` records current state plus append-only transition history and explicitly does not change financial/work-item state (`src/BillingControl/Services/AssignmentWorkflowService.cs:7-15`, `:120-170`).
- Weekly reporting uses business-timezone Monday–Sunday windows. Missing is calculated when no report exists; submitted/late classification is based on the first update timestamp. Current-week worker edits update assignment state and append history; historical reports preserve historical snapshots (`src/BillingControl/Services/ProgressReportService.cs:1-113`, `README.md:87-93`).
- `DocumentRequested` and `DocumentReceived` are currently workflow labels/snapshots, not document-level evidence. They do not identify a requested item, sender, message, file, acceptance decision, or completeness of a request batch. Any integration with the plan’s “Ready for Document Received” and human confirmation rule must be specified in Phase 0B/0D before implementation.
- Billing generation and corrections are serializable and dependency-locked. Billing statuses derived from active invoices/receipts cannot be safely treated as document-collection statuses; the plan’s non-negotiable rule not to auto-drive financial status is consistent with the repository constraints (`BillingService.cs:13-139`, `README.md:72-84`).

#### Authorization and role handling

- The five Identity roles are `Admin`, `InternalUser`, `AccountingFirm`, `Manager`, and `Worker`. Admin/InternalUser are staff. External roles link to exactly one `BusinessParty`, `Manager`, or `Worker` respectively (`src/BillingControl/Services/AccessScope.cs:9-47`).
- The fallback authorization policy requires authentication and a valid access profile for every endpoint except explicitly anonymous login/denied actions. The custom handler verifies one role, an active user, and role/link consistency against the database (`src/BillingControl/Program.cs:23-32`, `AccessScope.cs:49-60`). Security-stamp validation is immediate.
- `AccessScope` is the server-side scope boundary for engagements, billing, schedules, work items, assignments, progress reports, payments, masters, invoices, and reports. `CreatedBy` is audit metadata, not ownership (`AccessScope.cs:63-202`).
- Staff can edit shared work and assignments; workers can access their own active, visible assignments and weekly updates; managers have scoped read-only progress access; AccountingFirm has no work/progress route. Hidden assignments are not exposed to the linked worker, including through forged URLs (`src/BillingControl/Controllers/WorkController.cs:10-103`, `ProgressController.cs:9-164`, `AccessScope.cs:127-148`).
- User administration is Admin-only. Role/entity-link changes update the security stamp and revoke existing sessions. A database check constraint prevents more than one external entity link (`src/BillingControl/Controllers/UsersController.cs:12-75`, `src/BillingControl/Data/AppDbContext.cs:37-40`).
- No role represents a customer/end-customer user or an unauthenticated external WhatsApp sender. A phone-number match must not be treated as authorization without an explicit identity, consent, sender-matching, and permitted-scope rule.

#### Database and migration conventions

- `AppDbContext` derives from `IdentityDbContext<AppUser>`. Record-derived entities use integer keys, `long Version` concurrency tokens, UTC audit fields/actors, PostgreSQL numeric precision, and string-length conventions (`src/BillingControl/Data/AppDbContext.cs:8-49`).
- Record foreign keys default to `DeleteBehavior.Restrict`. Ordinary record deletion is rejected by `SaveChangesAsync`; only narrowly scoped invoice-line, billing-snapshot, and receipt-allocation corrections are permitted. Modified-field allowlists protect historical snapshots and audit origins (`AppDbContext.cs:129-189`).
- Explicit check constraints and indexes enforce positive money, percentage/date validity, workflow versions, one report per assignment/week, request-id idempotency for existing payment/receipt flows, and allocation uniqueness (`AppDbContext.cs:50-128`).
- Migrations are timestamp-prefixed EF Core migrations with generated designer files and `AppDbContextModelSnapshot.cs`. The latest committed migration is `20260910052737_WeeklyProgressUpdateHistory`; schema changes run through the explicit `--migrate` path, while normal startup does not alter schema (`src/BillingControl/Data/Migrations`, `Program.cs:71-76`).
- Existing migration precedent includes explicit SQL backfills, safety checks, restrictive foreign keys, and additive workflow/history changes. `InvoiceDocuments` migrated legacy invoice/receipt fields into normalized rows and refused unsafe downgrade after richer document data existed (`20260907101738_InvoiceDocuments.cs:160-201`, `:229-275`).
- Integration tests refuse a database name that does not end in `_test`; `Fresh()` drops/recreates only that database and applies committed migrations (`tests/BillingControl.Tests/IntegrationTests.cs:14-27`).

#### Background jobs, integrations, tests, and CI

- No WhatsApp/webhook/provider integration, provider signature verifier, message inbox/outbox, media-download service, SharePoint adapter, file scanner, durable queue, retry policy, hosted worker, or outbox dispatcher exists in the current repository.
- The existing tests use xUnit, `WebApplicationFactory<Program>`, real PostgreSQL integration tests, a fixed `TimeProvider` for workflow windows, and dependency-free Node tests. Current coverage includes financial allocation/concurrency, workflow/history, authentication/CSRF, all five roles, scope/tampering, reporting/export scope, redaction, and session revocation. It does not cover WhatsApp, SharePoint, attachments, media retrieval, file access, retention, webhook signatures, provider retries, or external failure states.
- CI runs JavaScript tests, .NET restore/build/test, PostgreSQL 17 integration tests, Docker image build, and a production Compose HTTPS smoke test with .NET 10 and Node 22 (`.github/workflows/ci.yml`). CI has no provider credentials or external WhatsApp/SharePoint environment.
- Phase 0A checks completed on the reconciled repository baseline: `dotnet build -c Release` passed with 0 warnings/errors; EF reported no pending model changes; JavaScript tests passed 12/12; the isolated PostgreSQL suite passed 51/51 with 0 failures and 0 skips; staged-diff whitespace checks passed before commit.

### Phase 0A — Confirmed Conflicts, Risks, and Constraints

The following conflicts were identified at Phase 0A close. Phase 0B resolves the core aggregate, status, linkage, invariant, and historical-record questions in the specification below; the external-boundary risks remain deferred.

1. **Document workflow is not document tracking.** Existing `DocumentRequested`/`DocumentReceived` states are assignment workflow labels and weekly snapshots. They cannot identify a checklist item, message, file, sender, acceptance decision, or completeness.
2. **The exact WorkItem linkage was a Phase 0B decision.** At Phase 0A close, the plan recommended collection primarily under `WorkItem`, while the current repository had one work item per billing record and multiple assignments. Phase 0B now fixes requests and received documents to one WorkItem; cross-company/period consolidation and confidentiality remain Phase 0C questions.
3. **PIC and external identity are missing.** The plan requires one PIC across multiple companies and group/direct conversation scope, but the repository has no contact, WhatsApp number, consent, participant, or customer-user model. Existing worker-based scope cannot be reused automatically for client messages.
4. **Role boundaries may need extension.** AccountingFirm is excluded from work/progress routes, customers have no login role, and an inbound provider event is not an Identity principal. Phase 0C must define who may see, link, classify, accept, reject, download, re-request, or override incoming material.
5. **No permanent media path exists in the application.** The plan’s SharePoint direction is not implemented. PostgreSQL stores metadata/financial data only; production Compose has no media volume. Temporary media handling, SharePoint upload failure state, hash/deduplication, malware scanning, retention, and backup/restore guarantees remain unimplemented.
6. **No asynchronous or durable integration boundary exists.** WhatsApp media retrieval, SharePoint upload, outbound reminders, retries, rate limits, dead-letter/manual retry, and webhook replay cannot be expressed safely by the current synchronous MVC services alone.
7. **Provider event idempotency and audit identity are absent.** Existing `RequestId` uniqueness covers customer receipts and worker payments, not provider message/event IDs. `SaveChangesAsync` derives the actor from an authenticated HTTP claim and otherwise uses `system`; provider-event identity and exact inbound/outbound snapshots need explicit design.
8. **Workflow/status layering is a safety risk.** `WorkItem.Status`, `BillingStatus`, assignment workflow, weekly report status, request-item status, and request-level status must remain distinct. Collection automation must not silently complete/reopen work or change financial status.
9. **Migration and deployment constraints apply.** Future schema changes must follow the repository’s additive/backfill/restrictive-FK conventions, use the isolated `_test` database rule, and account for the Hostinger importer sourcing `master`. No migration or deployment was performed in Phase 0A.
10. **The remote plan’s proposed data model was unapproved at Phase 0A close.** Sections 5–10 still describe the mature target direction, including SharePoint and WhatsApp concepts. Phase 0B now approves only the deterministic core specification below; provider, storage, contact, permission, and workflow-automation boundaries remain later Phase 0 work.

### Phase 0A — Unresolved Questions at Inventory Close

These questions were intentionally not answered by the inventory. The five Phase 0B gate questions are retained as project history and are resolved by the Phase 0B specification immediately below; the Phase 0C/0D questions remain intentionally open.

#### Phase 0B gate questions

- Confirm the exact approved aggregate and cardinalities: does a `DocumentRequest` belong to a `WorkItem`, and can one request/batch span multiple billing records, companies, engagements, or periods?
- Confirm the exact distinction and transitions among request-level state, request-item state, received-document state, existing assignment workflow, and the human “Confirm documents complete” action.
- Define mandatory/optional/waived/not-required semantics, partial receipt, replacement/version handling, duplicate handling, cancellation, and what can be reopened.
- Define database invariants, uniqueness, concurrency/version behaviour, audit fields, and migration/backfill rules for the new deterministic collection records without changing financial snapshots.
- Decide whether any existing historical `DocumentRequested`/`DocumentReceived` workflow rows are merely historical labels or require a non-destructive link to future collection records.

#### Questions reserved for Phase 0C/0D

- Which PIC/contact, customer, firm, manager, worker, and provider identities are authorised for each conversation, request batch, file, message, and classification action?
- Which WhatsApp provider/API mode, group eligibility, webhook signature/replay rules, consent/opt-out rules, template rules, and service-window constraints apply to the real business account?
- What SharePoint site/library/app-only permissions, folder/metadata conventions, temporary-file limits, malware scanning, retention, and restore guarantees are available?
- Is a durable queue/hosted worker/outbox required, and what retry, backoff, dead-letter, rate-limit, manual-replay, and failure-state behaviour is required?
- What exact audit trail is required for provider events, human overrides, automated follow-ups, promise dates, snoozes, classification, file access, and SharePoint failures?
- Which deterministic provider fakes/contract fixtures, signed-webhook tests, media/storage tests, authorization tests, concurrency/idempotency tests, and CI smoke checks are acceptance gates?

#### Phase 0B — Core Data Model Freeze

**No production coding.**

Using the confirmed Phase 0A repository facts, finalise only:

- Core document collection entities
- Exact relationships/cardinalities
- Statuses and allowed transitions
- Required database invariants/uniqueness rules
- WorkItem/BillingRecord linkage

Deliverable: exact reviewed data-model specification in this file.

Do not cover WhatsApp/SharePoint provider implementation in this sub-phase.

### Phase 0B — Final Core Data Model Specification (2026-09-12)

**Phase 0B status:** Core model frozen for review. This is an architecture specification only; it introduces no production entities, migration, controller, provider, storage, or business-logic change. Phase 0C remains not started.

The model below freezes the deterministic document-collection core. It intentionally leaves PIC/contact identity, WhatsApp conversation/provider details, SharePoint storage details, follow-up scheduling, permissions, and AI behaviour to Phase 0C/0D.

#### Core entities and cardinalities

1. **`DocumentRequirementTemplate`** — a versioned checklist definition for one `Service`.
   - `Service` 1 → many templates; a template belongs to exactly one service.
   - A template has a stable template key, positive version, name/description, and active/inactive state. A version becomes immutable once used to create a request.
   - At most one active template version is selected as the default for a service. Historical inactive versions remain addressable by existing requests.

2. **`DocumentRequirementTemplateItem`** — one checklist requirement within a template version.
   - One template 1 → many template items; every item belongs to exactly one template.
   - Each item has a stable `RequirementKey` within the template lineage, display name/description, required/optional flag, priority/wave, and display order.
   - `(DocumentRequirementTemplateId, RequirementKey)` is unique. Template items are immutable after their template is used; changing a checklist creates a new template version.

3. **`DocumentRequest`** — the current or historical collection aggregate for one job.
   - One `WorkItem` 1 → many document requests over time. Every request belongs to exactly one `WorkItem` and exactly one selected template version.
   - A request is job-level, not worker-level: it does not belong directly to a `WorkerAssignment`, because one work item can have multiple assignments and responsibility may change.
   - A request has a positive `Revision` unique within the work item, current request-level status, the selected template reference, immutable request/item snapshots, and an optional self-reference to the immediately superseded request revision.
   - At most one request for a work item may be current at a time. A current request includes `Draft`, `ReadyToSend`, `Requested`, `PartiallyReceived`, `Complete`, or `Paused`; `Cancelled` and `Superseded` are historical terminal states.
   - A request may be grouped into zero or more provider-neutral `DocumentRequestBatch` memberships over time. Batch membership never owns request state.

4. **`DocumentRequestItem`** — the frozen requirement instance inside one request.
   - One request 1 → many request items; every item belongs to exactly one request.
   - Each item references the source template item where available and snapshots the requirement key, name/description, required/optional flag, priority/wave, and display order. Later template edits cannot change an existing request.
   - `(DocumentRequestId, RequirementKey)` is unique. A request cannot contain the same requirement twice.
   - Request items do not belong directly to a billing record or worker assignment; their parent request reaches the billing record through `WorkItem`.

5. **`ReceivedDocument`** — one immutable received-artifact/intake record for one received document file or equivalent document artifact.
   - A raw received document has no direct `WorkItemId`, `BillingRecordId`, `DocumentRequestId`, or `DocumentRequestItemId`. It may be recorded before classification and may have zero evidence links while unclassified or unmatched.
   - A received document may be linked to zero or more request items through the evidence link below. A link reaches its request’s WorkItem and therefore the existing BillingRecord relationship. Links are created only by an explicit classification/reuse action; they are not inferred from a hash, sender, conversation, filename, or guessed company.
   - A single artifact may be explicitly linked to request items under more than one WorkItem only when that cross-work-item use is separately audited and later Phase 0C authorization/confidentiality rules permit it. No automatic evidence reuse occurs across companies or WorkItems.
   - Original receipt metadata (received timestamp, source actor/sender snapshot, original filename, MIME type, byte length, and SHA-256 where a file exists) is immutable. The lifecycle status and explicit evidence links are separately mutable/audited. Permanent storage references are metadata only and are finalized in Phase 0C; PostgreSQL does not store permanent document binaries.
   - Replacement is represented by a new received-document row pointing to the row it supersedes. The old row remains immutable history and is never overwritten or deleted.

6. **`DocumentRequestItemEvidence`** — explicit, auditable evidence linking.
   - One request item 0 → many evidence links; one received document 0 → many evidence links. Multiple links are permitted only by an explicit classification/reuse action. Cross-work-item links are allowed only as separately audited classification/reuse actions subject to later Phase 0C authorization/confidentiality rules.
   - `(DocumentRequestItemId, ReceivedDocumentId)` is unique. Unlinking is a status/audit operation, not row deletion.
   - The link has an active/inactive lifecycle. A new link is active; unlinking sets it inactive with an audit reason and timestamp. Inactive links remain queryable and do not satisfy an item.
   - Reactivating an inactive link requires a fresh explicit classification/reuse action and history entry; status changes or hash matches never reactivate it automatically.
   - An evidence link is satisfying only when the linked received document is `Accepted` and the link is active. A received file is not automatically accepted merely because it is present.
   - This link permits a valid document to be carried into a replacement request revision without mutating the original receipt record, while preventing implicit cross-company or cross-work-item reuse.

7. **`DocumentRequestBatch` and `DocumentRequestBatchMember`** — provider-neutral grouping scaffolding.
   - A batch has 1 → many membership rows; a request may have 0 → many memberships over its lifetime because an initial request and later follow-up may be separate batches.
   - `(DocumentRequestBatchId, DocumentRequestId)` is unique. Batch membership is not a request revision and does not change request/item completion.
   - Phase 0B freezes only this neutral grouping relationship. PIC identity, conversation identity, authorised participants, batch eligibility, batch statuses, and follow-up cadence are Phase 0C/0D decisions and must not be added to the core model from this phase.

8. **Append-only status histories** — `DocumentRequestStatusHistory`, `DocumentRequestItemStatusHistory`, `ReceivedDocumentStatusHistory`, and `DocumentRequestItemEvidenceHistory`.
   - Each status-history row belongs to exactly one current entity and records the previous status (nullable for the first row), new status, action/reason, actor/source, and UTC occurrence time.
   - Evidence-link history records link/unlink actions and the link identifier; it uses the same append-only/audit rules even though the link itself has an active/inactive lifecycle rather than a request-item status enum.
   - History rows are append-only, never deleted or edited, and are separate from the mutable current-status row. The existing `Record` audit fields remain on current and history entities.

There is deliberately no direct `WorkItemId`, `BillingRecordId`, `DocumentRequestId`, or `DocumentRequestItemId` on the raw `ReceivedDocument`. The authoritative linkage is:

```text
DocumentRequest → WorkItem → BillingRecord → Engagement / service period
DocumentRequestItem → DocumentRequest
ReceivedDocument → zero or more explicit DocumentRequestItemEvidence links → DocumentRequestItem → DocumentRequest → WorkItem → BillingRecord
```

This avoids a second billing foreign key that could disagree with the existing one-to-one `WorkItem`/`BillingRecord` relationship. It also keeps document collection independent of invoice, receipt, revenue-share, and payment tables.

#### Request-level statuses and allowed transitions

The request status enum is:

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

Meaning and transitions:

| Status | Meaning | Allowed next states |
| --- | --- | --- |
| `Draft` | Request and item snapshot exists but has not passed the send/readiness gate. | `ReadyToSend`, `Paused`, `Cancelled` |
| `ReadyToSend` | Snapshot is validated and can be issued by a later communication phase. | `Requested`, `Paused`, `Cancelled` |
| `Requested` | The request has been activated/issued; applicable outstanding items are requested. | `PartiallyReceived`, `Complete` by human confirmation, `Paused`, `Cancelled`, `Superseded` through replacement only |
| `PartiallyReceived` | At least one applicable item has qualifying evidence or has been partially acknowledged, but the mandatory set is not complete. | `Complete` by human confirmation, `Requested` after all qualifying evidence is withdrawn/rejected, `Paused`, `Cancelled`, `Superseded` through replacement only |
| `Complete` | Every required item is `Received`, `Waived`, or `NotRequired`, and a human has confirmed completion. Optional items do not block completion. | `PartiallyReceived`/`Requested` through explicit reopen, `Cancelled`, `Superseded` through replacement only |
| `Paused` | Collection progression is intentionally paused without losing item/evidence state. | `Draft`, `ReadyToSend`, `Requested`, `PartiallyReceived` after an explicit resume/recalculation, or `Cancelled` |
| `Cancelled` | Explicitly cancelled; historical and non-actionable. | None |
| `Superseded` | Replaced by a later request revision; historical and non-actionable. | None |

Rules:

- A request cannot jump directly to `Complete` from an unissued state. Completion requires an active request, qualifying item states, and an explicit human confirmation action.
- Request status is recalculated transactionally from item/evidence state and the transition action; callers cannot set an arbitrary status that violates the table.
- `Paused` retains the prior state in status history. Resume returns to `Draft`/`ReadyToSend` if it was never issued, otherwise to `Requested` or `PartiallyReceived` based on current item evidence.
- `Cancelled` is terminal. Re-requesting after cancellation creates a new revision; it never reopens or rewrites the cancelled row.
- `Superseded` is terminal. Creating a replacement revision changes the old current request to `Superseded` and creates the new request in the same transaction.
- Reopening a `Complete` request is an explicit audited action and does not reopen `WorkItem`, `WorkerAssignment`, or any financial status. It recalculates to `PartiallyReceived` or `Requested` after the affected item is reopened.

#### Request-item statuses and allowed transitions

The request-item status enum is:

```text
Missing
Requested
PartiallyReceived
Received
NotRequired
Waived
```

- `Missing` is the initial state for a draft/ready request or the state after an issued requirement has no qualifying evidence.
- `Requested` is set when the parent request is activated, except for items already `NotRequired` or `Waived`.
- `PartiallyReceived` means one or more active accepted evidence links exist but the worker has not confirmed the requirement complete.
- `Received` requires at least one active link to an `Accepted`, non-superseded received document and an explicit human confirmation that the requirement is complete. File arrival/classification alone cannot set this state.
- `NotRequired` means the requirement does not apply to this work item. It counts as satisfied but requires an auditable reason.
- `Waived` means the requirement applies but an authorised human has approved an exception. It counts as satisfied and requires an auditable reason and actor.

Allowed item transitions:

```text
Missing            → Requested, PartiallyReceived, NotRequired, Waived
Requested          → PartiallyReceived, Received, NotRequired, Waived
PartiallyReceived  → Received, Requested, NotRequired, Waived
Received           → PartiallyReceived, Requested, NotRequired, Waived by explicit correction/reopen only
NotRequired        → Missing or Requested by explicit reactivation only
Waived             → Missing or Requested by explicit reactivation only
```

Additional rules:

- A rejected, duplicate, quarantined, or superseded received document never satisfies an item. Removing or superseding the last qualifying evidence causes an explicit audited item reopen and request-state recalculation; it does not silently rewrite a completed request.
- `NotRequired` and `Waived` are reversible decisions, not deletes. Existing evidence remains historical and is not silently destroyed. Reactivation requires a reason and returns the item to `Requested` when the parent has been issued, otherwise `Missing`.
- Child item rows remain readable after parent cancellation/supersession but are frozen for ordinary changes. They do not need a separate `Cancelled` enum because the parent terminal state is authoritative.
- Optional items may remain `Missing`, `Requested`, or `PartiallyReceived` when the request reaches `Complete`; required items may reach completion only through `Received`, `NotRequired`, or `Waived`.

#### Received-document statuses and lifecycle

The received-document status enum is:

```text
PendingReview
Quarantined
Accepted
Rejected
Duplicate
Superseded
```

Allowed transitions:

```text
PendingReview  → Accepted, Rejected, Duplicate, Quarantined
Quarantined    → PendingReview or Rejected after the safety issue is resolved
Accepted       → Superseded through an explicit replacement action, Rejected through an explicit correction/invalidation action, or Quarantined through an explicit safety-hold action
Rejected       → terminal
Duplicate      → terminal
Superseded     → terminal
```

- `PendingReview` is the initial state for a received artifact that has been recorded but not accepted as evidence.
- `Quarantined` blocks classification/acceptance while a later safety/processing decision is pending. Provider and scanner implementation is outside Phase 0B.
- `Accepted` means the artifact is valid evidence for its active evidence links; it still does not by itself mark a request item `Received` because human completeness confirmation remains required.
- `Rejected` means the artifact does not satisfy any requirement; a new receipt is required. The rejection reason is mandatory. If the artifact was previously `Accepted`, the transition is a `CorrectAcceptedDocument`/invalidation action and the history must preserve the prior acceptance and correction actor/reason.
- `Duplicate` means the artifact is a duplicate of a canonical received-document row; `DuplicateOfReceivedDocumentId` is mandatory and the row never satisfies an item.
- `Superseded` means a later version replaces this artifact; `SupersedesReceivedDocumentId` on the new row and the old row’s status history preserve the chain. The original file metadata remains immutable.
- An `Accepted` artifact later found unsafe or requiring investigation may be moved to `Quarantined` through an explicit safety-hold action with a mandatory actor and reason. While quarantined, it cannot satisfy evidence links; it may return to `PendingReview` for a new review or move to terminal `Rejected`.
- A received-document row is never deleted or overwritten to represent a replacement. A new row is created and the old row is transitioned through the audited replacement action.

Accepted-document correction rules:

- A correction is never a silent metadata edit or deletion. The correction/safety-hold action records the previous status, new status, actor, source, UTC time, mandatory reason, and correlation identifier where available.
- In the same database transaction, every active evidence link for the corrected artifact is set inactive with an `AcceptedDocumentCorrection` or `AcceptedDocumentSafetyHold` link-history action. The links remain queryable; they no longer satisfy their request items, including when one artifact had explicit links across multiple WorkItems.
- The transaction recalculates every affected request-item status and every affected request-level status. A completed request is explicitly reopened/recalculated with history; it is never silently left `Complete` and no unrelated item is changed.
- If a quarantined artifact later passes a fresh review, it returns through `PendingReview`; existing corrected links are not automatically restored. A fresh explicit classification action must reactivate a retained inactive link or create the link where no prior link exists before the artifact can satisfy any item.
- Correction changes only document/evidence/request-collection state. It never changes `BillingRecord.Status`, invoice/receipt/payment state, revenue-share snapshots, `WorkItem.Status`, or worker-assignment workflow automatically.

#### WorkItem/BillingRecord linkage and financial isolation

- A document request is created only for one existing `WorkItem`. Because the repository has one `WorkItem` per `BillingRecord`, the billing record and service period are reached transitively and cannot drift from the work item.
- A request is not attached directly to `WorkerAssignment`; documents belong to the job and must remain available to authorised future/current assignments according to later permission rules.
- A request linked to a cancelled `WorkItem` or cancelled `BillingRecord` is not actionable. A raw received artifact may still be ingested without a WorkItem; linking it to a cancelled WorkItem is non-actionable and remains subject to later authorization rules. Existing collection history is retained; parent cancellation does not delete or fabricate child history.
- Document request/item/received-document state never changes `BillingRecord.Status`, invoice/receipt/payment state, revenue-share snapshots, `WorkItem.Status`, or assignment workflow automatically in Phase 0B. Any approved workflow mapping is a later Phase 0D rule.
- A request revision cannot alter the historical billing period, customer/service snapshot, revenue-share base, invoice allocation, worker entitlement, or payment data.

#### Replacement, version, partial receipt, duplicate, cancellation, and reopen rules

- **Template version:** editing a used template creates a new template version. Existing requests retain their original template and item snapshots.
- **Request revision:** a replacement is a new `DocumentRequest` row with the next work-item revision and `SupersedesRequestId`. The old request becomes `Superseded` in the same transaction. No request item is edited in place after activation.
- **Evidence carry-forward:** accepted evidence may be explicitly linked to a corresponding item in a replacement request through `DocumentRequestItemEvidence` when a human records the action. A normal replacement carries evidence within the same WorkItem; any cross-WorkItem reuse is a separate explicit classification action subject to later Phase 0C authorization/confidentiality rules. It is never copied or matched automatically.
- **Partial receipt:** multiple accepted evidence rows may link to one item. The item remains `PartiallyReceived` until a human confirms that the requirement is complete; rejected/duplicate/superseded rows do not count.
- **Duplicate:** duplicate detection may use artifact hash and any explicitly classified requirement context, but it never guesses WorkItem ownership or automatically reuses evidence. SHA-256 is indexed for detection but is not globally unique; a duplicate row points to its canonical raw artifact and remains auditable.
- **Cancellation:** request cancellation is terminal and non-destructive. Child items, evidence links, received documents, and histories remain queryable but are frozen. A new request is required for further collection.
- **Reopen:** reopening is an explicit human correction on the same non-cancelled request. It changes only the affected item/request states, records a reason/history row, and never reopens the financial or worker workflow aggregates.
- **Not required:** an item is intentionally excluded because the requirement does not apply. It is satisfied for request completion, but the decision is reversible and audited.
- **Waived:** an applicable requirement is intentionally excused. It is satisfied for request completion only with an actor and reason; it is reversible and audited.

#### Database invariants, uniqueness, concurrency, and audit

Required invariants and indexes:

- Required foreign keys: `DocumentRequest.WorkItemId`, `DocumentRequest.DocumentRequirementTemplateId`, `DocumentRequestItem.DocumentRequestId`, and both evidence-link foreign keys. `ReceivedDocument` has no required WorkItem foreign key; an unclassified raw artifact is valid with zero evidence links. All applicable foreign keys use restrictive deletes.
- Unique template/version and item keys: `(ServiceId, TemplateKey, Version)`, `(DocumentRequirementTemplateId, RequirementKey)`, `(WorkItemId, Revision)`, and `(DocumentRequestId, RequirementKey)`.
- At most one current request per work item, enforced with a PostgreSQL partial unique index over non-terminal request statuses (`Draft`, `ReadyToSend`, `Requested`, `PartiallyReceived`, `Complete`, `Paused`).
- Unique evidence membership: `(DocumentRequestItemId, ReceivedDocumentId)`. Every evidence link must point to an existing request item and received artifact and must record an explicit classification/reuse action; it must not be generated automatically from hash, sender, conversation, filename, or an inferred company.
- Received-document replacement/duplicate references must be non-self-referential and acyclic. They are artifact-level references and must not infer a WorkItem. A duplicate row must reference a canonical artifact; a superseding row must reference the artifact it replaces. Any evidence links to the affected artifacts remain independently explicit and audited.
- Positive/valid values are enforced for template version, revision, display order, byte length, SHA-256 format, and bounded text lengths. Exact limits for MIME types, file sizes, and storage references are deferred to the implementation/provider boundary.
- A request may be `Complete` only when every required item is `Received`, `Waived`, or `NotRequired`, and the completion action is recorded. The database may enforce local row validity; the aggregate rule is enforced transactionally.
- `Cancelled` and `Superseded` request rows, history rows, received-document rows, and evidence links are retained. No ordinary delete is allowed.

Concurrency and audit rules:

- Current request, request item, raw received artifact, evidence link, and mutable template records use the repository’s `Record` audit fields and `long Version` concurrency token. `CreatedAt`/`CreatedBy` never change; updates advance `UpdatedAt`/`UpdatedBy` and `Version`.
- Every status/reopen/waive/not-required/replace/link/unlink/classification/correction action appends an immutable history row containing previous/new state where applicable, action, reason where required, actor, source, correlation/request identifier where available, and UTC timestamp.
- State-changing operations update the child/current row, affected evidence links, aggregate request status, and history rows in one database transaction. Operations racing on the same work item/current request or received artifact use optimistic version checks plus the repository’s serializable/locking pattern where needed to guarantee one current request, no lost aggregate transition, and no partially applied accepted-document correction. A conflict returns a refresh/retry result; it never silently overwrites a newer decision.
- History rows are append-only and must not be used as editable snapshots. Provider/system actor semantics and external event identifiers are reserved for Phase 0C/0D, but the core audit contract requires them to be representable rather than relying only on the generic `system` actor.

#### Existing historical workflow records

- Existing `WorkerAssignmentWorkflowHistory` and weekly-report snapshots containing `DocumentRequested` or `DocumentReceived` remain unchanged and are treated as historical workflow evidence only.
- Phase 0B creates no synthetic `DocumentRequest`, `DocumentRequestItem`, `ReceivedDocument`, or evidence rows from those old workflow values. The repository has no reliable document/item/file mapping or historical acceptance evidence to justify a backfill.
- Existing historical rows are not retroactively linked to a new request. A future UI may display them as legacy workflow context, but they do not satisfy a new request item.
- The core model owns document-collection state, not `WorkflowStatus`. A later approved Phase 0D rule may map a successful first request to `DocumentRequested` and a human-confirmed complete request to `DocumentReceived`; those transitions must preserve the existing assignment workflow history and must not change financial status.

#### Phase 0B unresolved questions carried to later phases

No core entity, cardinality, status, transition, invariant, WorkItem linkage, replacement rule, or historical-record treatment remains unresolved within Phase 0B. The following are intentionally deferred and must not be pulled into this phase:

- Phase 0C: PIC/contact identity, customer/firm/manager/worker participant scope, group/direct conversation ownership, batch confidentiality, provider identifiers, webhook identity, and permission implementation.
- Phase 0C: SharePoint site/library/folder/metadata references, storage-provider failure states, temporary media handling, and external file-access rules.
- Phase 0D: follow-up cadence, anti-spam scheduling, promised-date/snooze scope, exact workflow transition authority, retry/idempotency/outbox behaviour, provider/system audit correlation, and AI suggestion boundaries.
- Phase 1/implementation: exact CLR/table names, migration ordering, backfill execution, provider-specific processing states, and concrete MIME/size/security limits once the external boundaries are approved.

#### Phase 0C — Contact, WhatsApp & SharePoint Boundaries

**No production coding.**

Finalise only:

- PIC/contact relationships
- Group/direct conversation model and authorised scope
- Request batching confidentiality boundary
- SharePoint folder/metadata strategy
- Storage abstraction boundary
- Permission/least-privilege direction
- External configuration prerequisites that must be verified later

Deliverable: reviewed integration-boundary specification in this file.

#### Phase 0D — Automation, Reliability & Final Freeze

**No production coding.**

Finalise only:

- Follow-up and anti-spam rules
- Promised-date/snooze scope
- Workflow integration rules
- Failure/retry/idempotency requirements
- Audit requirements
- Security constraints
- AI boundaries
- Remaining unresolved decisions

Deliverable: final Phase 0 architecture freeze and explicit approval/readiness statement for Phase 1.

Only after Phase 0D is reviewed and approved may Phase 1 begin.

### Phase 1 — Core Database Model

Add only the structural entities/enums/migration/tests.

No WhatsApp API. No SharePoint API. No AI.

Split into smaller `1A/1B/...` execution units before implementation if repository inspection shows the migration/model work is too broad for one focused Luna Max run.

### Phase 2 — Document Requirement Templates

Add reusable service-based document requirement templates with Required/Optional, Priority/Wave, display order, active status.

Split before implementation if model, UI, validation, and tests cannot remain one small cohesive change.

### Phase 3 — Document Request Internal UI

Build request/checklist UI and manual state transitions before external integrations.

Split UI, application logic, and workflow integration if the diff would otherwise become broad.

### Phase 4 — Contact / PIC Model

Support one PIC across multiple companies/engagements, WhatsApp consent/preferences, pause/do-not-contact.

Split schema/model work from UI/management work where useful.

### Phase 5 — Request Batching

Create `DocumentRequestBatch` logic and preview consolidated requests for the same eligible PIC/conversation.

No sending yet.

Split deterministic batching rules from preview UI if needed.

### Phase 6 — Request Waves / Client Workload Control

Implement Start Work / Normal / Later / Optional and default 3–5 client-facing next-action items.

### Phase 7 — SharePoint Integration Foundation

Implement `IDocumentStorage` + `SharePointDocumentStorage`, metadata/reference model, safe upload test path.

This milestone should normally be split into smaller execution units such as abstraction/reference model first, provider integration second, and failure/retry test path third.

### Phase 8 — WhatsApp Infrastructure

Implement WhatsApp service abstraction, Meta provider, webhook verification, signature validation, message-ID deduplication, logging, retry safety.

Manual/test sending only.

This milestone must be split into smaller execution units before coding; do not implement the entire provider/webhook/reliability stack in one Luna Max run.

### Phase 9 — WhatsApp Group / Direct Conversation Management

Implement conversation type, group/direct metadata, participants, authorised scope, and current Meta eligibility handling.

Split data model/rules from management UI/provider-specific behaviour if needed.

### Phase 10 — Manual WhatsApp Document Request

Add Preview → Send flow. Persist exact outbound snapshot and provider status. Link successful first request to `Document Requested` workflow as approved.

Split message preparation/persistence from provider sending/workflow update if the change becomes broad.

### Phase 11 — Incoming WhatsApp Messages

Handle inbound text, PDF, image, and document webhooks. Show WhatsApp timeline in Billing Control.

Split webhook ingestion/persistence from timeline UI and media handling.

### Phase 12 — Incoming Document → SharePoint

Download media temporarily, validate, upload to SharePoint, persist metadata/reference, remove temporary copy.

This milestone must be split into smaller execution units covering safe media handling, SharePoint persistence, and failure/retry behaviour.

### Phase 13 — Manual Document Classification

Build fast worker UI to classify Company + Document Type + Period + Request Item and confirm receipt.

### Phase 14 — Automatic Follow-Up Engine

Implement scheduler at PIC/conversation/batch level, business-day/cooling-off rules, outstanding-item selection, Follow up now / Snooze / Pause / Resume / Stop.

This milestone must be split into smaller execution units before coding, for example deterministic scheduling rules, persistence/state transitions, then scheduler execution/UI controls.

### Phase 15 — Promised Date Handling

Add promised date and scoped snooze behaviour. Ensure automated reminders respect it.

### Phase 16 — Workflow Automation

Integrate Document Requested / Ready for Document Received / human Confirm Complete behaviour. Do not auto-drive financial states.

### Phase 17 — AI Document Classification

Add AI suggestions for Company + Document Type + Period + confidence. Human confirmation first.

Split provider abstraction, suggestion workflow, and UI/validation if needed.

### Phase 18 — AI Conversation Understanding

Detect promised dates, not-applicable statements, and other useful conversation intents as suggestions requiring confirmation initially.

### Phase 19 — Smart Follow-Up Composition

Use deterministic facts from Billing Control and AI only for natural-language wording.

### Phase 20 — Mature Document Collection Dashboard

Add operational KPIs, PIC-centric view, filters, next-action list, failures, snoozes, classification queue.

Split dashboard data/query work from UI if needed.

### Phase 21 — Audit / Reliability / Security Hardening

Verify:

- Webhook idempotency
- Outbound message outbox
- Retry policies
- Duplicate media handling
- SharePoint failures
- Dead-letter visibility
- Concurrency
- Rate limiting
- Permissions
- Audit trail
- Secrets management
- Structured logging
- Health checks
- CSRF where applicable

This is a milestone, not one Codex run. Split it into focused reliability/security sub-phases and review each separately.

### Phase 22 — Controlled Production Rollout

Roll out gradually:

```text
Stage 1 — Internal test company
Stage 2 — 2–3 friendly customers
Stage 3 — ~10 customers
Stage 4 — Normal production
```

Monitor complaints, reminder frequency, completion time, WhatsApp failures, SharePoint upload issues, AI classification corrections, and support load.

---

## 15. Codex Luna Max Capacity Planning

Use planning capacity only to judge whether a milestone needs splitting. Do not treat a long milestone estimate as permission to give Luna Max one very large task.

Rules:

- **Codex Luna Max is the only Codex model used for this project.**
- Prefer one cohesive, reviewable execution unit at a time.
- Any milestone that appears to require several independent changes must be split before coding.
- External-integration, database, concurrency, security, or reliability work should be split more aggressively.
- It is acceptable to create more sub-phases than originally planned.
- Never combine sub-phases merely to reduce the number of Codex runs.

Correct architecture, data integrity, testability, and reviewability matter more than speed.

---

## 16. Testing Expectations Per Phase

Every coding phase should include tests appropriate to that phase.

At minimum where relevant:

- .NET Release build
- PostgreSQL integration tests
- JavaScript tests
- Authorization/security tests
- Concurrency/idempotency tests
- Migration/model consistency checks
- GitHub Actions
- Docker/Compose smoke where applicable

Integration phases additionally need provider-specific test evidence without exposing secrets.

Do not deploy automatically unless explicitly instructed for the rollout phase.

---

## 17. Codex Working Rules

For every implementation phase or sub-phase:

1. Use **Codex Luna Max only**.
2. Start from the latest approved SHA.
3. Read this master plan and inspect the relevant existing repository code before changing anything.
4. Implement **only the approved phase/sub-phase** plus directly required support code.
5. If the requested task becomes broader than expected, stop at a safe boundary and report what should become the next sub-phase rather than expanding scope silently.
6. Do not silently begin the next phase.
7. Preserve all current Billing Control business logic unless the phase explicitly changes an approved operational rule.
8. Do not modify the existing 35/25/40 revenue-share logic.
9. Do not weaken role-based access or historical workflow rules.
10. Add migrations only when the phase explicitly requires schema changes.
11. Report files changed, migration(s), tests, CI, and final SHA.
12. Stop and explain when Meta/SharePoint tenant configuration prevents safe implementation rather than inventing credentials or bypassing security.
13. Update/synchronise `master` and `codex/accounting-mvp` only after the phase/sub-phase is reviewed/approved according to the current development workflow.

---

## 18. Deferred / Verify-at-Implementation Items

These items are intentionally not hard-coded into the architecture because external platform rules can change:

- Current Meta WhatsApp Group API eligibility and participant limits
- Current WhatsApp message/template pricing
- Current template category/classification rules
- Current customer-service window rules
- Current Microsoft Graph/SharePoint permission options
- Current AI model/provider choice and pricing

Verify current official platform documentation when implementing the relevant phase.

---

## 19. Definition of the Mature End State

The feature is mature when Billing Control can reliably do this:

```text
1. A Work Item identifies required documents from a service template.
2. Requests for one PIC across eligible companies are intelligently batched.
3. Staff preview the next small, manageable request set.
4. Billing Control sends to the approved WhatsApp Group or Direct conversation.
5. Firm/manager/client/LCM MGT can monitor the authorised conversation where appropriate.
6. Client replies and sends files in WhatsApp.
7. Billing Control receives and audits all messages/media.
8. Files are safely stored in SharePoint.
9. Documents are classified and matched to company/period/request item.
10. Follow-ups are consolidated, rate-limited, and limited to the next useful 3–5 items.
11. Promised dates and pauses are respected.
12. All mandatory items are confirmed complete.
13. Workflow can move from Document Requested to Document Received with human confirmation.
14. Dashboard shows outstanding, overdue, snoozed, complete, classification queue, and failures.
15. AI assists classification and wording without becoming the source of truth for operational state.
16. The entire process has a reliable audit trail and safe retry/error handling.
```

---

## 20. Next Action

**Phase 0B is complete and ready for review/approval. Do not start Phase 0C automatically.**

Review the Phase 0B specification above against the current repository and approve or amend it before implementation. When explicitly instructed after approval, begin **Phase 0C — Contact, WhatsApp & SharePoint Boundaries** only. Do not start Phase 0C automatically.

No production code, migration, provider integration, or Phase 0C work was started in Phase 0B.

---

## 21. AI Development Rules

This file and the current repository are the authoritative source of truth for the project. Previous chat history or AI memory may provide useful context, but must not override the current repository or this plan.

For every development phase or sub-phase:

1. Use **Codex Luna Max only** for Codex work in this project.
2. Read this master plan before making changes.
3. Review the current repository implementation relevant to the phase/sub-phase.
4. Keep the execution unit small and focused. If the work is too broad, split it before implementation rather than forcing a large Codex task.
5. Work only on the current approved phase/sub-phase unless a directly required supporting change is necessary for correctness.
6. Do not silently change architecture, business rules, database behaviour, security rules, or previously approved requirements.
7. After Codex completes work, review the actual code changes rather than relying only on its summary.
8. Run or review the relevant tests and check for regressions, security issues, data-integrity risks, concurrency issues, and missing requirements where applicable.
9. Update this master plan when requirements, implementation decisions, architecture, risks, completed work, or project status change.
10. Keep the **Current Project Status** section accurate, including the current phase/sub-phase, completed work, outstanding work, and next step.
11. Preserve useful project history; do not rewrite the plan in a way that hides important prior decisions.
12. Do not begin the next phase/sub-phase until the current one has been reviewed and approved.

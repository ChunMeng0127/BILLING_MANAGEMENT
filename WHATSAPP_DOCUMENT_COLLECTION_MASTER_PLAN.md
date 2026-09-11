# WhatsApp Document Collection & Follow-Up — Master Plan

> **Repository:** `ChunMeng0127/BILLING_MANAGEMENT`  
> **Planning baseline:** `6677680feb12141eefe97dd5d21b37fd3b6b4bf1`  
> **Primary implementation model:** Luna Max, split into small bounded phases  
> **Permanent document storage:** SharePoint  
> **Status:** Architecture / roadmap only — implement phase-by-phase, do not build everything in one Codex run.

---

## Current Project Status

**Current Phase:** Phase 0 — Architecture Freeze  
**Status:** In Progress — Phase 0 must be reviewed and frozen before Phase 1 begins.  
**Last Reviewed:** 2026-09-12

### Completed

- Master roadmap and phased implementation plan drafted.
- Core business rules, architecture direction, integrations, testing expectations, and rollout sequence documented.

### Current Work

- Validate the Phase 0 architecture against the current repository.
- Turn remaining conceptual items into exact implementation decisions.

### Outstanding

- Final entity relationships and statuses.
- PIC/contact and WhatsApp group/direct rules.
- SharePoint folder/metadata and permission rules.
- Request batching, anti-spam, follow-up, and workflow integration rules.
- Failure, retry, idempotency, audit, and AI-boundary decisions.

### Next Step

Complete and review Phase 0. Do not begin Phase 1 until Phase 0 is approved and this file is updated accordingly.

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

Final names may change during Phase 0 architecture review, but the mature model should cover these concepts.

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
WorkItemId
BillingRecordId
DocumentRequestId
DocumentRequestItemId
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

## 14. Phased Luna Max Implementation Roadmap

Use **small bounded phases**. One phase should not quietly expand into multiple architectural changes.

For architecture/database/security/integration phases, use **Luna Max**.

### Phase 0 — Architecture Freeze

**No production coding.**

Finalise:

- Entity model
- Statuses
- Relationships
- PIC/contact model
- WhatsApp group/direct model
- SharePoint folder + metadata design
- Template rules
- Request batching
- Anti-spam rules
- Follow-up rules
- Workflow integration
- AI boundaries
- Permissions
- Failure/retry model

Deliverable: architecture/specification update to this file or a reviewed implementation section before Phase 1.

Estimated Codex time: 1–2 hours.

### Phase 1 — Core Database Model

Add only the structural entities/enums/migration/tests.

No WhatsApp API. No SharePoint API. No AI.

Estimated Codex time: 2–4 hours.

### Phase 2 — Document Requirement Templates

Add reusable service-based document requirement templates with Required/Optional, Priority/Wave, display order, active status.

Estimated Codex time: 3–5 hours.

### Phase 3 — Document Request Internal UI

Build request/checklist UI and manual state transitions before external integrations.

Estimated Codex time: 3–5 hours.

### Phase 4 — Contact / PIC Model

Support one PIC across multiple companies/engagements, WhatsApp consent/preferences, pause/do-not-contact.

Estimated Codex time: 3–4 hours.

### Phase 5 — Request Batching

Create `DocumentRequestBatch` logic and preview consolidated requests for the same eligible PIC/conversation.

No sending yet.

Estimated Codex time: 3–5 hours.

### Phase 6 — Request Waves / Client Workload Control

Implement Start Work / Normal / Later / Optional and default 3–5 client-facing next-action items.

Estimated Codex time: 2–4 hours.

### Phase 7 — SharePoint Integration Foundation

Implement `IDocumentStorage` + `SharePointDocumentStorage`, metadata/reference model, safe upload test path.

Estimated Codex time: 4–5 hours; allow 1–2 usage windows if tenant/Graph debugging is required.

### Phase 8 — WhatsApp Infrastructure

Implement WhatsApp service abstraction, Meta provider, webhook verification, signature validation, message-ID deduplication, logging, retry safety.

Manual/test sending only.

Estimated Codex time: 4–5 hours; allow 1–2 usage windows.

### Phase 9 — WhatsApp Group / Direct Conversation Management

Implement conversation type, group/direct metadata, participants, authorised scope, and current Meta eligibility handling.

Estimated Codex time: 3–5 hours.

### Phase 10 — Manual WhatsApp Document Request

Add Preview → Send flow. Persist exact outbound snapshot and provider status. Link successful first request to `Document Requested` workflow as approved.

Estimated Codex time: 3–4 hours.

### Phase 11 — Incoming WhatsApp Messages

Handle inbound text, PDF, image, and document webhooks. Show WhatsApp timeline in Billing Control.

Estimated Codex time: 3–5 hours.

### Phase 12 — Incoming Document → SharePoint

Download media temporarily, validate, upload to SharePoint, persist metadata/reference, remove temporary copy.

Estimated Codex time: 4–5 hours; allow 1–2 usage windows.

### Phase 13 — Manual Document Classification

Build fast worker UI to classify Company + Document Type + Period + Request Item and confirm receipt.

Estimated Codex time: 2–4 hours.

### Phase 14 — Automatic Follow-Up Engine

Implement scheduler at PIC/conversation/batch level, business-day/cooling-off rules, outstanding-item selection, Follow up now / Snooze / Pause / Resume / Stop.

Estimated Codex time: 4–5 hours; allow 1–2 usage windows.

### Phase 15 — Promised Date Handling

Add promised date and scoped snooze behaviour. Ensure automated reminders respect it.

Estimated Codex time: 2–3 hours.

### Phase 16 — Workflow Automation

Integrate Document Requested / Ready for Document Received / human Confirm Complete behaviour. Do not auto-drive financial states.

Estimated Codex time: 3–4 hours.

### Phase 17 — AI Document Classification

Add AI suggestions for Company + Document Type + Period + confidence. Human confirmation first.

Estimated Codex time: 4–5 hours; allow 1–2 usage windows.

### Phase 18 — AI Conversation Understanding

Detect promised dates, not-applicable statements, and other useful conversation intents as suggestions requiring confirmation initially.

Estimated Codex time: 3–5 hours.

### Phase 19 — Smart Follow-Up Composition

Use deterministic facts from Billing Control and AI only for natural-language wording.

Estimated Codex time: 2–4 hours.

### Phase 20 — Mature Document Collection Dashboard

Add operational KPIs, PIC-centric view, filters, next-action list, failures, snoozes, classification queue.

Estimated Codex time: 3–5 hours.

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

Estimated Codex time: 4–5 hours; allow 1–2 usage windows.

### Phase 22 — Controlled Production Rollout

Roll out gradually:

```text
Stage 1 — Internal test company
Stage 2 — 2–3 friendly customers
Stage 3 — ~10 customers
Stage 4 — Normal production
```

Monitor complaints, reminder frequency, completion time, WhatsApp failures, SharePoint upload issues, AI classification corrections, and support load.

Estimated Codex time: 2–4 hours.

---

## 15. Estimated Luna Max Usage Budget

Treat the following as planning capacity, not literal runtime.

| Area | Approx. 5-hour Codex windows |
| --- | ---: |
| Core document request system | 4–5 |
| SharePoint integration | 1–2 |
| WhatsApp base integration | 3–4 |
| Batch/follow-up automation | 2–3 |
| AI maturity | 3–4 |
| Dashboard/hardening/deployment | 2–3 |
| **Full mature version** | **15–20** |

Recommended planning reserve: **20 × 5-hour windows**.

Do not combine phases merely to reduce the number of windows. Correct architecture and testability matter more than squeezing work into one run.

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

For every implementation phase:

1. Start from the latest approved SHA.
2. Implement **only that phase** plus directly required support code.
3. Do not silently begin the next phase.
4. Preserve all current Billing Control business logic unless the phase explicitly changes an approved operational rule.
5. Do not modify the existing 35/25/40 revenue-share logic.
6. Do not weaken role-based access or historical workflow rules.
7. Add migrations only when the phase explicitly requires schema changes.
8. Report files changed, migration(s), tests, CI, and final SHA.
9. Stop and explain when Meta/SharePoint tenant configuration prevents safe implementation rather than inventing credentials or bypassing security.
10. Update/synchronise `master` and `codex/accounting-mvp` only after the phase is reviewed/approved according to the current development workflow.

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

**Start with Phase 0 only.**

Do not begin implementation until Phase 0 architecture decisions are reviewed and frozen. Phase 0 should validate this master plan against the current repository and turn any remaining conceptual items into exact entity relationships, statuses, permissions, SharePoint metadata/folder rules, WhatsApp conversation rules, and failure-handling rules.

---

## 21. AI Development Rules

This file and the current repository are the authoritative source of truth for the project. Previous chat history or AI memory may provide useful context, but must not override the current repository or this plan.

For every development phase:

1. Read this master plan before making changes.
2. Review the current repository implementation relevant to the phase.
3. Work only on the current approved phase unless a directly required supporting change is necessary for correctness.
4. Do not silently change architecture, business rules, database behaviour, security rules, or previously approved requirements.
5. After Codex completes work, review the actual code changes rather than relying only on its summary.
6. Run or review the relevant tests and check for regressions, security issues, data-integrity risks, concurrency issues, and missing requirements where applicable.
7. Update this master plan when requirements, implementation decisions, architecture, risks, completed work, or project status change.
8. Keep the **Current Project Status** section accurate, including the current phase, completed work, outstanding work, and next step.
9. Preserve useful project history; do not rewrite the plan in a way that hides important prior decisions.
10. Do not begin the next phase until the current phase has been reviewed and approved.

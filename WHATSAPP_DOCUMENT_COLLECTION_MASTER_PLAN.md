# WhatsApp Document Collection Master Plan

## Repository baseline and Phase 0A scope

Repository: `ChunMeng0127/BILLING_MANAGEMENT`
Branch reviewed: `codex/accounting-mvp`
Review date: 2026-09-12

This document is the planning artifact for the WhatsApp document-collection work. Phase 0A is limited to repository and architecture inventory. It does not introduce production entities, migrations, controllers, provider calls, storage, or business-logic changes. Phase 0B has not started.

### Baseline availability issue

The requested `WHATSAPP_DOCUMENT_COLLECTION_MASTER_PLAN.md` was not present in the working tree, any local branch/ref, or the reachable Git history when Phase 0A began. The repository was clean before this file was created. Consequently, there was no prior plan text available to preserve or read, and no WhatsApp-specific requirements can be treated as confirmed from the missing document. This section and the inventory below record repository facts only; restoring the intended pre-existing plan is an open prerequisite before Phase 0B.

## Current Project Status

**Phase 0A — Repository & Architecture Inventory: COMPLETE (inventory recorded; Phase 0B not started).**

Confirmed baseline:

- The application is an ASP.NET Core 10 MVC/Razor application using Entity Framework Core 10, ASP.NET Core Identity, PostgreSQL 17, and locally bundled Bootstrap. The README describes it as an internal billing/work MVP for fewer than 10 users and explicitly says there are no external accounting integrations (`README.md:1-3`).
- The current branch is `codex/accounting-mvp`. No production source, business logic, migration, test, CI, deployment, or configuration file was changed for this phase; this planning document is the only intended change.
- The latest committed EF migration is `20260910052737_WeeklyProgressUpdateHistory`. The current workflow release is documented as not deployed to Hostinger (`README.md:87-93`, `README.md:118`).
- Phase 0B must begin only after the missing baseline plan is restored or its intended requirements are supplied and the open questions below are answered.

## Confirmed repository facts

### Application and runtime architecture

- `src/BillingControl/BillingControl.csproj` has only the application’s core package references: ASP.NET Core Identity EF integration, EF Core design tooling, and the Npgsql EF Core provider. There is no WhatsApp SDK, HTTP client abstraction, queue package, job scheduler, object-storage SDK, malware scanner, OCR library, or document-management package.
- `src/BillingControl/Program.cs:13-78` configures one web application, one `AppDbContext`, Identity, authorization, the existing domain services, a system `TimeProvider`/`BusinessClock`, and rate limiting. There is no `IHostedService`, `BackgroundService`, scheduled job registration, queue consumer, or separate worker process.
- The production Compose topology is `db`, one one-shot `migrate` container, one long-running `app` container, and an Nginx/Traefik-facing proxy (`compose.yaml`, `docker-compose.yaml`). The application has a persistent data-protection-key volume and PostgreSQL volume, but no application media/file-storage volume. The Hostinger-oriented `docker-compose.yaml` builds from the repository’s `master` branch, so the reviewed `codex/accounting-mvp` branch is not the automatic Hostinger deployment source.
- The request/response boundary is MVC controller → scoped domain service → EF Core/PostgreSQL. `AppController` converts business and database/concurrency errors into a redirect with a user-facing message (`src/BillingControl/Controllers/AppController.cs:9-44`).

### Existing entities and relationships relevant to document collection

The current graph is financial/work-oriented. There is no document-collection entity in the repository.

| Existing entity | Confirmed relationship and relevance |
| --- | --- |
| `Customer`, `Service`, `BusinessParty`, `Manager`, `Worker` | Master records. `Worker` has a `WorkerType` and optional email. These are the parties that a future collection flow might need to identify, but none currently has a WhatsApp number, messaging identity, consent, or document-contact relationship. (`src/BillingControl/Models/Domain.cs:26-37`) |
| `Engagement` | Links one customer, service, accounting firm (`BusinessParty`), and manager; holds dates, billing amount, revenue-share percentages, status, and one `BillingSchedule`. (`Domain.cs:63-89`) |
| `BillingRecord` | Belongs to an engagement, stores a service period, billing status, customer/service name snapshots, amount, revenue-share base snapshot, and share allocations. Each billing record has one `WorkItem` and zero or more invoice lines. (`Domain.cs:91-110`) |
| `WorkItem` | One-to-one with `BillingRecord`; stores `WorkStatus`, notes, and many `WorkerAssignment` rows. It is created with the billing record in `BillingService.Generate` (`BillingService.cs:13-60`). (`Domain.cs:123-129`) |
| `WorkerAssignment` | Many-to-one with `WorkItem` and `Worker`; stores worker-name, percentage, LCM-gross, entitlement, cancellation, visibility, current workflow state/version/progress, and links to payments, weekly reports, progress-history rows, and workflow-history rows. (`Domain.cs:131-154`) |
| `WeeklyProgressReport` | At most one report per assignment/week. It stores the latest weekly summary, progress, workflow snapshot, submission time, and a link to append-only update history. (`Domain.cs:156-171`, `AppDbContext.cs:97-106`) |
| `WeeklyProgressUpdateHistory` | Append-only operational timeline linked both to a weekly report and directly to the assignment; records workflow/progress/content, actor, source, and occurrence time. (`Domain.cs:173-187`, `AppDbContext.cs:107-120`) |
| `WorkerAssignmentWorkflowHistory` | Append-only assignment-level workflow/visibility transition history. (`Domain.cs:189-198`, `AssignmentWorkflowService.cs:120-170`) |
| `Invoice`, `InvoiceLine`, `CustomerReceipt`, `CustomerReceiptAllocation` | Existing financial documents and allocations. They are not uploaded source documents and contain no file/media/message metadata. (`Domain.cs:220-265`) |

The current relationship path most relevant to a future collection feature is:

`Engagement → BillingRecord → WorkItem → WorkerAssignment → Worker`

with `BillingRecord → InvoiceLine → Invoice` and `WorkerAssignment → WeeklyProgressReport / history` as separate downstream paths. The repository does not establish that a future WhatsApp request must attach at any one of those levels.

There are no entities or tables for `DocumentRequest`, document type/checklist, document item/version, attachment/media, conversation/thread, inbound/outbound message, WhatsApp sender, provider event, webhook delivery, consent, or file-retention state.

### Work Item and Worker Assignment implementation

- `WorkItem.Status` is the small operational status enum `Upcoming`, `InProgress`, `Completed`; `WorkItem.Notes` is free text. Shared work status/notes can be changed only by Admin/InternalUser through `WorkController.Update` (`src/BillingControl/Models/Domain.cs:38-42`, `src/BillingControl/Controllers/WorkController.cs:45-60`). Workers submit weekly reports instead of changing shared work status.
- A work item is created as part of billing generation. Worker assignment is a separate staff action. Multiple active assignments are allowed; combined worker percentages and rounded entitlements are capped against the immutable LCM gross share (`BillingService.cs:141-158`). A 0% assignment is still active operational work but has RM0.00 entitlement.
- Assignment cancellation is a business-state operation rather than row deletion. Active payments must be cancelled first. Assignment edits use the assignment `Version`; changing the worker after progress exists is blocked and requires cancellation/reassignment (`BillingService.cs:160-190`, `BillingService.cs:235-247`).
- Assignment visibility is represented by `IsHidden`, `HiddenAt`, `HiddenBy`, and history. Hidden assignments are not exposed to the linked worker, including through forged URLs (`AccessScope.cs:127-148`).
- The existing work UI has no document checklist, attachment view, incoming-message view, document acceptance/rejection action, or customer/firm submission route (`src/BillingControl/Controllers/WorkController.cs`, `src/BillingControl/Views/Work`).

### Billing Record implementation

- Billing generation is schedule-validated and runs in a serializable transaction. It creates the `BillingRecord` and its one `WorkItem` together, snapshots customer/service names and revenue shares, and advances the schedule (`BillingService.cs:13-60`).
- Billing status is `Upcoming`, `WorkInProgress`, `Completed`, `ReadyToBill`, `Billed`, `PartiallyPaid`, `Paid`, or `Cancelled` (`Domain.cs:38-42`). Invoice and receipt state is recalculated from active financial records; derived financial statuses cannot be forged by an ordinary billing edit (`BillingService.cs:62-139`, `BillingService.cs:224-233`).
- Historical financial snapshots are deliberately protected. Customer amount and revenue-share-base corrections have dependency locks; period changes require no active invoices, assignments, or downstream payments. Billing, invoice, receipt, assignment, and payment cancellation rules are explicit and do not imply document-collection completion (`BillingService.cs:62-139`, `README.md:72-84`).
- `BillingRecord` has no fields for requested documents, received documents, outstanding checklist items, customer submission dates, or document evidence. Its service period must not be assumed to be the collection window without a Phase 0B decision.

### Workflow and status implementation

- `WorkflowStatus` already contains eleven worker-delivery stages: `DocumentRequested`, `DocumentReceived`, `AssignedNotStarted`, `AssignmentStarted`, `StartPreparing`, `QueriesSent`, `DraftManagementReportSent`, `PendingReview`, `AmendmentRevision`, `FinalManagementReportSent`, and `Completed` (`Domain.cs:44-56`). The labels are defined in `Finance.Label` (`Finance.cs:38-84`).
- Workflow state is deliberately separate from billing, invoicing, payment, and `WorkItem.Status`. `AssignmentWorkflowService` owns current assignment workflow and append-only transition history; its class comment explicitly says it does not change `WorkItem`, `BillingRecord`, invoice, or payment state (`AssignmentWorkflowService.cs:7-15`).
- `QueriesSent` and `DraftManagementReportSent` require positive versions. Repeated stage/version rules are enforced using history. Batch workflow updates may update current state/history but never create weekly reports (`AssignmentWorkflowService.cs:99-149`, `:172-216`).
- Weekly reporting uses business-timezone Monday–Sunday windows through `BusinessClock`. Missing is calculated when no report exists; submitted/late classification is based on the first update timestamp. Current-week worker edits update the assignment current state and append update history; historical reports preserve historical snapshots and do not reopen current work (`ProgressReportService.cs:1-113`, `AssignmentWorkflowService.cs:22-97`, `README.md:87-93`).
- `DocumentRequested` and `DocumentReceived` are therefore existing workflow labels, not evidence that the repository can track a particular document, its source, its contents, or a WhatsApp exchange. Reusing those labels for collection semantics without defining the boundary would risk conflating assignment workflow with document-level state.

### Authorization and role handling

- The five Identity roles are `Admin`, `InternalUser`, `AccountingFirm`, `Manager`, and `Worker`. `Admin` and `InternalUser` are staff; external roles are linked to exactly one `BusinessParty`, `Manager`, or `Worker` respectively (`src/BillingControl/Services/AccessScope.cs:9-47`).
- A fallback authorization policy requires authentication and a valid access profile for every endpoint except explicitly anonymous login/denied actions (`Program.cs:23-32`). The custom authorization handler verifies one role, an active user, and role/link consistency against the database on every request (`AccessScope.cs:49-60`). Security-stamp validation is immediate (`Program.cs:30`).
- `AccessScope` is the server-side scoping boundary. It scopes engagements, billing, schedules, work items, assignments, progress reports, payments, masters, invoices, and reports by linked entity. Out-of-scope identifiers normally become `404`; role-prohibited operations use authorization/forbidden behavior (`AccessScope.cs:63-202`). `CreatedBy` is audit metadata and is not used as ownership.
- Work/progress permissions are currently: staff can edit shared work and assignments; workers can see/update their own active assignments and weekly updates; managers have scoped read-only progress access; AccountingFirm has no work/progress route. Users cannot directly edit financial records outside their role-specific controller permissions (`WorkController.cs:10-103`, `ProgressController.cs:9-164`).
- User administration is Admin-only. Role and entity-link changes update the security stamp and revoke existing sessions; the database check constraint prevents more than one external entity link (`UsersController.cs:12-75`, `AppDbContext.cs:37-40`).
- No role currently represents a customer/end-customer user or an unauthenticated external WhatsApp sender. A future inbound message must not be treated as authorized merely because its phone number appears to match a master record; identity proof, sender matching, and permitted visibility are unresolved.

### Database and migration conventions

- `AppDbContext` derives from `IdentityDbContext<AppUser>` and exposes DbSets for all business entities. Record-derived entities use integer primary keys, a `long Version` concurrency token, audit fields (`CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`), PostgreSQL numeric precision, and string length conventions (`src/BillingControl/Data/AppDbContext.cs:8-49`).
- All record foreign keys default to `DeleteBehavior.Restrict`. `SaveChangesAsync` rejects ordinary record deletion and only permits narrowly scoped invoice-line deletion, billing snapshot correction, and receipt-allocation correction. Modified-field allowlists protect historical snapshots and audit origins (`AppDbContext.cs:129-189`).
- The model uses explicit check constraints and unique/filtered indexes for dates, positive money, percentages, assignment/workflow versions, one report per assignment/week, request-id idempotency, and allocation uniqueness (`AppDbContext.cs:50-128`).
- Migrations are timestamp-prefixed EF Core migrations in `src/BillingControl/Data/Migrations`, with the generated `Designer.cs` and `AppDbContextModelSnapshot.cs` checked in. The latest sequence is: `InitialCreate`, `EntityScopedAccess`, `InvoiceDocuments`, `InvoiceIssuerAndReceiptIntegrity`, `RevenueShareBaseAmount`, `AllowZeroWorkerAssignmentPercent`, `WeeklyWorkerProgressReporting`, `WorkerAssignmentWorkflowState`, and `WeeklyProgressUpdateHistory`.
- The repository has precedent for data-preserving migrations with explicit SQL backfills and safety checks. `InvoiceDocuments` migrated legacy invoice/receipt fields into normalized document/allocation rows and refused a downgrade after split/consolidated/unallocated data existed (`20260907101738_InvoiceDocuments.cs:160-201`, `:229-275`). Later workflow migrations are additive and do not fabricate historical actor/timestamp data (`20260909152736_WorkerAssignmentWorkflowState.cs`, `20260910052737_WeeklyProgressUpdateHistory.cs`).
- Schema changes are applied by the explicit `--migrate` path (`Program.cs:71-76`) and by CI integration-test setup. Normal application startup does not alter the schema. The documented generation convention is `dotnet ef migrations add DescriptiveChange --project src/BillingControl --output-dir Data/Migrations` (`README.md:95-105`).
- Integration tests refuse to run against a database whose name does not end in `_test`; `Fresh()` drops/recreates only that database and applies committed migrations (`tests/BillingControl.Tests/IntegrationTests.cs:14-27`). This is a material constraint for any Phase 0B migration test.

### Background jobs and integration abstractions

Confirmed absent from the current repository:

- No WhatsApp or messaging provider integration, webhook controller, provider signature verifier, phone-number/contact abstraction, message inbox/outbox, media-download service, file scanner, object-storage adapter, or document metadata service.
- No background job abstraction, scheduler, durable queue, retry policy, hosted worker, or outbox dispatcher. The only long-running application process is the MVC web app; `migrate` is a one-shot container.
- No application-level `HttpClient`/typed-client registration or external integration configuration. The only external-facing runtime concerns currently visible are PostgreSQL, Identity email/password login, forwarded headers, proxy/HTTPS, and rate limiting.

This means a WhatsApp collection feature would require architectural decisions about inbound webhook handling, provider API/media retrieval, durable idempotency, asynchronous work, retry/failure state, and media storage before implementation. Phase 0A does not choose any of them.

### Existing tests and CI

- `tests/BillingControl.Tests` contains xUnit tests, MVC integration tests through `WebApplicationFactory<Program>`, and a fixed `TimeProvider` used for workflow-window assertions. `tests/js` contains dependency-free Node test files for browser-side invoice allocation, receipt editing, and site-shell behavior.
- The integration suite covers financial allocation/concurrency, billing and invoice/receipt behavior, worker assignment/payment constraints, weekly workflow and history, authentication/CSRF, all five roles, entity-scope/tampering checks, reporting/export scope, financial redaction, and session revocation. The existing tests do not cover WhatsApp, external webhooks, document attachments, media retrieval, file access, document retention, or provider failure/retry behavior.
- `.github/workflows/ci.yml` runs on push and pull request with PostgreSQL 17, .NET 10, Node 22, JavaScript tests, `dotnet restore`, Release build, `dotnet test`, Docker image build, and a production Compose HTTPS smoke test. CI has no WhatsApp/provider secrets and no external integration environment.
- `README.md:95-105` is the documented local verification path: `dotnet build -c Release`, set an isolated `_test` PostgreSQL connection, then run `dotnet test`; JavaScript tests and CI are described there as well.

## Conflicts, gaps, and risks for the WhatsApp plan

These are repository-to-plan risks. They are not proposed solutions.

1. **The master-plan baseline is missing.** The requested source document could not be read, and there is no existing planning text to preserve. Requirements, decisions, exclusions, and prior terminology may be lost until the intended plan is restored or supplied.
2. **Existing “Document Requested/Received” states are not document records.** They live on assignment workflow/history and weekly-report snapshots. They cannot identify which document was requested, who submitted it, which WhatsApp message carried it, whether it was accepted, or whether the received set is complete.
3. **The correct aggregate is undecided.** The current graph supports engagement, billing period, work item, worker assignment, and weekly report scopes, but no repository rule says whether a collection request is per customer, engagement, billing period, work item, assignment, or a separate case. Attaching it to the wrong level could duplicate requests or leak data across periods/assignments.
4. **Current worker scope may be too narrow for collection intake.** Worker access is assignment-based and excludes hidden/cancelled assignments. AccountingFirm cannot access work/progress routes. If the plan expects customers or accounting firms to submit documents through WhatsApp, neither an authenticated external-user path nor the required scope exists.
5. **Status layering is a risk.** `WorkItem.Status`, `BillingStatus`, assignment `WorkflowStatus`, weekly report status, and document-level lifecycle would be separate concepts. Updating one from an inbound message without an explicit state-transition contract could incorrectly mark work complete or change financial state.
6. **There is no durable media path.** Production Compose has PostgreSQL and data-protection-key persistence but no media/object-storage volume. Storing bytes, provider URLs, hashes, scan results, retention/deletion state, and access logs requires a deliberate architecture and operational backup/restore policy.
7. **There is no asynchronous delivery boundary.** WhatsApp media retrieval, outbound reminders, retries, signature failures, provider rate limits, and dead-letter/manual-retry handling cannot be expressed by the current synchronous MVC/service abstractions alone.
8. **Webhook and duplicate-event safety are absent.** Existing `RequestId` uniqueness protects customer receipts and worker payments, not provider event IDs or media messages. A webhook design will need an explicit idempotency and replay policy; otherwise provider retries can duplicate messages, files, or workflow changes.
9. **Audit actor semantics are incomplete for integrations.** `AppDbContext.SaveChangesAsync` obtains the actor from the authenticated HTTP claim and otherwise uses `system`. A provider event needs its own durable sender/provider-event identity and audit meaning; `system` alone is insufficient to reconstruct a message trail.
10. **Migration safety matters.** Existing migrations use restrictive foreign keys, additive changes, explicit backfills, and safe handling of irreversible data transformations. A future document/media migration must account for existing production data, backups, deployment ordering, and the fact that the Hostinger importer tracks `master`, not this branch.
11. **Test coverage has an external-boundary gap.** CI can run deterministic fakes but has no provider credentials or external WhatsApp service. Acceptance will need contract tests, signed-webhook tests, idempotency/retry tests, storage/file-access tests, and scoped authorization tests that fit the isolated `_test` database rule.

## Unresolved questions for Phase 0B

Phase 0B must answer these questions before a data model or production implementation is designed.

### Requirements and scope

- Restore or supply the missing master-plan requirements. What is explicitly in scope for the first release, and what is explicitly out of scope?
- Is collection organized by customer, engagement, service period/billing record, work item, worker assignment, or an independent recurring case? Can one request cover multiple periods or services?
- What document categories/checklist items are required? Are requests one-off, recurring, conditional, or versioned? Can a request be partially satisfied or reopened?
- What does “received” mean: message arrived, media downloaded, file passed validation, human accepted, or all required documents received?
- Does document collection update the existing assignment workflow, only provide evidence alongside it, or have an independent lifecycle?

### WhatsApp/provider boundary

- Which provider and API mode will be used (Meta WhatsApp Cloud API, Business Solution Provider, or another gateway)? Is there one tenant/business number or multiple numbers?
- Are inbound messages, outbound requests/reminders, replies, templates, delivery/read status, groups, and voice notes in scope?
- How is the sender identity verified and matched to a customer/contact/engagement? What happens when a number is shared, changed, unknown, or matches multiple parties?
- What webhook signature, replay-window, provider-event-ID, and idempotency guarantees are required? How will provider outages, rate limits, retries, and manual replay be handled?
- Is customer/firm consent or opt-in required, and where is it recorded? Are privacy notices, retention notices, or opt-out handling required?

### Document/media handling

- Which MIME types, file sizes, page counts, image dimensions, and filename rules are allowed? Are ZIPs, spreadsheets, photos, PDFs, or password-protected files accepted?
- Where are original bytes stored, and for how long? Must files be encrypted at rest, virus-scanned, content-hashed, deduplicated, OCR-processed, or immutable?
- Are provider media URLs treated as temporary references only? What backup/restore and disaster-recovery guarantees are required for media?
- Who may list, download, preview, accept, reject, delete, or re-request a document? Are download/access events part of the audit record?
- What happens to a file that is malicious, unreadable, duplicate, wrong-period, incomplete, or sent after the related assignment/billing is cancelled?

### Roles, workflow, and audit

- Are existing five roles sufficient, or must the system represent customer/end-customer contacts, firm staff, or provider identities separately from logged-in application users?
- Should staff, managers, workers, accounting firms, and external senders have different visibility into message text, phone numbers, filenames, file contents, and rejection reasons?
- Who may request documents, send reminders, manually link/relink a message, accept/reject a file, reopen a request, or override sender matching?
- How should document events interact with `DocumentRequested`, `DocumentReceived`, `WorkItem.Status`, assignment workflow versions, weekly reports, completion, hide/unhide, and cancellation?
- Which actor and timestamp must be preserved for human actions, provider events, automated jobs, and manual corrections? Is append-only history required for all collection transitions and message/media events?

### Persistence, jobs, deployment, and testing

- Is media stored in PostgreSQL, a mounted volume, object storage, or an existing external document system? What credentials, network routes, and local/CI emulators are permitted?
- Is an in-process hosted worker acceptable, or is a durable external queue/job runner required? What are retry, backoff, timeout, dead-letter, and operational-monitoring requirements?
- Should webhook receipt and business processing be separated by an inbox/outbox pattern? What delivery guarantee is required for outbound WhatsApp messages?
- What migration/backfill is needed for existing workflow states or weekly reports? Must the migration be additive only, and what is the rollback/restore procedure for existing production data?
- How will the feature be released when `docker-compose.yaml` sources `master` on Hostinger? Is deployment intentionally excluded from Phase 0A/0B?
- What deterministic CI acceptance is required: provider contract fixtures, signature validation, media test doubles, file download authorization, retry/idempotency tests, browser flow, and production-like Compose smoke tests?

## Phase 0A checks performed

- Read the repository tree, current branch/remotes/status, project files, README, validation notes, source models/services/controllers, EF context, all migration names and relevant migration bodies, tests, CI workflow, Docker/Compose files, and environment template.
- Searched the repository for WhatsApp, document-collection, attachment/media, webhook, integration, queue, scheduler, hosted-service, storage, and HTTP-client abstractions.
- Confirmed the requested master-plan path was absent from the working tree and checked reachable Git refs/history for it before creating this file.
- No production code, business logic, database schema, migration, test, CI, deployment, or runtime configuration was changed.

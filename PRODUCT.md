# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

- The product owner/operator (the user) running internal accounting operations.
- Accounting Firm users managing or reviewing customer billing work.
- Manager users overseeing their work and financial responsibilities.
- Worker users completing assigned work and submitting progress.

The repository describes a small internal deployment for fewer than 10 users; confirm this if the operating scale changes.

## Product Purpose

Billing Control is an internal accounting-work and billing workspace. It is used to operate scoped customer, engagement, billing, work-assignment, progress, invoice, receipt, worker-payment, document-collection, contact/PIC, and WhatsApp-conversation workflows in one place.

Success means the internal team can run those workflows with each role seeing only the information appropriate to it, while financial calculations and historical records remain trustworthy.

## Positioning

Billing Control is an internal, role-scoped accounting and work source of truth connecting billing, work, documents, invoices, receipts, and worker payments while keeping financial calculations server-side and historical snapshots immutable.

## Operating Context

- The product is for internal worker use rather than a public self-service product.
- Typical work moves from directory and engagement setup through billing-period generation, worker assignment and progress, invoicing, customer receipts, worker payments, reporting, and document collection.
- Document collection includes checklists, contacts/PICs, document requests, and WhatsApp conversation records.
- The current repository uses MYR financial records and defaults to the Asia/Kuala_Lumpur business timezone.
- Users operate through an authenticated browser application; there is no public registration flow.

## Capabilities and Constraints

- The existing application is ASP.NET Core 10 MVC/Razor with Entity Framework Core, ASP.NET Core Identity, PostgreSQL 17, and locally bundled Bootstrap. React and external accounting integrations are not part of the current product.
- Role boundaries are represented by Admin, InternalUser, AccountingFirm, Manager, and Worker access profiles. Entity access is scoped server-side; workers do not receive invoice or revenue-share visibility.
- Financial behavior uses server-side decimal calculations, immutable financial/share snapshots, audit metadata, concurrency/version checks, serializable transactions for critical operations, and cancellation/replacement rather than destructive deletion.
- Billing, invoice, receipt, payment, engagement, assignment, progress, document-collection, and messaging workflows already exist in the repository and should be treated as incumbent product behavior until the user explicitly changes scope.
- Authentication, antiforgery protection, session controls, role authorization, and confidential financial-field redaction are product and security constraints.

## Brand Commitments

- The product name is Billing Control.
- No additional brand, logo, voice, or visual-system constraints have been confirmed. Future visual work should treat the current interface as the incumbent implementation until a deliberate design decision is made.

## Evidence on Hand

- `README.md` documents the product purpose, workflows, roles, architecture, financial invariants, deployment model, and test expectations.
- The current MVC views, controllers, services, CSS, and client scripts under `src/BillingControl` are the incumbent implementation.
- `VALIDATION.md` and the repository test suite provide verification evidence for existing behavior.
- No external testimonials, marketing claims, legal copy, or brand asset package has been confirmed; future work must not fabricate them.

## Product Principles

- Keep internal work understandable and operable across the full billing lifecycle.
- Enforce role-scoped confidentiality at the server boundary.
- Treat financial correctness, immutable history, and auditability as core product behavior.
- Preserve workflow continuity and correct records through explicit, traceable operations.

## Accessibility & Inclusion

No product-specific accessibility standard has been confirmed. The required standard and any user-specific accessibility needs remain open decisions.

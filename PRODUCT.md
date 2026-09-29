# Product

<!-- impeccable:product-schema 1 -->

## Platform

Web application, desktop-first.

## Users

- Product owner/operator running internal accounting operations.
- Accounting Firm users reviewing billing and customer scope.
- Manager users overseeing managed work and billing responsibility.
- Worker users completing assigned work and weekly progress.

## Product Purpose

Billing Control is an internal accounting control room for reviewing billing state, moving assigned work forward, and making careful operational corrections without losing financial context.

Phase 1 is a clean-room frontend rebuild of the application shell and Dashboard. It must preserve the existing backend contracts while introducing a new visual system and interaction hierarchy.

## Operating Context

- Authenticated MVC web application with conventional routes.
- Financial records are shown in MYR.
- Users work primarily at desks, including 800–1000px split-screen desktop widths.
- The first Dashboard viewport should orient the user toward current attention items, then provide direct access to registers and filters.

## Navigation Contract

- Overview: Dashboard, Upcoming Billing, Billing Records, Invoices, Receipts.
- Work Management: Work Items, Worker Assignments, Worker Payments, Weekly Progress.
- Documents & Communication: Document Checklists, Contacts / PIC, WhatsApp Conversations.
- Master Data: Customers, Services, Accounting Firms, Managers, Workers, Engagements.
- Administration: User Management.

The shell must preserve the existing conventional MVC URLs and role-dependent access. Navigation visibility is a usability aid only; backend authorization remains authoritative.

## Phase 1 Scope

- New application shell and sidebar navigation.
- New page header system.
- New typography, spacing, buttons, form controls, filters, tables/data-grid standards, status badges, and modal/dialog patterns.
- New Dashboard implementation for `/` and `/Home/Index` using the existing `ReportModel` and `ReportFilter` contract.

Other screens remain functionally supported by the backend and are not redesigned in Phase 1.

## Hard Constraints

- Do not change business logic, calculations, database schema, permissions, routes, accounting logic, financial workflows, or backend behavior.
- Preserve MVC form names, antiforgery behavior, optimistic-concurrency fields, redirects, TempData errors, and role/entity scoping.
- Do not use the existing frontend as a visual or implementation reference.
- No generic AI dashboard styling, excessive cards, decorative gradients, flashy/futuristic effects, or ornamental visual noise.

## Design Direction

Accounting Control Room: precise, calm, practical, professional, minimalist, and data-focused.

The new system should feel like an instrument panel for careful accounting work: a quiet paper-like field, ink-led hierarchy, one deliberate action signal, firm alignment, clear state vocabulary, and honest density for financial registers.

The defining interaction is a priority-led overview: attention items first, supporting context second, full registers available without hiding their meaning.

## Success Criteria

- A user can identify the current Dashboard purpose and next action immediately.
- Important actions remain visible at 1440px, 1024px, 900px, and 800px widths.
- No page-level horizontal overflow; wide financial data may scroll inside its own region.
- The Dashboard remains readable without turning every datum into a card.
- Build, detector, and repository tests pass before the branch is pushed.

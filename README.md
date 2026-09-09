# Billing Control

Internal accounting-work and billing MVP for fewer than 10 users. ASP.NET Core 10 MVC/Razor, Entity Framework Core, ASP.NET Core Identity, PostgreSQL 17 and locally bundled Bootstrap. No React or external accounting integrations.

## Run the prepared local workspace

The prepared Windows workspace has an isolated PostgreSQL instance at `127.0.0.1:55432` and a development app at **http://127.0.0.1:5188**. Local credentials are in `.local/ACCESS.md` (ignored by Git). Start/restart with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Start-Local.ps1
```

The helper uses the existing compiled application; after changes run `dotnet build` first. The `.local` directory contains this machine's portable database, data, keys and credentials; it is not part of the repository. It is not a production deployment or an automatic Windows startup service.

## Fresh development setup

Install .NET SDK 10 and PostgreSQL 17 (or Docker Engine with Compose). Run these commands from the repository root:

```powershell
Copy-Item .env.example .env
# Edit .env: choose a long random POSTGRES_PASSWORD and strong admin credentials.
docker compose -f compose.dev.yaml up -d
$env:ConnectionStrings__Default = 'Host=127.0.0.1;Port=55432;Database=billing_dev;Username=billing;Password=YOUR_PASSWORD'
$env:BootstrapAdmin__Email = 'admin@example.com'
$env:BootstrapAdmin__Password = 'YOUR_STRONG_INITIAL_PASSWORD'
$env:DataProtection__Path = Join-Path (Get-Location) '.local/keys'
$env:Seed__Demo = 'true' # Optional sample customer, service, worker and RM1,000 engagement
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet restore
dotnet tool restore
dotnet build
dotnet run --project src/BillingControl --no-build -- --migrate
dotnet run --project src/BillingControl --no-build --no-launch-profile --urls http://127.0.0.1:5188
```

For native PostgreSQL, create `billing_dev` and an application login first; omit the Docker command. The initial admin is created only when no users exist. There is no default password and no public registration. `--migrate` applies committed EF migrations and initializes roles/admin, then exits. Normal startup does not alter the schema. Disable `Seed__Demo` for operational databases. Demo seeding is idempotent and does not create billing or payment transactions.

After setup, remove the bootstrap password from your shell/deployment configuration when no longer needed. Never commit `.env`, keys, backups, local data or credentials.

## Workflow

1. Maintain customers, services, accounting firms, managers and workers in Directory. Admin and InternalUser accounts can edit names, active status, notes and the applicable contact/reference fields; primary keys and historical snapshots never change.
2. Create an engagement with service dates, fee, frequency and firm/manager/LCM percentages totaling 100%. After its first billing record is generated, its customer, service, accounting firm and manager are locked. End the old engagement and create a new one when any of those parties changes; amount, percentages, valid schedule settings, end date, status and notes remain editable for future billing. Admin and InternalUser accounts can also use Edit schedule from the engagement or Upcoming billing screens to change future frequency, next period start and anchor day.
3. Generate a period from Upcoming billing. The service period and work item are created together. The server accepts only a schedule-valid generation mode: recurring schedules use Scheduled or Replacement, One-Off uses the current scheduled/replacement period, and Ad-Hoc uses AdHocManual. Recurring generation advances the next period normally; a manual replacement for a recurring schedule must start at the current advised start, may use a different valid end date, then advances the next start to the day after that manual period and realigns the recurring anchor. One-off replacement completes the schedule, while Ad-Hoc periods remain manually dated and non-recurring. Every 2 Months, quarterly, half-yearly, yearly, one-off and ad-hoc schedules are supported. Month-end anchoring survives short months. Fees are not automatically prorated for shortened final periods.
4. Assign workers from the work item. A worker's percentage applies to that billing record's **LCM gross share**. Multiple active assignments may share the work, with combined percentages and rounded entitlements capped at LCM's share. A worker percentage may be **0%**; this is an active operational assignment with RM0.00 worker entitlement and no worker payment due.
5. Create invoice documents only in the Invoice module. A billing record can be split across several invoices, and a consolidated invoice can contain several records. An Accounting Firm → End Customer invoice must contain one firm and one end customer, although it may consolidate that customer's services and periods. Manager → Accounting Firm invoices require one manager and firm; LCM MGT → Manager invoices require one manager. Flow-specific limits use the immutable billing/share snapshots. Invoice numbers are unique per issuer and remain reserved after cancellation.
6. Use the View/Edit actions on billing records for staff-only corrections. Period dates must remain inside the engagement, cannot overlap another active period, and cannot be changed while active invoices, assignments or downstream payments exist. Customer billing amount may be corrected while active customer invoices have no receipts, provided it is not lower than their active allocations. Revenue-share base may be corrected only before active manager/LCM invoices, worker assignments or worker payments; the stored percentages are reapplied to produce new snapshots. Active receipts and all share/worker dependencies are enforced server-side for every staff role, including Admin, while operational notes and safe statuses remain correctable with version checks.
7. Use the View/Edit actions on invoices for staff-only corrections. Invoice flow and issuer/customer/manager header relationships remain fixed; invoice number/date and line membership/amounts can be corrected before any active receipt allocation. Blank or zero lines are removed and positive lines are added or adjusted within the flow cap. The service revalidates issuer uniqueness, grouping, totals, billing states and concurrency. Metadata remains editable after a receipt, but allocation changes are clearly blocked until the receipt is cancelled.
8. Allocate customer receipts only from the Invoice Receipt screen. One receipt may cover several customer invoices when every invoice has the same accounting firm and end customer. Partial receipts and several receipts against one invoice are supported; overpayment is rejected.
9. Record worker payments, allocating positive amounts to one or more outstanding assignments for that worker. Leave unused allocation rows blank; blank and zero rows are ignored. Entitlement and cash paid remain separate.
10. Staff can use the View/Edit actions on customer receipts and worker payments to correct dates and references with version checks. Existing customer receipt invoice membership remains fixed, but staff may correct each attached allocation amount in a serializable transaction; the receipt total is recalculated from those amounts and invoice outstanding balances exclude the receipt being corrected. To change receipt membership, reverse the receipt and create a replacement. Worker payment allocation rows remain immutable and use cancel-and-re-enter for allocation corrections.
11. Staff can edit an active worker assignment before it has an active payment allocation. Worker changes and percentage corrections recalculate entitlement from the immutable LCM gross snapshot and reject combined percentages or rounded entitlements above the available share. Workers remain read-only.
12. Corrections preserve CreatedAt/CreatedBy and advance UpdatedAt/UpdatedBy plus the version token. Once invoice or payment-derived billing status exists, a billing-record correction cannot forge it back to an operational status; cancel and recreate the dependent document when a financial correction is required.
13. Workers submit one Monday–Sunday progress report per active assignment and week. Reports record progress percentage, status, work completed, next action and blockers. Workers can submit missing past weeks and edit their current-week report with a version check; submitted past reports are read-only to workers. Admin and InternalUser can review and correct reports, while Manager can view reports for their managed work. AccountingFirm has no progress-report access.
14. Use reports and CSV export to review revenue shares, costs, retained amounts and balances. Date filters include overlapping **service periods**; a worker filter selects whole billing records and their full costs. Cancelled records remain visible but are excluded from headline totals.

Five roles are available:

- **Admin** has full system access. User administration and financial-record cancellation remain Admin-only.
- **InternalUser** has normal LCM operational access across customers, engagements, work, billing and payments, without user administration or cancellation authority.
- **AccountingFirm** is linked to exactly one accounting firm and can see only that firm's customers, engagements, customer billing, receipts and historical firm share. Manager, LCM and worker financial figures are excluded.
- **Manager** is linked to exactly one manager and can see only that manager's engagements, billing, manager share and related work progress. Receipt, LCM and worker financial figures are excluded.
- **Worker** is linked to exactly one worker and can see only active assignments and that worker's percentage, entitlement and payment history. Other parties, workers, billing pages and internal revenue shares are excluded.

Admin creates and maintains these entity links on the Users page. A role/link change updates the Identity security stamp and revokes the user's existing sessions. Invalid or missing external links fail authorization. No role physically deletes financial records. Cancel active receipts and assignments before cancelling billing; cancel active worker payments before cancelling their assignments. Master records can be deactivated. Users can change their own passwords.

Register controls include stable single/multiple-column sorting (Shift-click), column visibility, row limits, text selection/search, year/month/day date trees and amount operators. Applied filters are independent from draft changes; Enter applies search results. The row limit is a **client display limit**, not server pagination. Use date filters to narrow large billing/report queries.

## Financial and security rules

- Money uses C# `decimal` and PostgreSQL `numeric(18,2)`; percentages use `numeric(18,4)`. Financial arithmetic runs on the server.
- Money inputs must already have at most two decimal places. Worker entitlement rounds to cents using midpoint away from zero. Revenue shares use largest-remainder cent allocation so the three amounts always total the bill: floor exact shares to cents, then allocate residual cents by descending fractional remainder, with firm/manager/LCM order breaking ties. This avoids a negative residual for very small bills.
- Billing stores customer/service names, the customer billing amount, and an immutable revenue-share base plus party/percentage/amount snapshots. By default the customer billing amount and revenue-share base are both the Engagement billing amount, but generation and safe pre-dependency correction may set them independently. Revenue shares and worker entitlements use the revenue-share base snapshot; customer invoice caps use the customer billing amount snapshot. Assignments store worker name, agreed percentage, LCM gross and entitlement. Worker Assignment percentage may be 0%; a 0% assignment represents operational responsibility without worker profit-share entitlement. Its entitlement is RM0.00, it remains active and visible, and positive worker payment allocations are rejected. Master edits never recalculate historical snapshots. Engagement customer, service, accounting firm and manager relationships become immutable after billing exists so historical entity scope cannot move to another party. Invoice headers store the invoice number/date/flow, issuer and recipient snapshots, status, total, cancellation reason and audit fields. Invoice lines allocate immutable amounts to billing records; worker payments remain separate from invoices.
- The New Invoice table displays the selected flow's cap, active allocated amount and remaining balance per billing record. Customer, Manager and LCM flows are calculated separately from the customer amount, Manager share snapshot and LCM share snapshot respectively. Rows with no remaining amount for the selected flow are excluded from the grid count and hidden until another flow makes them eligible; allocation inputs are cleared whenever the user changes flow, while initial values and ordinary grid refreshes are preserved. Server-side allocation validation remains authoritative.
- Customer receipts are allocated against Accounting Firm → End Customer invoices. A receipt is restricted to one accounting firm and one end customer, may cover several invoices, and an invoice may be paid in parts; active allocation totals cannot exceed the invoice total. Staff can correct amounts on the existing receipt allocations with serializable version and overpayment validation, while changing invoice membership still requires cancellation and replacement. Receipt total always equals the sum of its active allocation amounts.
- Customer invoice headers store the end-customer relationship. Database indexes enforce invoice-number uniqueness for the actual issuer: accounting firm for customer invoices, manager for manager-issued invoices, and LCM MGT for LCM-issued invoices.
- Serializable transactions protect billing generation, assignments, receipts, allocations, schedule edits and cancellations. Concurrent conflicts return a refresh/retry message. Version tokens protect master, engagement and schedule edits; payment/receipt request IDs prevent duplicate submissions. A partial unique index blocks duplicate active engagement/period records; schedule changes and billing generation also reject overlapping periods.
- Login uses Identity password hashing, a 12-character password policy, five-attempt/15-minute account lockout, and per-IP login throttling. All application endpoints require authentication except login; static CSS/JS are public. Mutations require antiforgery tokens. Role changes and disable actions revoke existing sessions. Admin cannot disable/demote their own account or remove the final active admin.
- Entity access is enforced server-side by one scoped-query service using the user's linked accounting firm, manager or worker and the actual engagement/work relationships. `CreatedBy` remains audit metadata and is never treated as ownership. The same scope feeds dashboard totals, registers, invoice/receipt details, filters, reports, CSV exports and mutation lookups; an out-of-scope ID returns 404, while a forbidden role operation returns access denied. Workers have no invoice route or revenue-share visibility.
- External-role queries load only the shares, receipts, assignments and payment data needed for that role. Their pages and exports omit confidential foreign IDs and other parties' financial fields.
- Weekly progress access is scoped through the active WorkerAssignment and its Engagement relationships. Workers cannot update shared WorkItem status or notes; they submit progress reports instead. A database unique index prevents duplicate reports for one assignment and week, and serializable saves plus the Record version token protect concurrent edits. Cancelled assignments cannot receive new reports, and submitted report history is retained.
- Production cookies are Secure/HttpOnly; HTTPS redirection, HSTS, restrictive content policy and proxy trust are configured. Persist and protect the data-protection key volume. The database is accessed only by the application/migration service; no public database port is published in production.

## Tests and migrations

```powershell
dotnet build -c Release
# Create a separate disposable PostgreSQL database whose name ends in _test.
$env:BILLING_TEST_CONNECTION = 'Host=127.0.0.1;Port=55432;Database=billing_test;Username=billing;Password=YOUR_PASSWORD'
dotnet test -c Release --logger trx --results-directory artifacts/test-results
dotnet ef migrations add DescriptiveChange --project src/BillingControl --output-dir Data/Migrations
```

Integration tests **drop and recreate only the configured `_test` database**. They refuse other names and are explicitly skipped when the variable is absent. CI provisions PostgreSQL and runs them, then builds the Docker image. CI also runs the dependency-free JavaScript tests for invoice-flow eligibility, visible-row counts, empty state, input limits, flow-change clearing and preserved values during ordinary refreshes, plus receipt-total calculation. Tests cover allocation/rounding, blank and zero unused invoice/receipt/worker-payment rows, clear allocation validation, configured percentages, worker rates, historical snapshots, engagement party immutability, duplicate/overlapping periods, flow-specific allocation display, partial and consolidated invoices, customer/issuer/receipt grouping, issuer-scoped numbers, invoice/payment caps, receipt amount corrections, receipt membership locks, current-receipt exclusion from overpayment checks, cancellation recalculation, concurrent invoice/receipt/worker allocations, weekly progress uniqueness/week validation/concurrency/cancellation and worker-manager-firm scope, authentication, CSRF, all five roles, cross-entity ID tampering, scoped totals/reports/exports, financial-field redaction and session revocation after role/link changes. See `VALIDATION.md` for the checks completed in this workspace.

## Ubuntu 24.04 / Hostinger deployment

The repository supplies deployment configuration; it does not automatically deploy to your VPS.

1. Install Docker Engine and Compose, clone the repository on the VPS and check out the desired version.
2. Copy `.env.example` to `.env`, set strong credentials and `APP_HOST` to your DNS hostname. Restrict `.env` permissions: `chmod 600 .env`. Use a hex/alphanumeric database password to avoid connection-string separators. Bootstrap admin credentials must meet the password policy.
3. Obtain a valid certificate for that hostname. Place the certificate chain and private key at `deploy/tls/fullchain.pem` and `deploy/tls/privkey.pem`. Keep these files private and set up certificate renewal plus `docker compose exec nginx nginx -s reload`. Do not use the development HTTP endpoint over the internet.
4. Point DNS to the VPS. Allow HTTPS/HTTP and your existing restricted SSH access. PostgreSQL has **no host port** in `compose.yaml`. Confirm the fixed Docker subnet `172.30.88.0/24` does not conflict with existing networks. If changed, update both the Nginx static IP and `ReverseProxy__Address` together.
5. Run `docker compose config --quiet`, then `docker compose up -d --build`. The migration container must finish successfully before the app starts. Inspect `docker compose logs migrate app nginx` and sign in through HTTPS.
6. Verify the full flow using an explicitly marked test engagement before operational use. Back up before upgrades. For later releases, build images, stop `app nginx`, run `docker compose run --rm migrate`, then `docker compose up -d app nginx`. Keep `.env` and volumes intact.

The entity-scoped access migration renames the legacy `User` Identity role to `InternalUser` and adds nullable user links plus foreign keys and a database constraint preventing multiple entity links. The `InvoiceDocuments` migration creates invoice headers/lines and receipt allocations, copies every legacy BillingRecord invoice/date and receipt into document rows, preserves audit/cancellation metadata, then removes the old single-invoice columns. `InvoiceIssuerAndReceiptIntegrity` backfills the customer identity on existing customer invoices and replaces global invoice-number uniqueness with issuer-scoped indexes. `RevenueShareBaseAmount` adds the historical share-base snapshot, backfills existing rows from their customer billing amount, and then enforces a positive money value. `AllowZeroWorkerAssignmentPercent` permits active 0% operational assignments. `WeeklyWorkerProgressReporting` adds the assignment-linked Monday–Sunday report table, validation constraints and assignment/week unique index without changing financial history. It stops safely if an existing customer invoice contains more than one customer. Existing Admin and operational-user access is retained. After the migration, an Admin must assign the required single link before creating or converting an account to AccountingFirm, Manager or Worker. Take a database backup before applying these migrations in production.

If the VPS already uses ports 80/443 for another proxy, integrate this app with that existing proxy instead of starting a second listener. Keep the application private to the proxy network and trust only its actual address. Do not expose Kestrel or PostgreSQL publicly.

### Hostinger MCP deployment

Hostinger's project importer reads `docker-compose.yaml` from the repository's `master` branch. That file is designed for the existing Traefik proxy: it clones the selected `master` source into a temporary volume, builds the published app with the .NET SDK image, runs the migration once, and exposes only the app through the Traefik HTTPS router. Supply `POSTGRES_PASSWORD`, `ADMIN_EMAIL`, `ADMIN_PASSWORD` and `APP_HOST` as project environment variables. Keep `APP_HOST` on a DNS A record pointing at the VPS before starting the project so Traefik can obtain the certificate.

## Backup and restore

```bash
bash deploy/backup.sh
# Copy the resulting backups/billing-<UTC timestamp>.dump to protected off-server storage.
# Before a restore, make a fresh backup. Restore replaces the current billing database:
bash deploy/restore.sh /absolute/path/billing-backup.dump --confirm-replace
```

`backup.sh` creates a PostgreSQL custom-format dump and checks its archive directory. Run daily via cron and monitor failures; a valid archive listing is not a restore test. Periodically restore into an isolated database and verify counts/balances and login. Back up the `data_keys` volume and securely preserve deployment configuration separately. Restore stops the app/proxy; on failure it leaves them stopped so the error can be investigated. Never use `docker compose down -v` on operational data.

## Project layout

`src/BillingControl/Models` contains entities and form models; `Data` contains the Identity/EF context, seed routine and migration; `Services` contains monetary and transaction rules; `Controllers` and `Views` provide MVC screens. `tests` contains unit and real PostgreSQL integration tests. `deploy` contains the reverse proxy and database maintenance scripts.

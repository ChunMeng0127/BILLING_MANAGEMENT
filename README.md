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

1. Maintain customers, services, accounting firms, managers and workers in Directory.
2. Create an engagement with service dates, fee, frequency and firm/manager/LCM percentages totaling 100%.
3. Generate a period from Upcoming billing. The service period and work item are created together. Recurring generation advances the next period; manual/replacement generation does not. Every 2 Months, quarterly, half-yearly, yearly, one-off and ad-hoc schedules are supported. Month-end anchoring survives short months. Fees are not automatically prorated for shortened final periods.
4. Assign workers from the work item. A worker's percentage applies to that billing record's **LCM gross share**. Multiple active assignments may share the work, with combined percentages and rounded entitlements capped at LCM's share.
5. Mark billing as Billed with an invoice reference and billing date. Record partial or full customer receipts. Paid and Partially Paid are calculated from receipts. Work progress and billing workflow status are maintained separately.
6. Record worker payments, allocating positive amounts to one or more outstanding assignments for that worker. Entitlement and cash paid remain separate.
7. Use reports and CSV export to review revenue shares, costs, retained amounts and balances. Date filters include overlapping **service periods**; a worker filter selects whole billing records and their full costs. Cancelled records remain visible but are excluded from headline totals.

Admin can create/disable users, change roles, reset passwords/unlock accounts, and cancel financial records with reasons. Users can maintain operational data. Neither role physically deletes financial records. Cancel active receipts and assignments before cancelling billing; cancel active worker payments before cancelling their assignments. Master records can be deactivated. Users can change their own passwords.

Register controls include stable single/multiple-column sorting (Shift-click), column visibility, row limits, text selection/search, year/month/day date trees and amount operators. Applied filters are independent from draft changes; Enter applies search results. The row limit is a **client display limit**, not server pagination. Use date filters to narrow large billing/report queries.

## Financial and security rules

- Money uses C# `decimal` and PostgreSQL `numeric(18,2)`; percentages use `numeric(18,4)`. Financial arithmetic runs on the server.
- Money inputs must already have at most two decimal places. Worker entitlement rounds to cents using midpoint away from zero. Revenue shares use largest-remainder cent allocation so the three amounts always total the bill: floor exact shares to cents, then allocate residual cents by descending fractional remainder, with firm/manager/LCM order breaking ties. This avoids a negative residual for very small bills.
- Billing stores customer/service names and party/percentage/amount snapshots. Assignments store worker name, agreed percentage, LCM gross and entitlement. Master edits never recalculate historical snapshots.
- Serializable transactions protect billing generation, assignments, receipts, allocations and cancellations. Concurrent conflicts return a refresh/retry message. Version tokens protect normal edits; payment/receipt request IDs prevent duplicate submissions. A partial unique index blocks duplicate active engagement/period records; the service also rejects overlapping periods.
- Login uses Identity password hashing, a 12-character password policy, five-attempt/15-minute account lockout, and per-IP login throttling. All application endpoints require authentication except login; static CSS/JS are public. Mutations require antiforgery tokens. Role changes and disable actions revoke existing sessions. Admin cannot disable/demote their own account or remove the final active admin.
- Production cookies are Secure/HttpOnly; HTTPS redirection, HSTS, restrictive content policy and proxy trust are configured. Persist and protect the data-protection key volume. The database is accessed only by the application/migration service; no public database port is published in production.

## Tests and migrations

```powershell
dotnet build -c Release
# Create a separate disposable PostgreSQL database whose name ends in _test.
$env:BILLING_TEST_CONNECTION = 'Host=127.0.0.1;Port=55432;Database=billing_test;Username=billing;Password=YOUR_PASSWORD'
dotnet test -c Release --logger trx --results-directory artifacts/test-results
dotnet ef migrations add DescriptiveChange --project src/BillingControl --output-dir Data/Migrations
```

Integration tests **drop and recreate only the configured `_test` database**. They refuse other names and are explicitly skipped when the variable is absent. CI provisions PostgreSQL and runs them, then builds the Docker image. Tests cover allocation/rounding, configured percentages, worker rates, historical snapshots, duplicate/overlapping periods, partial/multiple-assignment payments, cancellation, wrong-worker/overpayment rejection, concurrent payments, authentication, CSRF and Admin access. See `VALIDATION.md` for the checks completed in this workspace.

## Ubuntu 24.04 / Hostinger deployment

The repository supplies deployment configuration; it does not automatically deploy to your VPS.

1. Install Docker Engine and Compose, clone the repository on the VPS and check out the desired version.
2. Copy `.env.example` to `.env`, set strong credentials and `APP_HOST` to your DNS hostname. Restrict `.env` permissions: `chmod 600 .env`. Use a hex/alphanumeric database password to avoid connection-string separators. Bootstrap admin credentials must meet the password policy.
3. Obtain a valid certificate for that hostname. Place the certificate chain and private key at `deploy/tls/fullchain.pem` and `deploy/tls/privkey.pem`. Keep these files private and set up certificate renewal plus `docker compose exec nginx nginx -s reload`. Do not use the development HTTP endpoint over the internet.
4. Point DNS to the VPS. Allow HTTPS/HTTP and your existing restricted SSH access. PostgreSQL has **no host port** in `compose.yaml`. Confirm the fixed Docker subnet `172.30.88.0/24` does not conflict with existing networks. If changed, update both the Nginx static IP and `ReverseProxy__Address` together.
5. Run `docker compose config --quiet`, then `docker compose up -d --build`. The migration container must finish successfully before the app starts. Inspect `docker compose logs migrate app nginx` and sign in through HTTPS.
6. Verify the full flow using an explicitly marked test engagement before operational use. Back up before upgrades. For later releases, build images, stop `app nginx`, run `docker compose run --rm migrate`, then `docker compose up -d app nginx`. Keep `.env` and volumes intact.

If the VPS already uses ports 80/443 for another proxy, integrate this app with that existing proxy instead of starting a second listener. Keep the application private to the proxy network and trust only its actual address. Do not expose Kestrel or PostgreSQL publicly.

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

# Validation — 6 September 2026

## Completed locally

- .NET 10 Release build: **0 errors, 0 warnings**.
- Automated tests: **22 passed, 0 failed, 0 skipped**, using real PostgreSQL 17.6. Test report: `artifacts/test-results/results.trx` (local, ignored by Git).
- Applied the committed EF migration to a fresh PostgreSQL database and seeded the initial Identity roles/admin and optional sample master records.
- Verified allocation percentages, rounding, four worker-share rates, duplicate and overlapping service-period rejection, partial/multiple-assignment payments, historical snapshots after editing engagement percentages, cancellation rules, wrong-worker rejection and concurrent overpayment protection.
- Verified login redirects across application pages, CSRF rejection, safe login return URLs, Admin access, failed-login lockout and disabled-session revocation.
- Browser walkthrough: generated sample September billing; posted with an invoice date/reference; recorded a RM250 customer receipt; assigned a worker at 70% of LCM's share; recorded a RM100 worker payment. Dashboard showed RM1,000 billing, RM350 firm, RM250 manager, RM400 LCM gross, RM280 worker entitlement, RM120 retained, RM750 customer outstanding and RM180 worker outstanding.
- Browser checks: text search with Enter and empty results, year/month/day date filter display and month search, inclusive amount ranges with reversed bounds, Cancel preserving filters, Clear filters, column visibility and multiple-column sort indicators. No browser console errors were captured.
- Responsive register checked at 390×844: page width remained 390px; wide table content scrolled within its 360px container. Desktop layout was also inspected.
- PostgreSQL custom-format backup restored successfully into a separate `billing_restore_test` database. Restored user count (1), billing (RM1,000), worker entitlement (RM280) and payments (RM100) matched the source demo.
- JavaScript syntax check and Compose/CI YAML parsing passed.

## Deployment verification

Docker is not installed on this Windows PC, so the local Docker image and Nginx/TLS stack were not executed here. GitHub CI is configured to build the image and run the same PostgreSQL integration tests. Production HTTPS, certificate renewal, VPS proxy/network compatibility and the deployed end-to-end workflow must be checked during deployment. No Hostinger infrastructure was changed.

The local database contains explicitly marked demonstration records. Credentials, database binaries/data, backups, key material and build outputs are excluded from Git.

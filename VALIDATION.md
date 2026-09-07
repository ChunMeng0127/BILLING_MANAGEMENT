# Validation — 7 September 2026

## Completed locally

- .NET 10 Release build: **0 errors, 0 warnings**.
- Automated tests: **26 passed, 0 failed, 0 skipped**, using real PostgreSQL 17.6. Test report: `artifacts/test-results/invoice-integrity-results.trx` (local, ignored by Git).
- Applied the EF migrations through `InvoiceDocuments` to a fresh PostgreSQL database and seeded the initial Identity roles/admin and optional sample master records.
- Verified allocation percentages, rounding, four worker-share rates, duplicate and overlapping service-period rejection, invoice splits/consolidation/partial allocation and all three flow caps, partial/multiple-assignment payments, historical snapshots after editing engagement percentages, cancellation rules and wrong-worker rejection.
- Verified same-customer customer-invoice consolidation is allowed while mixed customers are rejected; Manager and LCM flows retain their permitted consolidation. Verified invoice numbers can be reused by different issuers and are rejected for the same issuer.
- Verified one receipt can cover several invoices for the same firm/customer, while mixed-customer and mixed-firm receipts and overpayments are rejected. Invoice and receipt cancellation recalculate the derived states.
- Verified concurrent worker payments, invoice allocations and receipt allocations preserve their caps. PostgreSQL serialization/deadlock exceptions, including wrapped `40001`, roll back and return the normal refresh/retry business error without automatic retry.
- Verified legacy invoice/date and receipt data migrates into Invoice, InvoiceLine and CustomerReceiptAllocation rows without changing the historical amount or removing audit/cancellation data.
- Verified login redirects across application pages, CSRF rejection, safe login return URLs, Admin access, failed-login lockout and disabled-session revocation.
- Verified AccountingFirm A cannot read AccountingFirm B data, Manager A cannot read Manager B data, and Worker A cannot read or mutate Worker B assignments or payments, including manually substituted URL/form IDs.
- Verified dashboard totals, lists, reports and CSV exports use the same entity scope. External detail pages and exports omit other-party shares, LCM gross/retained amounts, worker costs and unrelated receipt/payment data according to role.
- Verified Admin retains full access, InternalUser retains LCM operational access without user administration/cancellation, invalid role/link combinations are rejected, and changing a role or entity link revokes the existing session.
- Browser walkthrough: generated sample September billing; created the customer invoice document; allocated a RM250 customer receipt; assigned a worker at 70% of LCM's share; recorded a RM100 worker payment. Dashboard showed RM1,000 billing, RM350 firm, RM250 manager, RM400 LCM gross, RM280 worker entitlement, RM120 retained, RM750 customer outstanding and RM180 worker outstanding.
- Browser checks: text search with Enter and empty results, year/month/day date filter display and month search, inclusive amount ranges with reversed bounds, Cancel preserving filters, Clear filters, column visibility and multiple-column sort indicators. No browser console errors were captured.
- Responsive register checked at 390×844: page width remained 390px; wide table content scrolled within its 360px container. Desktop layout was also inspected.
- PostgreSQL custom-format backup restored successfully into a separate `billing_restore_test` database. Restored user count (1), billing (RM1,000), worker entitlement (RM280) and payments (RM100) matched the source demo.
- JavaScript syntax check and CI YAML parsing passed. Docker is unavailable on this Windows PC; the production image and Compose smoke test are run by GitHub Actions.

## Deployment verification

Docker is not installed on this Windows PC, so the local Docker image and Nginx/TLS stack were not executed here. GitHub CI is configured to build the image and run the same PostgreSQL integration tests. Hostinger MCP project update action **113460048** completed successfully for `billing-control` on VM `1910481`: the builder exited 0, the migration container exited 0, the application is running, and PostgreSQL is healthy. `https://billing.lcmmgt.com/Account/Login` returned 200 over HTTPS; the protected `/Invoices` route returned the expected unauthenticated redirect. Production certificate renewal and the authenticated end-to-end invoice workflow should still be exercised by an operator with the production account.

The local database contains explicitly marked demonstration records. Credentials, database binaries/data, backups, key material and build outputs are excluded from Git.

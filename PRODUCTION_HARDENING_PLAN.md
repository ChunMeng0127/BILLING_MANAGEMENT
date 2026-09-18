# Production Hardening & Recovery Plan

**System:** Billing Management / Billing Control  
**Scope:** Currently deployed production version only  
**Baseline application SHA:** `6677680feb12141eefe97dd5d21b37fd3b6b4bf1`  
**Plan created:** 2026-09-17  
**Execution principle:** Correctness → Data Safety → Security → Maintainability → Simplicity

---

## 1. Purpose

This document is the execution baseline for hardening the currently deployed Billing Management production system.

It intentionally excludes the in-progress development branch and future WhatsApp / document collection work. Development features must not be merged into production as part of this plan.

The production accounting core is currently treated as stable. The objective is to improve operational safety around it without unnecessary redesign of billing, invoicing, receipt allocation, revenue sharing, worker entitlement, or payment logic.

---

## 2. Production Baseline

Current deployed application source:

`6677680feb12141eefe97dd5d21b37fd3b6b4bf1` — `Redesign Billing Control interface`

Observed production topology:

- ASP.NET Core / .NET 10 application
- PostgreSQL 17
- Docker Compose
- Traefik reverse proxy / TLS
- Persistent PostgreSQL volume
- Persistent ASP.NET Data Protection keys
- Database not directly exposed to the public Internet

Current production database audit snapshot:

| Item | Records |
| --- | ---: |
| Customers | 15 |
| Engagements | 18 |
| Billing Records | 64 |
| Invoices | 33 |
| Customer Receipts | 19 |
| Worker Assignments | 51 |
| Weekly Progress Reports | 25 |
| Worker Payments | 0 |

The 2026-09-17 read-only integrity audit found **zero violations** for the following checks:

- overlapping active billing periods
- revenue-share snapshot totals
- invoice total vs invoice lines
- receipt total vs receipt allocations
- worker payment total vs allocations
- invoice overpayment
- worker overpayment
- worker assignment percentage / entitlement caps
- invoice flow allocation caps
- cancelled billing with active invoices
- cancelled billing with active assignments
- cancelled invoice with active receipts
- cancelled assignment with active payments
- inconsistent user role/entity scope

This is the starting baseline. Any hardening work must preserve these results.

---

## 3. Existing Strengths That Must Not Regress

The current production application already has important controls that must be preserved:

- PostgreSQL `numeric` precision for money and percentage values
- database check constraints for financial and percentage validity
- deterministic cent allocation for revenue sharing
- unique request identifiers for duplicate protection
- optimistic concurrency tokens
- serializable transactions for financial operations
- restricted physical deletion of financial/history records
- immutable / guarded historical snapshots
- scoped user access by role and linked entity
- secure cookies
- anti-forgery protection
- login rate limiting and lockout
- HSTS and HTTPS enforcement
- CSP, clickjacking protection and no-sniff headers
- persistent Data Protection keys

No hardening phase may weaken these controls.

---

## 4. Audit Findings and Priority

| Priority | Finding | Current Status |
| --- | --- | --- |
| P0 | Current production database backup | Verified on 2026-09-17 |
| P0 | Production financial data integrity | No violation found |
| P1 | Automated + offsite backup | Not implemented |
| P1 | Runtime DB login is PostgreSQL superuser | Must be corrected |
| P1 | Production deploy follows mutable `master` | Must be corrected |
| P1 | `master` has no required branch protection/checks | Must be corrected |
| P1 | SSH root/password access and host firewall posture | Must be hardened carefully |
| P2 | CI topology differs from actual Traefik production topology | Must be aligned |
| P2 | Application container has no healthcheck | Must be added |
| P2 | Monitoring / alerting is limited | Must be improved |
| P2 | Application process runs as root | Should be hardened |
| P2 | Three date-dependent integration tests currently fail | Must be fixed |
| P3 | Generalized before/after financial correction audit trail | Future improvement |
| P3 | Antiforgery warning log noise | Cleanup only |

---

# 5. Execution Phases

## A1 — Backup & Disaster Recovery

### Objective

Ensure production data can be recovered even if the VPS, Docker volumes, deployment directory, or local backups are lost.

### Required work

1. Create an automated daily PostgreSQL backup job.
2. Use PostgreSQL custom-format dumps (`pg_dump -Fc`).
3. Validate every generated dump.
4. Apply a documented retention policy.
5. Copy backups to storage outside the production VPS.
6. Protect backup files with owner-only permissions.
7. Record backup success/failure in logs.
8. Add failure notification where practical.
9. Create a restore procedure matching the real Traefik production topology.
10. Periodically restore a backup into an isolated PostgreSQL instance and verify migrations/tables.

### Recommended retention

Initial practical baseline:

- Daily: 14 days
- Weekly: 8 weeks
- Monthly: 12 months

Retention can be adjusted later based on storage and business requirements.

### Existing verified recovery point

Backup created on 2026-09-17:

`billing-20260917T032100Z.dump`

SHA-256:

`653f49fa61bd85de156c6e94f87b6b7adf4a366ca9a6abd3293c163bafcddc82`

The dump was restored into an isolated temporary PostgreSQL instance successfully:

- restore: PASS
- public tables: 28
- EF migrations: 9

### Acceptance criteria

A1 is complete only when:

- a fresh backup is created automatically without manual action
- failure is detectable
- backup files are not world-readable
- at least one copy exists outside the production VPS
- a documented restore test succeeds using the actual production-compatible procedure
- production DB is never used as the restore test target

### Rollback / safety

Backup automation must not delete existing backups until retention logic has been independently verified.

---

## A2 — Database Least Privilege

### Objective

Remove PostgreSQL superuser privileges from the normal website runtime connection.

### Current risk

The production application currently connects using PostgreSQL role `billing`, which is also the cluster bootstrap/superuser role.

Observed privileges:

- superuser: true
- createdb: true
- createrole: true
- replication: true

This creates an unnecessarily large blast radius if application credentials or the application process are compromised.

### Target model

Separate database identities:

**Migration / administration role**

- used only for schema migration and controlled administration
- retains permissions required for migrations
- not used by the running website

**Runtime application role**

- login allowed
- access only to the Billing application database/schema
- only required table/sequence permissions
- no superuser
- no createdb
- no createrole
- no replication

### Required work

1. Design runtime grants from actual EF/application behavior.
2. Create the runtime DB role in a controlled test environment first.
3. Run the full integration suite using that restricted role.
4. Verify normal CRUD, invoice, receipt, billing, worker and progress workflows.
5. Keep migration credentials separate from runtime credentials.
6. Update production configuration only after tests pass.
7. Verify application startup and smoke tests after cutover.

### Acceptance criteria

- website runs successfully using a non-superuser role
- migrations still run through the dedicated migration/admin path
- runtime role cannot create databases or roles
- all financial regression tests pass
- production integrity audit remains clean

### Rollback

Keep the existing DB credential available during the cutover window so the previous connection can be restored immediately if the restricted role is missing a required grant.

---

## A3 — Immutable Production Deployment

### Objective

Make every production release reproducible and traceable to one exact source revision.

### Current risk

The live deployment currently clones the latest `master` branch during deployment. `master` is mutable and currently has no required protection/checks.

A redeployment on two different days can therefore produce different application code even when using the same server procedure.

### Target release flow

```text
Approved release SHA / immutable image
        ↓
CI PASS
        ↓
Pre-deploy DB backup
        ↓
Migration
        ↓
Deploy exact release
        ↓
Application health check
        ↓
Smoke test
        ↓
Record deployed SHA
```

### Required work

1. Stop production deployment from implicitly following latest `master`.
2. Deploy an explicit commit SHA, release tag or immutable image digest.
3. Add branch protection / repository rules for the production branch where supported.
4. Require relevant CI checks before production promotion.
5. Record release SHA in deployment metadata or an accessible runtime/version endpoint.
6. Document rollback to the previous application release.
7. Ensure a DB backup occurs before migrations.

### Acceptance criteria

At any time it must be possible to answer:

> What exact commit/image is production running?

The same release identifier must reproduce the same application artifact.

### Rollback

Application rollback and database rollback are separate decisions. Never automatically restore a database merely because application code is rolled back. Database restoration is permitted only when migration compatibility and data consequences have been reviewed.

---

## A4 — CI, Health Checks & Monitoring

### Objective

Make automated validation represent the real production environment more closely and detect failures quickly.

### Current gap

CI currently validates a Compose topology using nginx, while the VPS production environment uses Traefik and a different deployment Compose configuration.

The PostgreSQL container has a healthcheck, but the Billing application container does not.

### Required work

1. Align CI smoke testing with the actual production deployment topology where practical.
2. Add an application health endpoint that does not expose sensitive data.
3. Add Docker application healthcheck.
4. Healthcheck should verify application availability and, where safe, database connectivity.
5. Add deployment smoke test for HTTPS/login route.
6. Monitor application restart state and unhealthy containers.
7. Monitor backup job success/failure.
8. Surface repeated HTTP 5xx / database connection failures.
9. Reduce known non-actionable warning noise after functional monitoring is established.

### Acceptance criteria

- unhealthy application is detectable without manual browser testing
- CI validates the real production path closely enough to catch configuration drift
- failed backups are visible
- deployment finishes with an explicit smoke-test result

---

## A5 — VPS / Host Hardening

### Objective

Reduce host-level compromise risk without locking out legitimate administration or disrupting other services on the VPS.

### Current observations

- SSH root login enabled
- SSH password authentication enabled
- SSH public-key authentication enabled
- UFW inactive
- fail2ban inactive
- system reports reboot required
- Billing PostgreSQL is not publicly exposed
- Billing application currently runs as root inside its container
- another non-Billing service currently exposes TCP port 32769 publicly; ownership/need must be confirmed before changing it

### Mandatory safe sequence

Do **not** disable root/password SSH first.

Use this sequence:

1. Create/confirm a non-root administrative user.
2. Configure SSH key authentication for that user.
3. Verify a second independent SSH session works.
4. Verify sudo/root escalation works.
5. Inventory every publicly required port/service.
6. Configure firewall rules without breaking required services.
7. Enable brute-force protection where appropriate.
8. Only then disable direct root/password SSH as appropriate.
9. Plan and perform required host reboot in a maintenance window.
10. Verify all production services after reboot.

### Container hardening

Assess running the Billing application as a non-root user and using additional container restrictions where compatible.

### Acceptance criteria

- tested non-root administrative access exists
- unnecessary public ports are closed
- firewall policy is active and documented
- SSH exposure is reduced without losing administrative access
- Billing application continues to operate normally
- all containers recover correctly after a controlled reboot

---

## A6 — Code Maintenance & Auditability

### Objective

Remove known maintenance debt after the infrastructure risk is under control.

### Required work

1. Fix the three date-dependent integration tests.
2. Ensure test audit timestamps and business clock use a consistent controllable time source.
3. Keep tests deterministic regardless of the actual calendar date.
4. Keep all existing financial/data-integrity regression coverage.
5. Evaluate a generalized before/after audit ledger for sensitive financial corrections.
6. Reduce known antiforgery warning noise if it can be done without weakening cache protection.

### Current test baseline

Deployed SHA isolated test result on 2026-09-17:

- build: PASS, 0 warnings, 0 errors
- JavaScript: 12/12 PASS
- .NET integration tests: 48 PASS / 3 FAIL

The three failures are currently associated with fixed test business time versus real `DateTime.UtcNow` audit timestamps in current-week workflow tests.

### Acceptance criteria

- complete automated test suite passes independently of current real-world date
- no financial test is removed merely to obtain a green build
- any new financial correction audit mechanism has tests and migration/rollback planning

---

# 6. Execution Order

Use this order unless a new critical issue is discovered:

1. **A1 — Backup & Disaster Recovery**
2. **A2 — Database Least Privilege**
3. **A3 — Immutable Production Deployment**
4. **A4 — CI, Health Checks & Monitoring**
5. **A5 — VPS / Host Hardening**
6. **A6 — Code Maintenance & Auditability**

A1, A2 and A3 are the highest-priority production hardening work.

---

# 7. Change-Control Rules

The following rules apply throughout this plan:

1. Never use production data as a disposable test target.
2. Take and validate a current backup before database migrations or privilege changes.
3. Never perform destructive restore testing against the live production database.
4. Financial logic changes require regression testing and explicit review.
5. DB privilege changes require a tested rollback path.
6. SSH/firewall changes require verified alternate access before restrictive changes.
7. Production releases must be tied to an exact immutable revision.
8. Do not mix in-progress development/WhatsApp features into production hardening.
9. Avoid unrelated refactoring.
10. After every production change, verify application health, HTTPS, login, database connectivity and relevant business workflows.

---

# 8. Phase Tracking

| Phase | Status | Notes |
| --- | --- | --- |
| Production baseline audit | ✅ Complete | Data-integrity audit clean |
| Verified current backup | ✅ Complete | 2026-09-17 dump restored successfully in isolation |
| Sensitive local file permissions | ✅ Complete | Current `.env` and known local DB dumps restricted to owner-only |
| A1 — Backup & Disaster Recovery | ✅ Complete | Automated local + Microsoft 365 offsite backup, retention and isolated restore verification are active |
| A2 — Database Least Privilege | ✅ Complete | Runtime, migration, backup and break-glass roles separated; bootstrap superuser locked NOLOGIN |
| A3 — Immutable Deployment | ✅ Production complete | Exact-SHA deterministic releases active; GitHub `master` protection requires repository-admin follow-up |
| A4 — CI / Health / Monitoring | ✅ Complete | Traefik-aligned CI, app readiness/Docker health, deployment smoke and 5-minute production monitor are active |
| A5 — VPS Hardening | Not started | Must use controlled lockout-safe sequence |
| A6 — Code Maintenance | 🚧 In progress | A6.1 date-dependent tests fixed; remaining maintenance pending |

---

# 9. Definition of Production Hardening Complete

This plan is complete when all of the following are true:

- daily backups run automatically
- backups have tested off-VPS recovery copies
- restore procedure is documented and periodically verified
- running website does not use a PostgreSQL superuser
- deployment uses an immutable release identifier
- production branch/release path is protected by appropriate checks
- application health is automatically detectable
- CI materially represents the actual production deployment path
- host administrative access is hardened without operational lockout risk
- deterministic automated tests are green
- production data-integrity audit remains clean after all changes

Until then, production should remain on the stable deployed financial core and development features should remain isolated.
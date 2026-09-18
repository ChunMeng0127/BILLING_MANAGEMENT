# A6 Code Maintenance & Auditability Execution

Status: COMPLETE; merge pending

## Scope

A6 closes the maintenance items identified during the production hardening review without
changing billing, revenue-share, invoice, receipt, worker-entitlement, payment or status
calculation rules.

No database migration is introduced by A6.

## Deterministic audit timestamps

### Previous state

Weekly-progress business logic already used the injectable application-wide
`TimeProvider` through `BusinessClock`.

`AppDbContext.SaveChangesAsync`, however, stamped `CreatedAt` and `UpdatedAt` with
`DateTime.UtcNow` directly.

This meant tests could freeze business time while audit fields continued to use real wall
clock time. Three progress tests previously needed a test-only SQL correction of assignment
audit timestamps to avoid calendar-sensitive failures.

### A6 change

`AppDbContext` now accepts the same injectable `TimeProvider` and obtains one UTC
timestamp per save operation with `GetUtcNow()`.

Production keeps `TimeProvider.System`, so production semantics remain current UTC time.

Tests may inject `FixedProgressTime` into both `BusinessClock` and `AppDbContext`.
The progress test suite now creates its test database context with that frozen provider
instead of rewriting `CreatedAt` / `UpdatedAt` through direct SQL.

A focused regression assertion verifies that a newly-created worker assignment receives
the frozen `CreatedAt` and `UpdatedAt` values.

Benefits:

- business time and audit time can be controlled by the same test clock
- deterministic tests no longer depend on the real calendar date
- no production timezone or financial-date convention changes
- no schema change

## Antiforgery warning noise

### Root cause

The global security-header middleware wrote:

`Cache-Control: no-store`

before MVC/antiforgery generated form tokens.

ASP.NET Core antiforgery intentionally applies its own stronger anti-cache headers to
responses containing antiforgery tokens. Because a Cache-Control header was already
present, the framework replaced it and logged the non-actionable warning about overridden
Cache-Control / Pragma headers.

### A6 change

The global middleware now registers an `OnStarting` callback and adds
`Cache-Control: no-store` only when no component has already supplied Cache-Control.

Therefore:

- antiforgery remains enabled globally for mutation endpoints
- antiforgery may retain its own `no-cache, no-store` response policy
- other responses still receive the application's default `no-store`
- CSRF protection and cache protection are not weakened

## Financial correction audit-ledger evaluation

A6 reviewed the currently deployed sensitive correction paths:

1. billing record corrections, including bounded billing amount / revenue-share base changes
2. invoice header and allocation corrections
3. customer receipt metadata and same-membership allocation amount corrections
4. worker assignment worker / percentage corrections
5. worker payment metadata corrections

### Existing controls

The current financial core already provides substantial correction safety:

- staff-only correction routes
- optimistic `Version` checks
- serializable financial transactions
- immutable `CreatedAt` / `CreatedBy`
- updated `UpdatedAt` / `UpdatedBy`
- guarded historical snapshots in `AppDbContext`
- explicit temporary scopes for the small set of normally-immutable fields that a vetted
  correction path is allowed to change
- invoice / receipt / payment / assignment dependency locks
- active allocation and overpayment caps
- cancellation rather than physical deletion
- cancellation reasons
- immutable worker-payment allocation correction policy (cancel and replace)
- immutable receipt membership correction policy (cancel and replace to change membership)
- deterministic financial regression coverage

### Remaining audit gap

After a permitted in-place correction, the database records the current values and who/when
updated the entity, but it does not provide a generalized stored copy of every previous
financial value.

A before/after correction ledger would improve forensic and accounting traceability.

### Decision

Do **not** add a generalized audit table inside A6 production hardening.

Reasons:

- the item was classified P3 / future improvement in the original hardening review
- the production integrity audit is clean
- current correction guards prevent unrestricted historical mutation
- a generic EF-level change interceptor would also capture derived status recalculations
  and other implementation noise, making the ledger less useful
- introducing an append-only financial history table requires a schema migration, retention
  rules, access policy, redaction policy and dedicated regression/rollback planning
- mixing that larger feature into the final maintenance phase would increase production risk
  without resolving a current integrity defect

### Recommended future design

Implement a separate, reviewed enhancement using an explicit **service-level append-only
FinancialCorrectionAudit** rather than a generic catch-all change tracker.

Recommended fields:

- audit ID
- entity type
- entity ID
- correction type
- actor user ID
- UTC occurrence timestamp
- reason / operator note where applicable
- canonical before snapshot
- canonical after snapshot
- optional request/correlation ID

Recommended boundaries:

- emit only for deliberate user correction operations
- write the audit row in the same database transaction as the correction
- prohibit update/delete of audit rows in normal application paths
- restrict detailed audit visibility to authorized staff/admin roles
- avoid storing secrets or unrelated personal data
- use an additive migration with no historical backfill claim
- include explicit tests for atomicity, immutability, concurrency and rollback behaviour

This enhancement is not a prerequisite for closing the current production hardening plan.

## Final validation

GitHub Actions run `35325899339` completed successfully.

- JavaScript: 12/12 PASS
- Release build: PASS, 0 warnings / 0 errors
- PostgreSQL-backed .NET: 51/51 PASS, 0 failed, 0 skipped
- Docker image build: PASS
- production Compose / Traefik HTTPS smoke: PASS
- health/login/authenticated production-path smoke: PASS
- antiforgery / Cache-Control override warning in CI log: none

## A6 acceptance

A6 satisfies:

- deterministic full test suite independent of real-world date
- all existing financial/data-integrity regression coverage retained
- production financial behaviour unchanged
- antiforgery protection retained while warning noise is removed
- financial before/after ledger evaluated and deliberately separated into future change control
- no migration required for A6

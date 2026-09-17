# A1 — Backup & Disaster Recovery Execution Record

**Date:** 2026-09-17  
**Scope:** Production Billing Control only  
**Production application SHA:** `6677680feb12141eefe97dd5d21b37fd3b6b4bf1`

## Implemented

- Daily PostgreSQL custom-format (`pg_dump -Fc`) backup automation.
- Atomic `.partial` → final backup creation.
- `pg_restore --list` validation for every backup.
- SHA-256 sidecar generation and verification.
- Owner-only (`0600`) backup/status/config permissions.
- Advisory locking to prevent overlapping backup jobs.
- Daily/weekly/monthly retention: 14 daily, 8 weekly, 12 monthly.
- Success/failure status files and system journal logging.
- Production-compatible restore procedure with emergency pre-restore backup.
- Isolated PostgreSQL 17 restore verification.
- Daily backup and weekly restore-verification systemd timers.
- `rclone` direct offsite support with remote read-back SHA-256 verification.
- rclone offsite retention using the same 14/8/12 policy.
- Microsoft 365 Business OneDrive connected as the production off-VPS target.

## Production validation

Final A1 production backup validation on 2026-09-17:

- backup service: PASS
- backup file: `billing-20260917T081413Z.dump`
- bytes: `108090`
- SHA-256: `6731707fe2d041a1a715b7bd622e592ce3cfb93aee712e948709a6875e4aae76`
- offsite destination: Microsoft 365 Business OneDrive
- offsite path: `BillingControl-Backups`
- offsite upload: PASS
- remote read-back SHA-256: PASS
- resulting status: `rclone-copied-verified`
- isolated restore: PASS
- restored public tables: 28
- EF migration rows: 9
- production PostgreSQL container: healthy
- production application container: running without restart

The production backup job now has `OFFSITE_REQUIRED=1`. A failed Microsoft 365 upload, unavailable rclone remote, or read-back checksum mismatch causes the job to fail instead of silently reporting success.

## Retention validation

Local retention was independently tested with synthetic backup sets in both dry-run and apply modes.

The rclone retention path was also tested independently against an isolated rclone target:

- synthetic backups: 9
- configured test policy: 2 daily / 2 weekly / 2 monthly
- expired backups deleted: 7
- remaining backups: 2
- matching `.sha256` sidecars preserved/deleted correctly
- orphan sidecars: 0
- unrelated marker file preserved: PASS

Production offsite retention uses the standard 14 daily / 8 weekly / 12 monthly policy.

## Failure detection validation

A forced offsite failure was tested using a nonexistent rclone remote and isolated temporary status/backup directories.

- backup command exit code: non-zero as expected
- `status=failed` written: PASS
- production status files were not modified by the failure test
- temporary test data was removed afterwards

## Active schedules

- Daily backup: approximately 02:30 Asia/Kuala_Lumpur, with up to 10 minutes randomized delay.
- Weekly isolated restore verification: Sunday approximately 03:30 Asia/Kuala_Lumpur, with up to 15 minutes randomized delay.
- Both timers are enabled and active.
- Timers use `Persistent=true`, so missed schedules run after the server becomes available again.

## Offsite security decision

The A1 production target uses company Microsoft 365 Business OneDrive. Microsoft 365 provides encryption in transit and at rest.

Client-side `rclone crypt` was intentionally not enabled in A1 because a crypt key stored only on the VPS would create a disaster-recovery dependency: losing the VPS could also lose the only decryption key. Client-side encryption should only be added after a separate recovery-key escrow is established, such as a company password manager or offline recovery pack.

The production rclone config is root-owned and mode `0600`. OAuth credentials are not committed to GitHub.

## CI note

PR CI reports the same three date-dependent integration-test failures already present on the `master` baseline. A1 does not modify the affected application workflow/test code. These pre-existing deterministic-time test failures are tracked separately and are not an A1 backup regression.

## Acceptance status

| Requirement | Status |
| --- | --- |
| Automatic fresh backup | PASS |
| Failure detectable | PASS |
| Owner-only backup files/config | PASS |
| Local retention implemented and tested | PASS |
| Offsite retention implemented and tested | PASS |
| Production-compatible restore procedure | PASS |
| Isolated restore verification | PASS |
| Daily timer enabled | PASS |
| Weekly restore-verification timer enabled | PASS |
| Company-owned off-VPS destination authorized | PASS |
| Production backup copied off-VPS | PASS |
| Remote read-back checksum verified | PASS |
| Failure notification beyond local journal/status | DEFERRED TO A4 |

## Safety notes

- Production DB was never used as a restore-test target.
- The application did not need to be stopped or redeployed for backup/offsite validation.
- Development / WhatsApp code was not deployed or merged as part of A1.
- No OAuth token or cloud credential is stored in the repository.
- Test folders/files created during validation were removed.

**A1 status: COMPLETE.**

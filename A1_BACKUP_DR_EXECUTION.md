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
- Daily/weekly/monthly retention logic: 14 daily, 8 weekly, 12 monthly.
- Success/failure status files and system journal logging.
- Production-compatible restore script that stops only the Billing app and leaves external Traefik running.
- Emergency pre-restore backup before destructive production restore.
- Isolated PostgreSQL 17 restore verification script.
- Daily systemd backup timer.
- Weekly systemd isolated restore verification timer.
- `rclone` installed on the VPS for direct offsite copy support.
- Direct rclone offsite upload support with remote read-back SHA-256 verification.
- Mounted-filesystem offsite mode remains available as an alternative.

## Production validation

Manual production backup validation on 2026-09-17:

- backup service: PASS
- backup file: `billing-20260917T074054Z.dump`
- bytes: `108090`
- SHA-256: `58d1f6057851f5a79a57080000c1cad2096268eaee3921aafdb178ae13a8f290`
- isolated restore: PASS
- restored public tables: 28
- EF migration rows: 9
- application HTTPS login after validation: HTTP 200
- production PostgreSQL container remained healthy

Retention logic was independently tested against synthetic backup sets in both dry-run and apply modes. Sidecar deletion matched dump deletion and unrelated files were preserved.

The rclone offsite mechanism was also tested with an isolated temporary rclone alias target:

- backup creation: PASS
- dump upload: PASS
- SHA-256 sidecar upload: PASS
- remote dump read-back: PASS
- remote read-back SHA-256 matched local SHA-256: PASS
- resulting status: `rclone-copied-verified`

The temporary test target was deleted after verification and was not treated as a real offsite backup.

## Active schedules

- Daily backup: approximately 02:30 Asia/Kuala_Lumpur, with up to 10 minutes randomized delay.
- Weekly isolated restore verification: Sunday approximately 03:30 Asia/Kuala_Lumpur, with up to 15 minutes randomized delay.
- Timers use `Persistent=true`, so missed schedules run after the server becomes available again.

## Offsite status

**External destination authorization is still pending.**

The VPS is now prepared for direct rclone-based offsite backup. No company cloud account has been connected yet, so no financial backup has been uploaded outside the VPS.

Preferred configuration after a company-owned cloud destination is authorized:

```text
OFFSITE_RCLONE_REMOTE=billing-backup-crypt:production
OFFSITE_RCLONE_CONFIG=/root/.config/rclone/rclone.conf
OFFSITE_REQUIRED=1
```

The target should preferably be an rclone `crypt` remote layered over the company-owned storage provider. The backup job uploads the dump and checksum sidecar, then reads the remote dump back through rclone and recalculates SHA-256. The job is successful only when the read-back checksum matches the local backup.

A separately mounted remote filesystem remains supported as an alternative, but another directory on the same VPS must never be treated as offsite disaster recovery.

A1 cannot be marked fully complete until an off-VPS destination is authorized, a production backup is copied there, and the remote read-back checksum is verified.

## CI note

PR CI currently reports the same three date-dependent integration-test failures already present on the `master` baseline. The A1 change does not modify the affected application workflow/test code. These pre-existing deterministic-time test failures remain tracked for A6 and must not be misclassified as an A1 backup regression.

## Acceptance status

| Requirement | Status |
| --- | --- |
| Automatic fresh backup | PASS |
| Failure detectable | PASS |
| Owner-only backup files | PASS |
| Retention implemented and tested | PASS |
| Production-compatible restore procedure | PASS |
| Isolated restore verification | PASS |
| Daily timer enabled | PASS |
| Weekly restore-verification timer enabled | PASS |
| Offsite client / verified copy mechanism | PASS |
| Company-owned off-VPS destination authorized | PENDING |
| Production backup copied off-VPS and read-back checksum verified | PENDING |
| Failure notification beyond local journal/status | PENDING / optional where practical |

## Safety notes

- Production DB was never used as a restore-test target.
- The application did not need to be stopped for backup or verification testing.
- Development / WhatsApp code was not deployed or merged as part of A1.
- No cloud account or OAuth token has been connected without explicit user authorization.
- No production backup has been uploaded to a personal or unknown destination.

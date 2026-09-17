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

## Production validation

The installed scripts were sourced from branch `hardening/a1-backup-dr` at SHA `69163fc507307ba92774f1cdcdd8be6eb4f25a4c`.

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

## Active schedules

- Daily backup: approximately 02:30 Asia/Kuala_Lumpur, with up to 10 minutes randomized delay.
- Weekly isolated restore verification: Sunday approximately 03:30 Asia/Kuala_Lumpur, with up to 15 minutes randomized delay.
- Timers use `Persistent=true`, so missed schedules run after the server becomes available again.

## Offsite status

**Not yet complete.**

No separately mounted remote filesystem, object-storage client, or existing offsite destination was present on the production VPS during implementation. `OFFSITE_DIR` therefore remains disabled rather than copying to another path on the same VPS and incorrectly treating it as disaster recovery.

The backup implementation already supports a separately mounted offsite filesystem through `/etc/billing-control/backup.env` using:

```text
OFFSITE_DIR=/mnt/billing-control-offsite
OFFSITE_REQUIRED=1
OFFSITE_MUST_BE_MOUNT=1
```

A1 cannot be marked fully complete until an off-VPS destination is configured and a copied backup is checksum-verified there.

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
| Off-VPS copy | PENDING |
| Failure notification beyond local journal/status | PENDING / optional where practical |

## Safety notes

- Production DB was never used as a restore-test target.
- The application did not need to be stopped for backup or verification testing.
- Development / WhatsApp code was not deployed or merged as part of A1.
- Do not configure `OFFSITE_DIR` to another directory on the same VPS filesystem.

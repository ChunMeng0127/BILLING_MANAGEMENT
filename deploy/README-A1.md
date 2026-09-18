# A1 Backup & DR Operator Commands

## Check timers

```bash
systemctl list-timers billing-control-backup.timer billing-control-backup-verify.timer --all
```

## Check latest backup status

```bash
cat /var/lib/billing-control-backup/last-success
cat /var/lib/billing-control-backup/last-failure 2>/dev/null || true
```

## Check latest restore verification

```bash
cat /var/lib/billing-control-backup/last-restore-verification
```

## Run an immediate backup

```bash
systemctl start billing-control-backup.service
systemctl status billing-control-backup.service --no-pager
```

## Run an isolated restore verification

```bash
systemctl start billing-control-backup-verify.service
systemctl status billing-control-backup-verify.service --no-pager
```

## View backup logs

```bash
journalctl -u billing-control-backup.service -u billing-control-backup-verify.service --since today --no-pager
```

## Production restore

Production restore is destructive. Use `deploy/restore.sh` only after identifying the exact verified backup and reviewing the rollback plan. The script requires the explicit `--confirm-replace` argument and creates an emergency pre-restore backup before replacing the database by default.

Never use the production database as a restore-test target. Use `verify-backup-restore.sh` for restore drills.

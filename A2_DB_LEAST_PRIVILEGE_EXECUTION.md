# A2 Database Least Privilege Execution Record

Status: COMPLETE (production controls applied; PR/CI pending)

## Production outcome

A2 separates normal application, migration, backup, and emergency database authority:

| Role | Purpose | Superuser | Login |
| --- | --- | --- | --- |
| `billing_runtime` | Web application DML | No | Yes |
| `billing_migrator` | Database/schema owner and EF migrations | No | Yes |
| `billing_backup` | Read-only pg_dump | No | Yes |
| `billing_breakglass` | Emergency administration / restore only | Yes | Yes |
| `billing` | PostgreSQL bootstrap role retained by cluster | Yes* | No |

`billing` is PostgreSQL's bootstrap superuser and PostgreSQL does not permit removing its
SUPERUSER attribute. It has therefore been removed from all normal operational paths,
set to NOLOGIN, and its password set to NULL.

## Ownership and grants

- Database `billing` owner: `billing_migrator`
- Public tables owned by `billing_migrator`: 28/28
- Public sequences owned by `billing_migrator`: 22/22
- `billing_runtime`: SELECT/INSERT/UPDATE/DELETE on all current tables; required sequence rights
- `billing_backup`: SELECT on all current tables and sequences
- Default privileges are owned only by `billing_migrator`, so future migration-created objects
  inherit runtime and backup grants.
- Stale default ACL entries owned by bootstrap `billing` were removed.

## Production routing

- Web app connection: `billing_runtime`
- EF migration connection: `billing_migrator`
- Backup DB user: `billing_backup`
- Restore admin: `billing_breakglass`
- Restore object owner: `billing_migrator`

## Validation completed

- Runtime TCP authentication: PASS
- Runtime DML capability: PASS
- Runtime DDL denied: PASS
- Migrator SCRAM authentication from Docker database network: PASS
- EF `--migrate`: PASS; database already up to date
- Backup role `pg_dump`: PASS
- Break-glass temporary create/drop database test: PASS
- Production HTTPS: HTTP 200
- PostgreSQL container: healthy
- Recent application DB permission errors: 0
- Latest A2 backup: `billing-20260918T042353Z.dump`
- SHA-256: `ab524bc76b361ba2884ee77d76bbd58f46a7b6b88af1c0867c6ee15a804eb669`
- Microsoft 365 offsite read-back verification: PASS
- Isolated restore: PASS
  - tables: 28
  - migrations: 9
  - owner mismatch: 0
  - runtime table grants: 28
  - backup table grants: 28

## Restore verification improvement

After A2, backups contain role ACLs. The isolated restore verifier now creates NOLOGIN
placeholder roles (`billing_migrator`, `billing_runtime`, `billing_backup`), makes the temporary
database owned by `billing_migrator`, and restores using `--role=billing_migrator`. This verifies
both data restoration and the least-privilege ownership/grant model instead of bypassing ACLs.

## Rollback / emergency notes

- Root-only pre-A2 copies of production compose/config files were retained on the VPS during cutover.
- Database credentials remain outside GitHub.
- `billing_breakglass` must never be used by the web application or normal migration/backup jobs.
- Do not re-enable login on bootstrap `billing` for routine operations.

## Known unrelated CI baseline

The production branch has three pre-existing date-dependent integration test failures.
A2 does not modify that application test logic; it remains scheduled for A6.

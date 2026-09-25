# A4 CI, Health Checks & Monitoring Execution Record

Status: PRODUCTION COMPLETE; PR/merge pending

## Objective

Align automated validation more closely with production and make application, database,
backup and repeated runtime failures detectable without manual browser testing.

## Health endpoints

The application now exposes two anonymous endpoints that return only a simple health state:

- `/health/live` — application process availability
- `/health/ready` — application readiness including PostgreSQL connectivity

No database connection strings, credentials, query details, user data, or internal
configuration are returned.

Production health paths are permitted over container-local HTTP so Docker can probe the
application without passing through the public reverse proxy. Normal application routes
continue to use HTTPS redirection. External monitoring still checks the health endpoint
through production Traefik over HTTPS.

## Docker health

The production application container now has a healthcheck.

For current A4 releases it requires `/health/ready` HTTP 200.

To preserve A3 rollback compatibility, an old release that predates the health endpoint
may use the legacy HTTPS-redirect/login fallback. A real A4 readiness failure returning
503 is never accepted by the fallback.

Preflight against the legacy A3 release confirmed the compatibility path before production
cutover.

## CI topology

CI HTTPS smoke now uses Traefik instead of nginx and pins the same Traefik image digest
currently running on the VPS.

The CI smoke requires:

- Docker application health = healthy
- HTTPS `/health/ready` = 200
- HTTPS login route = 200
- anonymous redirect/login behavior
- antiforgery-protected admin login
- Secure Identity cookie
- protected admin, billing schedule and progress pages

PR #5 CI run:

`https://github.com/ChunMeng0127/BILLING_MANAGEMENT/actions/runs/35316443393`

Result: PASS

The run includes 51/51 .NET tests, JavaScript tests, Docker build, Docker health and
Traefik HTTPS smoke.

## Immutable production deployment

Production was promoted through the A3 exact-SHA release gate.

Release SHA:

`a80a09802ea0c977dd69892ae703eb8120144b0f`

Artifact SHA-256:

`b8c031b0663b7a6b7f39067971fbfc15baaae5867ebcc9e0f33a1a76ddb03a54`

Previous production release:

`6677680feb12141eefe97dd5d21b37fd3b6b4bf1`

Deployment smoke:

- Docker health: healthy
- `/health/live`: HTTP 200
- `/health/ready`: HTTP 200
- `/Account/Login`: HTTP 200
- PostgreSQL: healthy
- recent critical/DB/HTTP-5xx application errors: 0

No database migration was required.

## Pre-deploy backup and restore regression

Pre-deploy backup:

- file: `billing-20260918T065235Z.dump`
- SHA-256: `290e9d6eba41ff9a0bc5346edabe9e7cc48468555b82096e07db4c38fd9075fe`
- Microsoft 365 offsite read-back: PASS

Isolated restore:

- result: PASS
- tables: 28
- migrations: 9
- ownership mismatches: 0
- runtime table grants: 28/28
- backup table grants: 28/28

## Production monitor

A systemd monitor runs every five minutes:

- service: `billing-control-monitor.service`
- timer: `billing-control-monitor.timer`
- script: `/usr/local/lib/billing-control/monitor-production.sh`
- state: `/var/lib/billing-control-monitor/last-status`

It checks:

- application container exists and is running
- Docker health is healthy
- restart-count increases on the same container
- external HTTPS readiness and login route
- latest backup status and age
- Microsoft 365 offsite verification
- newer backup failure versus last success
- repeated HTTP 5xx in recent app logs
- repeated Npgsql/PostgreSQL connection/readiness failures

Production monitor validation:

- normal production: PASS
- synthetic newer backup failure: detected and returned non-zero
- synthetic missing application container: detected and returned non-zero
- synthetic repeated HTTP 5xx: 3 detected and returned non-zero
- synthetic repeated DB connection errors: 2 detected and returned non-zero

Current normal status after the synthetic tests:

- monitor status: healthy
- app state: running
- app health: healthy
- restart count: 0
- readiness: HTTP 200
- login: HTTP 200
- backup status: success
- backup offsite: rclone-copied-verified
- repeated HTTP 5xx: 0
- repeated DB errors: 0

## Explicit deployment smoke

The A3 release workflow now finishes only after all of the following succeed:

- Docker application health
- HTTPS readiness
- HTTPS login route
- runtime release metadata verification

A failed post-deploy smoke stops the application and returns a non-zero release result.
It does not automatically restore the database.

Application rollback performs the same health checks. Legacy pre-A4 artifacts remain
eligible through the documented compatibility fallback, but a current release returning
readiness 503 is considered unhealthy.

## Operations

Current health state:

```bash
cat /var/lib/billing-control-monitor/last-status
```

Monitor timer:

```bash
systemctl status billing-control-monitor.timer
```

Latest monitor execution:

```bash
systemctl status billing-control-monitor.service
journalctl -u billing-control-monitor.service -n 100 --no-pager
```

Application Docker health:

```bash
docker inspect billing-control-app-1 --format '{{.State.Health.Status}}'
```

## Alerting scope

A4 establishes reliable local detection and failure state through Docker health,
systemd result/status files and journald. No external paging destination such as email,
Slack or PagerDuty has been configured; adding one can consume the existing non-zero
systemd result without changing the health logic.

## Result

A4 acceptance criteria are met:

- an unhealthy application is detectable without a browser
- CI exercises Traefik and production-style HTTPS health/login paths
- failed/stale/offsite-invalid backups are surfaced
- repeated 5xx and database connection failures are surfaced
- production deployment ends with an explicit smoke result

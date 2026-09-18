# A5 VPS / Host Hardening Execution Record

Status: COMPLETE; PR/merge pending

## Objective

Reduce host-level compromise risk without losing legitimate administration or disrupting
the Billing Control production workload or other provider-managed VPS services.

## Administrative access

A non-root administrator, `opsadmin`, was created before restrictive SSH changes.

Controls:

- UID 1000
- member of the sudo group
- account password locked
- SSH public-key authentication
- sudo escalation available non-interactively for production administration

The user's Windows workstation generated a dedicated ED25519 key for this VPS. The public
key was installed under `/home/opsadmin/.ssh/authorized_keys`.

Independent validation was performed from a fresh Windows PowerShell session:

- SSH login as `opsadmin`: PASS
- `whoami`: `opsadmin`
- `sudo -n whoami`: `root`

The original root session was retained until this independent test succeeded.

## SSH hardening

Before changing SSH, a backup of the SSH configuration was taken and an automatic rollback
timer was armed.

Production effective SSH policy is now:

- `PermitRootLogin no`
- `PasswordAuthentication no`
- `PubkeyAuthentication yes`
- `KbdInteractiveAuthentication no`
- `PermitEmptyPasswords no`
- `X11Forwarding no`
- `MaxAuthTries 3`

Ubuntu cloud-init originally set `PasswordAuthentication yes` in an earlier drop-in.
The hardened policy therefore uses `00-a5-hardening.conf` so it is evaluated before the
cloud-init file.

A fresh independent `opsadmin` login was verified after the restrictive SSH policy took
effect. The automatic rollback timer was then cancelled.

## Brute-force protection

fail2ban was installed and enabled.

The production sshd jail uses:

- systemd journal backend
- maximum retries: 3
- find time: 10 minutes
- ban time: 1 hour

Post-reboot status: active.

## Firewall

UFW is active and enabled at boot.

Final policy:

- default deny incoming
- default allow outgoing
- default deny routed
- TCP 22: LIMIT
- TCP 80: ALLOW
- TCP 443: ALLOW
- equivalent IPv6 rules enabled

The two accidental public PostgreSQL test ports discovered during A5, TCP 55432 and
55439, were closed by stopping/removing their temporary test containers.

Three leftover A2/A6 PostgreSQL test containers and their anonymous test volumes were
removed. Billing Control PostgreSQL has never published a host port.

## Hostinger provider exception

The Hostinger Hermes management agent is provider-managed and maps container TCP 4860 to
a dynamic Docker host port. Its host port changed from 32769 before reboot to 32768 after
reboot.

Because the port is dynamically allocated and Docker-published ports are not reliably
represented by normal UFW input rules, no hard-coded UFW rule is retained for Hermes.
The provider-managed container was not modified.

This exception is unrelated to Billing Control and should be reviewed against Hostinger's
management requirements before any future attempt to restrict it.

## Billing container hardening

The Billing application and migration runtime were moved away from root execution.

Production application controls:

- user: `1654:1654` (`app`)
- root filesystem: read-only
- `/tmp`: tmpfs
- Linux capabilities: all dropped
- `no-new-privileges:true`
- PID limit: 256
- release artifact mount: read-only
- Data Protection key volume remains writable

Runtime verification:

- application UID/GID: 1654/1654
- write to `/app`: denied
- write to `/etc`: denied
- write to `/tmp`: PASS
- write to `/keys`: PASS
- effective/permitted/bounding/ambient capabilities: all zero
- `NoNewPrivs`: 1
- Docker health: healthy
- HTTPS readiness/login: 200

CI uses the same restricted runtime model and passed migration, Docker health, Traefik
HTTPS and authenticated smoke.

## Non-root release metadata compatibility

The first restricted production cutover reached a healthy application but the release
workflow returned non-zero during final metadata verification.

Root cause:

A3 had created `RELEASE_SHA` and `RELEASE_ARTIFACT_SHA256` as mode 600 root-owned files.
The non-root application could not read them.

No application or database failure occurred. Docker health and HTTPS readiness/login
were already passing.

Correction:

- release metadata is now mode 444
- the release manifest remains mode 600
- existing retained release metadata was normalized to mode 444
- the live release workflow was updated
- current release state was reconciled after verification

Current production release:

`2dd7d81a918c17c2c933afe103666859bdad286f`

Artifact SHA-256:

`e5a31cd26132a75da62c73d3edf6758a6e63a991f2f4df0abfadb30ae725292f`

## System updates

A5 applied all 27 pending Ubuntu/Docker updates, including Docker Engine, containerd,
Docker Compose, netplan and Kerberos libraries.

After upgrade:

- pending updates: 0
- controlled reboot completed
- active kernel: `6.8.0-139-generic`
- reboot-required flag: cleared

## Desktop Commander recovery

Desktop Commander had previously been run manually in tmux. A systemd boot service was
enabled before reboot so the authorized remote management agent could reconnect
automatically after the controlled reboot.

Post-reboot status: active.

## Backup before reboot

Latest pre-reboot backup:

- file: `billing-20260918T081958Z.dump`
- SHA-256: `973b05a77cce923757f857fc990aa537e5f3107b5195a490c60cee8b6ecc4b86`
- Microsoft 365 offsite read-back: PASS

Post-reboot isolated restore:

- result: PASS
- tables: 28
- migrations: 9
- ownership mismatches: 0
- runtime table grants: 28/28
- backup table grants: 28/28

## Post-reboot production verification

Host:

- kernel: `6.8.0-139-generic`
- pending package updates: 0
- reboot required: no
- SSH hardened policy: active
- UFW: active
- fail2ban: active
- Docker: active
- Desktop Commander boot service: active
- production monitor timer: active
- backup timer: active
- isolated restore-verification timer: active

Billing Control:

- release SHA: `2dd7d81a918c17c2c933afe103666859bdad286f`
- application Docker health: healthy
- PostgreSQL Docker health: healthy
- HTTPS readiness: 200
- HTTPS login: 200
- application user: `1654:1654`
- read-only root filesystem: true
- all capabilities dropped
- `no-new-privileges`: active
- PID limit: 256
- recent application 5xx/DB/unhandled errors: 0

Production monitor after reboot:

- status: healthy
- restart count: 0
- readiness: 200
- login: 200
- backup status: success
- offsite status: rclone-copied-verified
- recent HTTP 5xx: 0
- recent DB errors: 0

## Acceptance result

A5 acceptance criteria are met:

- tested non-root administrative access exists
- unnecessary public test database ports are closed
- firewall policy is active and documented
- SSH root/password exposure is removed
- brute-force protection is active
- Billing application runs under reduced container privilege
- system updates and controlled reboot completed
- application, database, monitoring and backup/restore controls recovered successfully

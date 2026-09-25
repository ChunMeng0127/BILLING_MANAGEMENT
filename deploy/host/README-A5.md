# A5 VPS / Host Hardening

These files record the production host controls applied during A5.

## SSH

Production uses key-only SSH through a non-root administrative account.

Effective policy:

- public-key authentication enabled
- password authentication disabled
- keyboard-interactive authentication disabled
- direct root SSH disabled
- X11 forwarding disabled
- maximum authentication attempts reduced to 3

Do not apply the SSH drop-in until a second independent non-root SSH session and sudo
escalation have been verified. The A5 production cutover used an automatic rollback timer
until the fresh login was confirmed.

## fail2ban

The sshd jail bans an address for one hour after three failures within ten minutes.

## UFW

Production policy:

- default deny incoming
- default allow outgoing
- default deny routed
- rate-limit TCP 22
- allow TCP 80
- allow TCP 443

Do not blindly add Docker host ports to UFW. Docker-published ports may bypass normal UFW
input-chain expectations.

The Hostinger Hermes management agent is provider-managed and requests a dynamic Docker
host port for container port 4860. Its host port changed across reboot, so it is recorded
as a provider exception rather than hard-coded into UFW.

Billing Control itself publishes no application or PostgreSQL port directly to the host;
public HTTP/HTTPS terminates at Traefik.

## Package/reboot policy

A5 installed all pending Ubuntu/Docker updates and completed a controlled reboot. Verify
after every host reboot:

- expected kernel is active
- SSH effective policy
- UFW and fail2ban active
- Docker active
- production app/database healthy
- production monitor/backup/restore-verification timers active
- exact immutable release metadata still matches

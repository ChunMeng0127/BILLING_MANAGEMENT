#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
umask 077
mkdir -p backups
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
docker compose exec -T db pg_dump -U billing -d billing -Fc > "backups/billing-${stamp}.dump"
docker compose exec -T db pg_restore --list < "backups/billing-${stamp}.dump" > /dev/null
printf 'Backup created: backups/billing-%s.dump\n' "$stamp"

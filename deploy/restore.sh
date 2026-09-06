#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
backup="${1:?Usage: bash deploy/restore.sh /absolute/path/backup.dump --confirm-replace}"
[[ "${2:-}" == "--confirm-replace" ]] || { echo "Restore replaces the billing database. Pass --confirm-replace after reviewing the backup." >&2; exit 1; }
[[ -f "$backup" ]] || { echo "Backup not found" >&2; exit 1; }
docker compose exec -T db pg_restore --list < "$backup" > /dev/null
docker compose stop app nginx
docker compose exec -T db dropdb -U billing --force billing
docker compose exec -T db createdb -U billing billing
docker compose exec -T db pg_restore -U billing -d billing --no-owner --exit-on-error --single-transaction < "$backup"
docker compose up -d app nginx

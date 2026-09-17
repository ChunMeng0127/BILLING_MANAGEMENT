#!/usr/bin/env bash
set -Eeuo pipefail

backup="${1:?Usage: verify-backup-restore.sh /absolute/path/backup.dump}"
[[ -f "$backup" ]] || { echo "Backup not found: $backup" >&2; exit 2; }

if [[ -f "$backup.sha256" ]]; then
  expected="$(awk '{print $1}' "$backup.sha256")"
  actual="$(sha256sum "$backup" | awk '{print $1}')"
  [[ "$expected" == "$actual" ]] || { echo "SHA-256 mismatch for $backup" >&2; exit 3; }
fi

name="billing-restore-check-$$"
password="$(openssl rand -hex 24)"
cleanup() { docker rm -f "$name" >/dev/null 2>&1 || true; }
trap cleanup EXIT

printf 'Starting isolated PostgreSQL restore verification...\n'
docker run -d --rm --name "$name" \
  -e POSTGRES_DB=billing_verify \
  -e POSTGRES_USER=billing_verify \
  -e POSTGRES_PASSWORD="$password" \
  postgres:17 >/dev/null

for _ in $(seq 1 30); do
  if docker exec "$name" pg_isready -U billing_verify -d billing_verify >/dev/null 2>&1; then break; fi
  sleep 1
done
docker exec "$name" pg_isready -U billing_verify -d billing_verify >/dev/null

docker exec -i "$name" pg_restore \
  -U billing_verify -d billing_verify \
  --no-owner --exit-on-error --single-transaction < "$backup"

tables="$(docker exec "$name" psql -U billing_verify -d billing_verify -Atc \
  "select count(*) from pg_tables where schemaname='public';")"
migrations="$(docker exec "$name" psql -U billing_verify -d billing_verify -Atc \
  'select count(*) from "__EFMigrationsHistory";' 2>/dev/null || echo 0)"

min_tables="${MIN_TABLES:-28}"
min_migrations="${MIN_MIGRATIONS:-9}"
[[ "$tables" =~ ^[0-9]+$ && "$tables" -ge "$min_tables" ]]
[[ "$migrations" =~ ^[0-9]+$ && "$migrations" -ge "$min_migrations" ]]

printf 'restore_test=PASS\n'
printf 'backup=%s\n' "$backup"
printf 'restored_tables=%s\n' "$tables"
printf 'migration_rows=%s\n' "$migrations"

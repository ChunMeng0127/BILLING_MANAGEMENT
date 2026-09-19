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

docker exec -i "$name" psql -U billing_verify -d billing_verify -v ON_ERROR_STOP=1 <<'SQL'
CREATE ROLE billing_migrator NOLOGIN;
CREATE ROLE billing_runtime NOLOGIN;
CREATE ROLE billing_backup NOLOGIN;
ALTER DATABASE billing_verify OWNER TO billing_migrator;
SQL

docker exec -i "$name" pg_restore \
  -U billing_verify -d billing_verify --role=billing_migrator \
  --no-owner --exit-on-error --single-transaction < "$backup"

tables="$(docker exec "$name" psql -U billing_verify -d billing_verify -Atc "select count(*) from pg_tables where schemaname='public';")"
migrations="$(docker exec "$name" psql -U billing_verify -d billing_verify -Atc 'select count(*) from "__EFMigrationsHistory";' 2>/dev/null || echo 0)"
owner_mismatch="$(docker exec "$name" psql -U billing_verify -d billing_verify -Atc "select count(*) from pg_tables where schemaname='public' and tableowner <> 'billing_migrator';")"
runtime_grants="$(docker exec "$name" psql -U billing_verify -d billing_verify -Atc "select count(*) from pg_tables where schemaname='public' and has_table_privilege('billing_runtime',format('%I.%I',schemaname,tablename),'SELECT,INSERT,UPDATE,DELETE');")"
backup_grants="$(docker exec "$name" psql -U billing_verify -d billing_verify -Atc "select count(*) from pg_tables where schemaname='public' and has_table_privilege('billing_backup',format('%I.%I',schemaname,tablename),'SELECT');")"

min_tables="${MIN_TABLES:-28}"
min_migrations="${MIN_MIGRATIONS:-9}"
[[ "$tables" =~ ^[0-9]+$ && "$tables" -ge "$min_tables" ]]
[[ "$migrations" =~ ^[0-9]+$ && "$migrations" -ge "$min_migrations" ]]
[[ "$owner_mismatch" == "0" ]]
[[ "$runtime_grants" == "$tables" ]]
[[ "$backup_grants" == "$tables" ]]

printf 'restore_test=PASS\n'
printf 'backup=%s\n' "$backup"
printf 'restored_tables=%s\n' "$tables"
printf 'migration_rows=%s\n' "$migrations"
printf 'owner_mismatch=%s\n' "$owner_mismatch"
printf 'runtime_table_grants=%s\n' "$runtime_grants"
printf 'backup_table_grants=%s\n' "$backup_grants"

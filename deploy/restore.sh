#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="${PROJECT_DIR:-$(cd "$SCRIPT_DIR/.." && pwd)}"
DB_SERVICE="${DB_SERVICE:-db}"
APP_SERVICE="${APP_SERVICE:-app}"
DB_USER="${DB_USER:-billing}"
DB_NAME="${DB_NAME:-billing}"

backup="${1:?Usage: restore.sh /absolute/path/backup.dump --confirm-replace}"
[[ "${2:-}" == "--confirm-replace" ]] || {
  echo "Restore REPLACES the production billing database." >&2
  echo "Re-run with --confirm-replace only after reviewing the backup and rollback plan." >&2
  exit 2
}
[[ -f "$backup" ]] || { echo "Backup not found: $backup" >&2; exit 2; }

if [[ -n "${COMPOSE_FILE:-}" ]]; then
  compose_file="$COMPOSE_FILE"
elif [[ -f "$PROJECT_DIR/docker-compose.yml" ]]; then
  compose_file="$PROJECT_DIR/docker-compose.yml"
elif [[ -f "$PROJECT_DIR/docker-compose.yaml" ]]; then
  compose_file="$PROJECT_DIR/docker-compose.yaml"
elif [[ -f "$PROJECT_DIR/compose.yaml" ]]; then
  compose_file="$PROJECT_DIR/compose.yaml"
else
  echo "No Compose file found in $PROJECT_DIR" >&2
  exit 2
fi
compose() { docker compose -f "$compose_file" --project-directory "$PROJECT_DIR" "$@"; }

compose exec -T "$DB_SERVICE" pg_restore --list < "$backup" > /dev/null
if [[ -f "$backup.sha256" ]]; then
  expected="$(awk '{print $1}' "$backup.sha256")"
  actual="$(sha256sum "$backup" | awk '{print $1}')"
  [[ "$expected" == "$actual" ]] || { echo "SHA-256 mismatch for $backup" >&2; exit 3; }
fi

if [[ "${PRE_RESTORE_BACKUP:-1}" == "1" ]]; then
  echo "Creating an emergency pre-restore backup first..."
  PROJECT_DIR="$PROJECT_DIR" COMPOSE_FILE="$compose_file" APPLY_RETENTION=0 OFFSITE_REQUIRED=0 \
    "$SCRIPT_DIR/backup.sh"
fi

echo "Stopping the Billing application. External Traefik remains running."
compose stop "$APP_SERVICE"

restore_failed=1
finish() {
  if [[ "$restore_failed" == "0" ]]; then
    compose up -d "$APP_SERVICE"
  else
    echo "Restore did not complete. Application remains stopped to avoid serving a partial database." >&2
  fi
}
trap finish EXIT

compose exec -T "$DB_SERVICE" dropdb -U "$DB_USER" --force "$DB_NAME"
compose exec -T "$DB_SERVICE" createdb -U "$DB_USER" "$DB_NAME"
compose exec -T "$DB_SERVICE" pg_restore \
  -U "$DB_USER" -d "$DB_NAME" \
  --no-owner --exit-on-error --single-transaction < "$backup"

migrations="$(compose exec -T "$DB_SERVICE" psql -U "$DB_USER" -d "$DB_NAME" -Atc \
  'select count(*) from "__EFMigrationsHistory";' | tr -d '\r')"
tables="$(compose exec -T "$DB_SERVICE" psql -U "$DB_USER" -d "$DB_NAME" -Atc \
  "select count(*) from pg_tables where schemaname='public';" | tr -d '\r')"
[[ "$tables" =~ ^[0-9]+$ && "$tables" -gt 0 ]]
[[ "$migrations" =~ ^[0-9]+$ && "$migrations" -gt 0 ]]

restore_failed=0
echo "Restore completed. tables=$tables migrations=$migrations"
echo "The application will now be started; Traefik was not modified."

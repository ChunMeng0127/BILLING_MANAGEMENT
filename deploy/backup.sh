#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="${PROJECT_DIR:-$(cd "$SCRIPT_DIR/.." && pwd)}"
BACKUP_DIR="${BACKUP_DIR:-$PROJECT_DIR/backups}"
STATUS_DIR="${STATUS_DIR:-/var/lib/billing-control-backup}"
LOCK_FILE="${LOCK_FILE:-/var/lock/billing-control-backup.lock}"
DB_SERVICE="${DB_SERVICE:-db}"
DB_USER="${DB_USER:-billing_backup}"
DB_NAME="${DB_NAME:-billing}"
APPLY_RETENTION="${APPLY_RETENTION:-1}"
OFFSITE_DIR="${OFFSITE_DIR:-}"
OFFSITE_RCLONE_REMOTE="${OFFSITE_RCLONE_REMOTE:-}"
OFFSITE_RCLONE_CONFIG="${OFFSITE_RCLONE_CONFIG:-/root/.config/rclone/rclone.conf}"
OFFSITE_REQUIRED="${OFFSITE_REQUIRED:-0}"
OFFSITE_MUST_BE_MOUNT="${OFFSITE_MUST_BE_MOUNT:-1}"

if [[ -n "$OFFSITE_DIR" && -n "$OFFSITE_RCLONE_REMOTE" ]]; then
  echo "Configure either OFFSITE_DIR or OFFSITE_RCLONE_REMOTE, not both." >&2
  exit 2
fi

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

compose() {
  docker compose -f "$compose_file" --project-directory "$PROJECT_DIR" "$@"
}

umask 077
mkdir -p "$BACKUP_DIR" "$STATUS_DIR"
exec 9>"$LOCK_FILE"
if ! flock -n 9; then
  echo "Another billing backup is already running." >&2
  exit 75
fi

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
started="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
tmp="$BACKUP_DIR/.billing-${stamp}.dump.partial"
final="$BACKUP_DIR/billing-${stamp}.dump"
sha_file="$final.sha256"

on_error() {
  rc=$?
  rm -f "$tmp"
  {
    echo "status=failed"
    echo "timestamp=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "exit_code=$rc"
  } > "$STATUS_DIR/last-failure"
  chmod 600 "$STATUS_DIR/last-failure"
  logger -t billing-control-backup "FAILED exit_code=$rc"
  exit "$rc"
}
trap on_error ERR

printf 'Creating PostgreSQL backup at %s\n' "$started"
compose exec -T "$DB_SERVICE" pg_dump -U "$DB_USER" -d "$DB_NAME" -Fc > "$tmp"
test -s "$tmp"
compose exec -T "$DB_SERVICE" pg_restore --list < "$tmp" > /dev/null
sha="$(sha256sum "$tmp" | awk '{print $1}')"
size="$(stat -c %s "$tmp")"
mv "$tmp" "$final"
printf '%s  %s\n' "$sha" "$(basename "$final")" > "$sha_file"
chmod 600 "$final" "$sha_file"

offsite_status="disabled"
if [[ -n "$OFFSITE_RCLONE_REMOTE" ]]; then
  if ! command -v rclone >/dev/null 2>&1 || [[ ! -f "$OFFSITE_RCLONE_CONFIG" ]]; then
    if [[ "$OFFSITE_REQUIRED" == "1" ]]; then
      echo "rclone or rclone config is unavailable for required offsite backup." >&2
      false
    fi
    offsite_status="unavailable"
  else
    remote_base="${OFFSITE_RCLONE_REMOTE%/}"
    remote_dump="$remote_base/$(basename "$final")"
    remote_sha_file="$remote_base/$(basename "$sha_file")"
    rclone --config "$OFFSITE_RCLONE_CONFIG" copyto "$final" "$remote_dump" --retries 3 --low-level-retries 5
    rclone --config "$OFFSITE_RCLONE_CONFIG" copyto "$sha_file" "$remote_sha_file" --retries 3 --low-level-retries 5
    offsite_sha="$(rclone --config "$OFFSITE_RCLONE_CONFIG" cat "$remote_dump" | sha256sum | awk '{print $1}')"
    [[ "$offsite_sha" == "$sha" ]]
    offsite_status="rclone-copied-verified"
  fi
elif [[ -n "$OFFSITE_DIR" ]]; then
  if [[ "$OFFSITE_MUST_BE_MOUNT" == "1" ]] && ! mountpoint -q "$OFFSITE_DIR"; then
    if [[ "$OFFSITE_REQUIRED" == "1" ]]; then
      echo "Offsite path is not a mounted filesystem: $OFFSITE_DIR" >&2
      false
    fi
    offsite_status="unavailable"
  elif [[ ! -d "$OFFSITE_DIR" || ! -w "$OFFSITE_DIR" ]]; then
    if [[ "$OFFSITE_REQUIRED" == "1" ]]; then
      echo "Offsite path is not writable: $OFFSITE_DIR" >&2
      false
    fi
    offsite_status="unavailable"
  else
    install -m 600 "$final" "$OFFSITE_DIR/$(basename "$final")"
    install -m 600 "$sha_file" "$OFFSITE_DIR/$(basename "$sha_file")"
    offsite_sha="$(sha256sum "$OFFSITE_DIR/$(basename "$final")" | awk '{print $1}')"
    [[ "$offsite_sha" == "$sha" ]]
    offsite_status="mounted-copied-verified"
  fi
fi

retention=("$SCRIPT_DIR/backup-retention.py" "$BACKUP_DIR")
if [[ "$APPLY_RETENTION" == "1" ]]; then retention+=(--apply); fi
python3 "${retention[@]}"

if [[ "$offsite_status" == "rclone-copied-verified" ]]; then
  offsite_retention=("$SCRIPT_DIR/rclone-retention.py" "$OFFSITE_RCLONE_REMOTE" --config "$OFFSITE_RCLONE_CONFIG")
  if [[ "$APPLY_RETENTION" == "1" ]]; then offsite_retention+=(--apply); fi
  python3 "${offsite_retention[@]}"
elif [[ "$offsite_status" == "mounted-copied-verified" ]]; then
  offsite_retention=("$SCRIPT_DIR/backup-retention.py" "$OFFSITE_DIR")
  if [[ "$APPLY_RETENTION" == "1" ]]; then offsite_retention+=(--apply); fi
  python3 "${offsite_retention[@]}"
fi

{
  echo "status=success"
  echo "started=$started"
  echo "completed=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "file=$final"
  echo "bytes=$size"
  echo "sha256=$sha"
  echo "offsite=$offsite_status"
} > "$STATUS_DIR/last-success"
chmod 600 "$STATUS_DIR/last-success"
rm -f "$STATUS_DIR/last-failure"
logger -t billing-control-backup "SUCCESS file=$(basename "$final") bytes=$size offsite=$offsite_status"
printf 'Backup created and validated: %s (%s bytes)\n' "$final" "$size"
printf 'SHA-256: %s\n' "$sha"
printf 'Offsite: %s\n' "$offsite_status"

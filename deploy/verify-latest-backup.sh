#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
default_project_dir="$(cd "$SCRIPT_DIR/.." && pwd)"
if [[ "$default_project_dir" == "/usr/local/lib" && -d /docker/billing-control ]]; then
  default_project_dir="/docker/billing-control"
fi
PROJECT_DIR="${PROJECT_DIR:-$default_project_dir}"
BACKUP_DIR="${BACKUP_DIR:-$PROJECT_DIR/backups}"
STATUS_DIR="${STATUS_DIR:-/var/lib/billing-control-backup}"

umask 077
mkdir -p "$STATUS_DIR"
latest="$(find "$BACKUP_DIR" -maxdepth 1 -type f -name 'billing-*.dump' -printf '%T@ %p\n' | sort -nr | head -1 | cut -d' ' -f2-)"
[[ -n "$latest" && -f "$latest" ]] || { echo "No billing backup found in $BACKUP_DIR" >&2; exit 2; }

on_error() {
  rc=$?
  {
    echo "status=failed"
    echo "timestamp=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "backup=$latest"
    echo "exit_code=$rc"
  } > "$STATUS_DIR/last-restore-verification"
  chmod 600 "$STATUS_DIR/last-restore-verification"
  logger -t billing-control-backup-verify "FAILED backup=$(basename "$latest") exit_code=$rc"
  exit "$rc"
}
trap on_error ERR

output="$($SCRIPT_DIR/verify-backup-restore.sh "$latest")"
printf '%s\n' "$output"
{
  echo "status=success"
  echo "timestamp=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "backup=$latest"
  printf '%s\n' "$output" | grep -E '^(restored_tables|migration_rows)=' || true
} > "$STATUS_DIR/last-restore-verification"
chmod 600 "$STATUS_DIR/last-restore-verification"
logger -t billing-control-backup-verify "SUCCESS backup=$(basename "$latest")"

#!/usr/bin/env bash
set -Eeuo pipefail

PROJECT_DIR="${PROJECT_DIR:-/docker/billing-control}"
STATUS_DIR="${STATUS_DIR:-/var/lib/billing-control-monitor}"
BACKUP_STATUS_DIR="${BACKUP_STATUS_DIR:-/var/lib/billing-control-backup}"
APP_CONTAINER="${APP_CONTAINER:-billing-control-app-1}"
MAX_BACKUP_AGE_SECONDS="${MAX_BACKUP_AGE_SECONDS:-129600}"
LOG_WINDOW="${LOG_WINDOW:-15m}"
HTTP_5XX_THRESHOLD="${HTTP_5XX_THRESHOLD:-3}"
DB_ERROR_THRESHOLD="${DB_ERROR_THRESHOLD:-2}"

mkdir -p "$STATUS_DIR"
chmod 700 "$STATUS_DIR"
failures=()
notes=()

add_failure() { failures+=("$1"); }
read_field() {
  local file="$1" key="$2"
  awk -F= -v key="$key" '$1==key {sub(/^[^=]*=/,""); print; exit}' "$file" 2>/dev/null || true
}

app_state="missing"
app_health="missing"
container_id=""
restart_count="-1"
if docker inspect "$APP_CONTAINER" >/dev/null 2>&1; then
  container_id="$(docker inspect "$APP_CONTAINER" --format '{{.Id}}')"
  app_state="$(docker inspect "$APP_CONTAINER" --format '{{.State.Status}}')"
  app_health="$(docker inspect "$APP_CONTAINER" --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}')"
  restart_count="$(docker inspect "$APP_CONTAINER" --format '{{.RestartCount}}')"
  [[ "$app_state" == "running" ]] || add_failure "app_state=$app_state"
  [[ "$app_health" == "healthy" ]] || add_failure "app_health=$app_health"
else
  add_failure "app_container_missing"
fi

previous_id="$(read_field "$STATUS_DIR/container-state" container_id)"
previous_restarts="$(read_field "$STATUS_DIR/container-state" restart_count)"
if [[ -n "$container_id" && "$container_id" == "$previous_id" && "$restart_count" =~ ^[0-9]+$ && "$previous_restarts" =~ ^[0-9]+$ && "$restart_count" -gt "$previous_restarts" ]]; then
  add_failure "app_restart_count_increased=$previous_restarts->$restart_count"
elif [[ -n "$previous_id" && -n "$container_id" && "$container_id" != "$previous_id" ]]; then
  notes+=("container_changed")
fi
cat > "$STATUS_DIR/container-state.tmp" <<EOF
container_id=$container_id
restart_count=$restart_count
EOF
chmod 600 "$STATUS_DIR/container-state.tmp"
mv "$STATUS_DIR/container-state.tmp" "$STATUS_DIR/container-state"

app_host="$(awk -F= '$1=="APP_HOST"{print $2; exit}' "$PROJECT_DIR/.env" 2>/dev/null || true)"
ready_code="000"
login_code="000"
if [[ -n "$app_host" ]]; then
  ready_code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "https://$app_host/health/ready" || true)"
  login_code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "https://$app_host/Account/Login" || true)"
  [[ "$ready_code" == "200" ]] || add_failure "ready_http=$ready_code"
  [[ "$login_code" == "200" ]] || add_failure "login_http=$login_code"
else
  add_failure "APP_HOST_missing"
fi

backup_status="missing"
backup_completed=""
backup_age="-1"
backup_offsite=""
last_success="$BACKUP_STATUS_DIR/last-success"
last_failure="$BACKUP_STATUS_DIR/last-failure"
if [[ -f "$last_success" ]]; then
  backup_status="$(read_field "$last_success" status)"
  backup_completed="$(read_field "$last_success" completed)"
  backup_offsite="$(read_field "$last_success" offsite)"
  [[ "$backup_status" == "success" ]] || add_failure "backup_status=$backup_status"
  [[ "$backup_offsite" == "rclone-copied-verified" ]] || add_failure "backup_offsite=$backup_offsite"
  if completed_epoch="$(date -u -d "$backup_completed" +%s 2>/dev/null)"; then
    now_epoch="$(date -u +%s)"
    backup_age="$((now_epoch - completed_epoch))"
    (( backup_age <= MAX_BACKUP_AGE_SECONDS )) || add_failure "backup_age_seconds=$backup_age"
  else
    add_failure "backup_completed_invalid"
  fi
else
  add_failure "backup_last_success_missing"
fi
if [[ -f "$last_failure" && -f "$last_success" && "$last_failure" -nt "$last_success" ]]; then
  add_failure "backup_failure_newer_than_success"
fi

recent_logs=""
if docker inspect "$APP_CONTAINER" >/dev/null 2>&1; then
  recent_logs="$(docker logs --since "$LOG_WINDOW" "$APP_CONTAINER" 2>&1 || true)"
fi
http_5xx_count="$(printf '%s\n' "$recent_logs" | grep -Ec 'HTTP 5[0-9][0-9] ' || true)"
db_error_count="$(printf '%s\n' "$recent_logs" | grep -Eci 'Npgsql.*(Exception|fail)|PostgresException|Database readiness check (failed|returned unavailable)|Failed to connect|connection.*(refused|timeout)' || true)"
(( http_5xx_count < HTTP_5XX_THRESHOLD )) || add_failure "recent_http_5xx=$http_5xx_count"
(( db_error_count < DB_ERROR_THRESHOLD )) || add_failure "recent_db_errors=$db_error_count"

checked="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
status="healthy"
(( ${#failures[@]} == 0 )) || status="failed"
issues="none"
(( ${#failures[@]} == 0 )) || issues="$(IFS=';'; echo "${failures[*]}")"
note_text="none"
(( ${#notes[@]} == 0 )) || note_text="$(IFS=';'; echo "${notes[*]}")"

cat > "$STATUS_DIR/last-status.tmp" <<EOF
status=$status
checked=$checked
app_state=$app_state
app_health=$app_health
restart_count=$restart_count
ready_http=$ready_code
login_http=$login_code
backup_status=$backup_status
backup_completed=$backup_completed
backup_age_seconds=$backup_age
backup_offsite=$backup_offsite
recent_http_5xx=$http_5xx_count
recent_db_errors=$db_error_count
notes=$note_text
issues=$issues
EOF
chmod 600 "$STATUS_DIR/last-status.tmp"
mv "$STATUS_DIR/last-status.tmp" "$STATUS_DIR/last-status"

if [[ "$status" != "healthy" ]]; then
  echo "Billing Control production monitor FAILED: $issues" >&2
  exit 1
fi

echo "Billing Control production monitor PASS: ready=$ready_code login=$login_code backup_age=${backup_age}s 5xx=$http_5xx_count db_errors=$db_error_count"

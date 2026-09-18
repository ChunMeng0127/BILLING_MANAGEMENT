#!/usr/bin/env bash
set -Eeuo pipefail

PROJECT_DIR="${PROJECT_DIR:-/docker/billing-control}"
RELEASE_ROOT="${RELEASE_ROOT:-$PROJECT_DIR/releases}"
STATE_DIR="${STATE_DIR:-/var/lib/billing-control-release}"
ENV_FILE="${ENV_FILE:-$PROJECT_DIR/.env}"
REPO_SLUG="${REPO_SLUG:-ChunMeng0127/BILLING_MANAGEMENT}"
CI_WORKFLOW_NAME="${CI_WORKFLOW_NAME:-Build and PostgreSQL tests}"

target="${1:?Usage: release.sh <40-char-commit-sha> [--prepare-only]}"
mode="${2:-}"
[[ "$target" =~ ^[0-9a-fA-F]{40}$ ]] || { echo "Release SHA must be a full 40-character Git commit SHA." >&2; exit 2; }
target="${target,,}"
[[ -z "$mode" || "$mode" == "--prepare-only" ]] || { echo "Unknown option: $mode" >&2; exit 2; }
[[ -f "$ENV_FILE" ]] || { echo "Missing production env file: $ENV_FILE" >&2; exit 2; }

if [[ -f "$PROJECT_DIR/docker-compose.yml" ]]; then
  compose_file="$PROJECT_DIR/docker-compose.yml"
elif [[ -f "$PROJECT_DIR/docker-compose.yaml" ]]; then
  compose_file="$PROJECT_DIR/docker-compose.yaml"
else
  echo "No production Compose file found." >&2
  exit 2
fi

compose() {
  RELEASE_SHA="$target" RELEASE_ROOT="$RELEASE_ROOT"     docker compose -f "$compose_file" --project-directory "$PROJECT_DIR" "$@"
}

hash_publish() {
  local dir="$1"
  (
    cd "$dir"
    find . -type f ! -name RELEASE_ARTIFACT_SHA256 -print0       | LC_ALL=C sort -z       | xargs -0 sha256sum       | sha256sum       | awk '{print $1}'
  )
}

write_env_release() {
  local value="$1" tmp
  tmp="$(mktemp)"
  awk -v value="$value" '
    BEGIN { found=0 }
    /^RELEASE_SHA=/ { print "RELEASE_SHA=" value; found=1; next }
    { print }
    END { if (!found) print "RELEASE_SHA=" value }
  ' "$ENV_FILE" > "$tmp"
  chmod 600 "$tmp"
  mv "$tmp" "$ENV_FILE"
}

mkdir -p "$RELEASE_ROOT" "$STATE_DIR"
chmod 700 "$RELEASE_ROOT" "$STATE_DIR"

echo "Checking CI success for release $target ..."
ci_data="$(curl -fsSL --retry 3   "https://api.github.com/repos/$REPO_SLUG/actions/runs?head_sha=$target&status=completed&per_page=100")"
read -r ci_ok ci_url < <(
  printf '%s' "$ci_data" | python3 -c '
import json,sys
d=json.load(sys.stdin)
name=sys.argv[1]
runs=[r for r in d.get("workflow_runs",[]) if r.get("name")==name and r.get("conclusion")=="success"]
if runs:
    r=sorted(runs,key=lambda x:x.get("updated_at",""),reverse=True)[0]
    print("yes",r.get("html_url",""))
else:
    print("no","")
' "$CI_WORKFLOW_NAME"
)
[[ "$ci_ok" == "yes" ]] || {
  echo "No successful '$CI_WORKFLOW_NAME' workflow run exists for $target." >&2
  exit 3
}
echo "CI gate PASS: $ci_url"

release_dir="$RELEASE_ROOT/$target"
publish_dir="$release_dir/publish"
manifest="$release_dir/manifest.env"
mkdir -p "$release_dir"

if [[ -f "$manifest" && -f "$publish_dir/RELEASE_ARTIFACT_SHA256" ]]; then
  expected="$(awk -F= '$1=="artifact_sha256"{print $2}' "$manifest")"
  actual="$(hash_publish "$publish_dir")"
  [[ -n "$expected" && "$actual" == "$expected" ]] || {
    echo "Existing release artifact hash mismatch for $target." >&2
    exit 4
  }
  artifact_sha="$actual"
  echo "Reusing verified immutable artifact: $artifact_sha"
else
  rm -rf "$release_dir/source" "$publish_dir"
  mkdir -p "$release_dir/source" "$publish_dir"

  echo "Fetching exact source SHA ..."
  compose run --rm --no-deps source

  fetched="$(cat "$release_dir/source/RELEASE_SHA")"
  [[ "$fetched" == "$target" ]] || { echo "Fetched SHA mismatch." >&2; exit 4; }

  echo "Building release artifact ..."
  compose run --rm --no-deps builder

  printf '%s\n' "$target" > "$publish_dir/RELEASE_SHA"
  artifact_sha="$(hash_publish "$publish_dir")"
  printf '%s\n' "$artifact_sha" > "$publish_dir/RELEASE_ARTIFACT_SHA256"

  cat > "$manifest" <<EOF
release_sha=$target
artifact_sha256=$artifact_sha
source_repo=https://github.com/$REPO_SLUG.git
ci_run=$ci_url
built_at_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF
  chmod 600 "$manifest" "$publish_dir/RELEASE_SHA" "$publish_dir/RELEASE_ARTIFACT_SHA256"
  echo "Built immutable artifact: $artifact_sha"
fi

if [[ "$mode" == "--prepare-only" ]]; then
  echo "prepare_only=PASS"
  echo "release_sha=$target"
  echo "artifact_sha256=$artifact_sha"
  exit 0
fi

previous_sha=""
if [[ -f "$STATE_DIR/current" ]]; then
  previous_sha="$(awk -F= '$1=="release_sha"{print $2}' "$STATE_DIR/current")"
fi
if [[ -z "$previous_sha" ]]; then
  previous_sha="$(docker inspect billing-control-app-1     --format '{{index .Config.Labels "com.billing-control.release-sha"}}' 2>/dev/null || true)"
fi
if [[ -z "$previous_sha" ]] && docker volume inspect billing-control_app_source >/dev/null 2>&1; then
  previous_sha="$(docker run --rm -v billing-control_app_source:/src alpine@sha256:d9e853e87e55526f6b2917df91a2115c36dd7c696a35be12163d44e6e2a4b6bc sh -c     'apk add --no-cache git >/dev/null 2>&1 && git -C /src/repo rev-parse HEAD' 2>/dev/null || true)"
fi

current_env_sha="$(awk -F= '$1=="RELEASE_SHA"{print $2}' "$ENV_FILE")"
if [[ ! "$current_env_sha" =~ ^[0-9a-fA-F]{40}$ ]]; then
  [[ "$previous_sha" =~ ^[0-9a-fA-F]{40}$ ]] || {
    echo "Cannot establish the current production SHA before the backup gate." >&2
    exit 4
  }
  echo "Bootstrapping RELEASE_SHA from currently deployed production: $previous_sha"
  write_env_release "${previous_sha,,}"
fi

echo "Creating required pre-deploy database backup ..."
systemctl start billing-control-backup.service
grep -q '^status=success$' /var/lib/billing-control-backup/last-success

echo "Preparing Data Protection key permissions ..."
compose run --rm --no-deps keys-init

echo "Stopping application before migration ..."
compose stop app

write_env_release "$target"

echo "Running migrations from exact release artifact ..."
if ! compose run --rm --no-deps migrate; then
  echo "Migration failed. Application remains stopped. Review DB compatibility before rollback." >&2
  exit 5
fi

echo "Starting exact release ..."
compose up -d --no-deps --force-recreate app

app_host="$(awk -F= '$1=="APP_HOST"{print $2}' "$ENV_FILE")"
healthy=0
ready_code=""
login_code=""
docker_health=""
for _ in $(seq 1 30); do
  docker_health="$(docker inspect billing-control-app-1 --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' 2>/dev/null || true)"
  ready_code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "https://$app_host/health/ready" || true)"
  login_code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "https://$app_host/Account/Login" || true)"
  if [[ "$docker_health" == "healthy" && "$ready_code" == "200" && "$login_code" == "200" ]]; then
    healthy=1
    break
  fi
  sleep 2
done
if [[ "$healthy" != "1" ]]; then
  compose stop app || true
  echo "Release smoke test failed (docker_health=$docker_health ready=$ready_code login=$login_code); application has been stopped. Database was not restored automatically." >&2
  exit 6
fi

label_sha="$(docker inspect billing-control-app-1   --format '{{index .Config.Labels "com.billing-control.release-sha"}}')"
file_sha="$(docker exec billing-control-app-1 cat /app/RELEASE_SHA)"
file_artifact="$(docker exec billing-control-app-1 cat /app/RELEASE_ARTIFACT_SHA256)"
[[ "$label_sha" == "$target" && "$file_sha" == "$target" && "$file_artifact" == "$artifact_sha" ]] || {
  echo "Runtime release metadata mismatch." >&2
  exit 7
}

deployed_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > "$STATE_DIR/current" <<EOF
release_sha=$target
artifact_sha256=$artifact_sha
previous_sha=$previous_sha
deployed_at_utc=$deployed_at
ci_run=$ci_url
EOF
chmod 600 "$STATE_DIR/current"
printf '%s\t%s\t%s\t%s\n' "$deployed_at" "$target" "$artifact_sha" "$previous_sha" >> "$STATE_DIR/history.tsv"
chmod 600 "$STATE_DIR/history.tsv"

echo "release=PASS"
echo "release_sha=$target"
echo "artifact_sha256=$artifact_sha"
echo "previous_sha=$previous_sha"
echo "docker_health=$docker_health"
echo "ready_code=$ready_code"
echo "login_code=$login_code"

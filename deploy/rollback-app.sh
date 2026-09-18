#!/usr/bin/env bash
set -Eeuo pipefail

PROJECT_DIR="${PROJECT_DIR:-/docker/billing-control}"
RELEASE_ROOT="${RELEASE_ROOT:-$PROJECT_DIR/releases}"
STATE_DIR="${STATE_DIR:-/var/lib/billing-control-release}"
ENV_FILE="${ENV_FILE:-$PROJECT_DIR/.env}"

target="${1:?Usage: rollback-app.sh <40-char-commit-sha> --confirm-db-compatible}"
[[ "${2:-}" == "--confirm-db-compatible" ]] || {
  echo "Application rollback does not roll back the database." >&2
  echo "Re-run with --confirm-db-compatible only after reviewing migration compatibility." >&2
  exit 2
}
[[ "$target" =~ ^[0-9a-fA-F]{40}$ ]] || { echo "Release SHA must be a full 40-character Git commit SHA." >&2; exit 2; }
target="${target,,}"

if [[ -f "$PROJECT_DIR/docker-compose.yml" ]]; then
  compose_file="$PROJECT_DIR/docker-compose.yml"
elif [[ -f "$PROJECT_DIR/docker-compose.yaml" ]]; then
  compose_file="$PROJECT_DIR/docker-compose.yaml"
else
  echo "No production Compose file found." >&2
  exit 2
fi

release_dir="$RELEASE_ROOT/$target"
publish_dir="$release_dir/publish"
manifest="$release_dir/manifest.env"
[[ -f "$manifest" && -f "$publish_dir/RELEASE_SHA" && -f "$publish_dir/RELEASE_ARTIFACT_SHA256" ]] || {
  echo "Prepared release artifact not found for $target." >&2
  exit 3
}

hash_publish() {
  (
    cd "$1"
    find . -type f ! -name RELEASE_ARTIFACT_SHA256 -print0       | LC_ALL=C sort -z       | xargs -0 sha256sum       | sha256sum       | awk '{print $1}'
  )
}

expected="$(awk -F= '$1=="artifact_sha256"{print $2}' "$manifest")"
actual="$(hash_publish "$publish_dir")"
[[ -n "$expected" && "$actual" == "$expected" ]] || {
  echo "Release artifact hash mismatch; rollback refused." >&2
  exit 4
}

current_sha=""
if [[ -f "$STATE_DIR/current" ]]; then
  current_sha="$(awk -F= '$1=="release_sha"{print $2}' "$STATE_DIR/current")"
fi

tmp="$(mktemp)"
awk -v value="$target" '
  BEGIN { found=0 }
  /^RELEASE_SHA=/ { print "RELEASE_SHA=" value; found=1; next }
  { print }
  END { if (!found) print "RELEASE_SHA=" value }
' "$ENV_FILE" > "$tmp"
chmod 600 "$tmp"
mv "$tmp" "$ENV_FILE"

export RELEASE_SHA="$target"
export RELEASE_ROOT

docker compose -f "$compose_file" --project-directory "$PROJECT_DIR"   up -d --no-deps --force-recreate app

app_host="$(awk -F= '$1=="APP_HOST"{print $2}' "$ENV_FILE")"
healthy=0
for _ in $(seq 1 30); do
  code="$(curl -L -sS -o /dev/null -w '%{http_code}' --max-time 10 "https://$app_host/" || true)"
  if [[ "$code" == "200" ]]; then healthy=1; break; fi
  sleep 2
done
if [[ "$healthy" != "1" ]]; then
  docker compose -f "$compose_file" --project-directory "$PROJECT_DIR" stop app || true
  echo "Rollback target failed health check; application stopped." >&2
  exit 5
fi

label_sha="$(docker inspect billing-control-app-1   --format '{{index .Config.Labels "com.billing-control.release-sha"}}')"
[[ "$label_sha" == "$target" ]] || { echo "Runtime SHA mismatch after rollback." >&2; exit 6; }

rolled_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
mkdir -p "$STATE_DIR"
chmod 700 "$STATE_DIR"
cat > "$STATE_DIR/current" <<EOF
release_sha=$target
artifact_sha256=$actual
previous_sha=$current_sha
deployed_at_utc=$rolled_at
rollback=true
EOF
chmod 600 "$STATE_DIR/current"
printf '%s\tROLLBACK\t%s\t%s\t%s\n' "$rolled_at" "$target" "$actual" "$current_sha" >> "$STATE_DIR/history.tsv"
chmod 600 "$STATE_DIR/history.tsv"

echo "rollback=PASS"
echo "release_sha=$target"
echo "from_sha=$current_sha"
echo "https_code=200"

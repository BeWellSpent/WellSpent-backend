#!/usr/bin/env bash
#
# Publish a release's changelog to every WellSpent client.
#
#   ./scripts/changelog.sh publish changelog/server-1.1.0.json
#   ./scripts/changelog.sh --list web
#
# Notes are authored in the repo alongside the code — see docs/features/
# changelog.md in the workspace root — and published here at the moment the
# version actually ships. Publishing before a build reaches users would show a
# reader release notes for something they do not have.
#
# The JSON file looks like:
#
#   {
#     "component": "server",
#     "version": "1.1.0",
#     "releasedAt": "2026-08-25T12:00:00Z",
#     "items": [
#       { "changeType": "added", "summaryEn": "...", "summaryEs": "..." },
#       { "changeType": "fixed", "summaryEn": "..." }
#     ]
#   }
#
# releasedAt is optional and defaults to now. summaryEs is optional; Spanish
# readers fall back to the English text.

set -euo pipefail

API_PROD="https://wellspent-backend-1065664306856.us-central1.run.app"
API_LOCAL="http://localhost:8080"

TARGET="prod"
ASSUME_YES=0
ACTION=""
FILE=""
LIST_COMPONENT=""

usage() {
    cat <<'EOF'
Publish release notes shown in every WellSpent client's "what's new" and Help.

Usage:
  changelog.sh publish <file.json> [options]
  changelog.sh --list [web|ios|server]

Options:
  -e, --env ENV     prod | local (default: prod)
  -y, --yes         skip the confirmation prompt on prod
  -h, --help        this

Auth (first one wins):
  WELLSPENT_TOKEN                     an existing JWT
  WELLSPENT_EMAIL / WELLSPENT_PASSWORD
  otherwise you'll be prompted

Your account needs is_superuser = TRUE. Nothing sets that automatically:
  UPDATE users SET is_superuser = TRUE WHERE email = 'you@example.com';

A version can only be published once — (component, version) is unique, so a
second attempt fails loudly rather than listing the release twice.

Examples:
  changelog.sh publish changelog/web-1.28.0.json
  changelog.sh --list ios
EOF
}

die() { echo "error: $*" >&2; exit 1; }

# Prompts read from the terminal rather than stdin, so piping something into
# this script can't be swallowed as an answer.
TTY_AVAILABLE=0
if : </dev/tty 2>/dev/null; then TTY_AVAILABLE=1; fi

require_tty() {
    [[ "$TTY_AVAILABLE" -eq 1 ]] || die "$1"
}

ask() {
    local __var="$1" __prompt="$2" __silent="${3:-}" __value
    require_tty "no terminal to prompt on — set WELLSPENT_TOKEN, or WELLSPENT_EMAIL and WELLSPENT_PASSWORD"
    if [[ -n "$__silent" ]]; then
        read -r -s -p "$__prompt" __value </dev/tty
        echo >&2
    else
        read -r -p "$__prompt" __value </dev/tty
    fi
    printf -v "$__var" '%s' "$__value"
}

# ── Arguments ────────────────────────────────────────────────────────────────

while [[ $# -gt 0 ]]; do
    case "$1" in
        publish) ACTION="publish"; FILE="${2:-}"; shift 2 || true ;;
        --list) ACTION="list"; LIST_COMPONENT="${2:-}"; shift; [[ -n "$LIST_COMPONENT" && "$LIST_COMPONENT" != -* ]] && shift || LIST_COMPONENT="" ;;
        -e|--env) TARGET="${2:-}"; shift 2 ;;
        -y|--yes) ASSUME_YES=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) die "unexpected argument: $1 (try --help)" ;;
    esac
done

[[ -n "$ACTION" ]] || { usage; exit 1; }

case "$TARGET" in
    prod) API_URL="$API_PROD" ;;
    local) API_URL="$API_LOCAL" ;;
    *) die "unknown env: $TARGET (prod | local)" ;;
esac

command -v curl >/dev/null || die "curl is required"
command -v python3 >/dev/null || die "python3 is required (used to build and read JSON safely)"

# ── Auth ─────────────────────────────────────────────────────────────────────

get_token() {
    if [[ -n "${WELLSPENT_TOKEN:-}" ]]; then
        printf '%s' "$WELLSPENT_TOKEN"
        return
    fi

    local email="${WELLSPENT_EMAIL:-}"
    local password="${WELLSPENT_PASSWORD:-}"

    [[ -n "$email" ]] || ask email "Email: "
    [[ -n "$password" ]] || ask password "Password: " silent

    local body
    body=$(python3 -c 'import json,sys; print(json.dumps({"email": sys.argv[1], "password": sys.argv[2], "rememberMe": False}))' "$email" "$password")

    local response
    response=$(curl -sS -X POST "$API_URL/wellspent.v1.AuthService/Login" \
        -H "Content-Type: application/json" -d "$body")

    python3 -c '
import json, sys
try:
    data = json.loads(sys.argv[1])
except ValueError:
    sys.exit("login failed: unreadable response from " + sys.argv[2])
token = data.get("accessToken")
if not token:
    sys.exit("login failed: " + (data.get("message") or json.dumps(data)))
print(token)
' "$response" "$API_URL"
}

# ── Actions ──────────────────────────────────────────────────────────────────

if [[ "$ACTION" == "list" ]]; then
    TOKEN=$(get_token)
    BODY=$(python3 -c '
import json, sys
component = sys.argv[1]
req = {}
if component:
    req["components"] = ["CHANGELOG_COMPONENT_" + component.upper()]
print(json.dumps(req))
' "$LIST_COMPONENT")

    RESPONSE=$(curl -sS -X POST "$API_URL/wellspent.v1.ChangelogService/ListChangelog" \
        -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "$BODY")

    python3 -c '
import json, sys
try:
    data = json.loads(sys.argv[1])
except ValueError:
    sys.exit("unreadable response: " + sys.argv[1][:400])
if "releases" not in data:
    sys.exit("failed: " + (data.get("message") or json.dumps(data)))
print("server is running " + (data.get("currentServerVersion") or "?"))
releases = data.get("releases") or []
if not releases:
    print("no releases published yet")
for r in releases:
    component = (r.get("component") or "").replace("CHANGELOG_COMPONENT_", "").lower()
    print("\n%s %s  (%s)" % (component, r.get("version"), (r.get("releasedAt") or "")[:10]))
    for item in r.get("items") or []:
        kind = (item.get("changeType") or "").replace("CHANGE_TYPE_", "").lower()
        print("  [%s] %s" % (kind, item.get("summaryEn")))
' "$RESPONSE"
    exit 0
fi

# publish
[[ -n "$FILE" ]] || die "publish needs a file (try --help)"
[[ -f "$FILE" ]] || die "no such file: $FILE"

# Validated before login, so a malformed file fails before anyone types a
# password.
SUMMARY=$(python3 -c '
import json, sys
path = sys.argv[1]
with open(path) as fh:
    try:
        data = json.load(fh)
    except ValueError as exc:
        sys.exit("%s is not valid JSON: %s" % (path, exc))

component = data.get("component")
if component not in ("web", "ios", "server"):
    sys.exit("component must be one of web, ios, server (got %r)" % (component,))
version = (data.get("version") or "").strip()
if not version:
    sys.exit("version is required")
items = data.get("items") or []
if not items:
    sys.exit("at least one item is required — a release with nothing to say would show an empty what is new")
for item in items:
    if item.get("changeType") not in ("added", "fixed", "changed"):
        sys.exit("changeType must be one of added, fixed, changed (got %r)" % (item.get("changeType"),))
    if not (item.get("summaryEn") or "").strip():
        sys.exit("every item needs a summaryEn")
print("%s %s — %d item(s)" % (component, version, len(items)))
' "$FILE")

echo "$SUMMARY"
echo "target: $TARGET ($API_URL)"

if [[ "$TARGET" == "prod" && "$ASSUME_YES" -eq 0 ]]; then
    require_tty "refusing to publish to prod without a terminal to confirm on — pass --yes"
    ask CONFIRM "Publish to PROD? [y/N] "
    [[ "$CONFIRM" == "y" || "$CONFIRM" == "Y" ]] || die "cancelled"
fi

TOKEN=$(get_token)

BODY=$(python3 -c '
import json, sys
with open(sys.argv[1]) as fh:
    data = json.load(fh)
req = {
    "component": "CHANGELOG_COMPONENT_" + data["component"].upper(),
    "version": data["version"].strip(),
    "items": [
        {
            "changeType": "CHANGE_TYPE_" + item["changeType"].upper(),
            "summaryEn": item["summaryEn"].strip(),
            "summaryEs": (item.get("summaryEs") or "").strip(),
        }
        for item in data["items"]
    ],
}
if data.get("releasedAt"):
    req["releasedAt"] = data["releasedAt"]
print(json.dumps(req))
' "$FILE")

RESPONSE=$(curl -sS -X POST "$API_URL/wellspent.v1.ChangelogService/CreateChangelogRelease" \
    -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "$BODY")

python3 -c '
import json, sys
try:
    data = json.loads(sys.argv[1])
except ValueError:
    sys.exit("unreadable response: " + sys.argv[1][:400])
release = data.get("release")
if not release:
    sys.exit("failed: " + (data.get("message") or json.dumps(data)))
component = (release.get("component") or "").replace("CHANGELOG_COMPONENT_", "").lower()
print("Published: %s %s (%d items)" % (component, release.get("version"), len(release.get("items") or [])))
' "$RESPONSE"

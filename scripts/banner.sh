#!/usr/bin/env bash
#
# Post a status banner to every WellSpent client.
#
#   ./scripts/banner.sh red "Transactions aren't loading. We're on it."
#   ./scripts/banner.sh yellow "Bank syncing is delayed." -d 6
#   ./scripts/banner.sh --list
#   ./scripts/banner.sh --expire <id>
#
# See docs/features/status-banner.md in the workspace root for the full spec.

set -euo pipefail

API_PROD="https://wellspent-backend-1065664306856.us-central1.run.app"
API_LOCAL="http://localhost:8080"

TARGET="prod"
HOURS=24
MESSAGE_ES=""
ASSUME_YES=0
ACTION="post"
EXPIRE_ID=""
SEVERITY=""
MESSAGE=""

usage() {
    cat <<'EOF'
Post a status banner shown at the top of every WellSpent client.

Usage:
  banner.sh <severity> <message> [options]
  banner.sh --list
  banner.sh --expire <banner-id>

Severity (either spelling works):
  green | info        everything is fine, just letting you know
  yellow | warning    degraded, but the app works
  red | critical      something is badly broken (cannot be dismissed by users)

Options:
  -d, --hours N     how long it stays up (default: 24)
  -s, --es TEXT     Spanish text (default: Spanish readers see the English)
  -e, --env ENV     prod | local (default: prod)
  -y, --yes         skip the confirmation prompt on prod
  -h, --help        this

Credentials, in order of preference:
  WELLSPENT_TOKEN                     an existing JWT
  WELLSPENT_EMAIL / WELLSPENT_PASSWORD
  otherwise you'll be prompted

Your account needs is_superuser = TRUE. Nothing sets that automatically:
  UPDATE users SET is_superuser = TRUE WHERE email = 'you@example.com';

Examples:
  banner.sh red "Transactions aren't loading. We're on it."
  banner.sh yellow "Bank syncing is delayed." -d 6 -s "La sincronizacion esta retrasada."
  banner.sh green "Everything's back to normal." -d 2
EOF
}

die() { echo "error: $*" >&2; exit 1; }

# Prompts read from the terminal rather than stdin, so piping something into
# this script can't be swallowed as an answer. In a cron job or CI there is no
# terminal to prompt on, so those callers have to supply everything up front.
TTY_AVAILABLE=0
if : </dev/tty 2>/dev/null; then TTY_AVAILABLE=1; fi

require_tty() {
    [[ "$TTY_AVAILABLE" -eq 1 ]] || die "$1"
}

# ask <varname> <prompt> [silent]
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

[[ $# -eq 0 ]] && { usage; exit 1; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        -h|--help)   usage; exit 0 ;;
        --list)      ACTION="list"; shift ;;
        --expire)    ACTION="expire"; EXPIRE_ID="${2:-}"; [[ -n "$EXPIRE_ID" ]] || die "--expire needs a banner id"; shift 2 ;;
        -d|--hours)  HOURS="${2:-}"; shift 2 ;;
        -s|--es)     MESSAGE_ES="${2:-}"; shift 2 ;;
        -e|--env)    TARGET="${2:-}"; shift 2 ;;
        -y|--yes)    ASSUME_YES=1; shift ;;
        -*)          die "unknown option: $1" ;;
        *)
            if   [[ -z "$SEVERITY" ]]; then SEVERITY="$1"
            elif [[ -z "$MESSAGE"  ]]; then MESSAGE="$1"
            else die "unexpected argument: $1"
            fi
            shift ;;
    esac
done

case "$TARGET" in
    prod)    API_URL="$API_PROD" ;;
    local)   API_URL="$API_LOCAL" ;;
    *)       die "--env must be prod or local (got: $TARGET)" ;;
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
    # Silent, so the password never lands in the terminal scrollback.
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

# Pretty-print a response. The success header is printed here, not by the
# caller, so a failed request never gets announced as "Posted:" first.
show() {
    python3 -c '
import json, sys
raw, key, header = sys.argv[1], sys.argv[2], sys.argv[3]
try:
    data = json.loads(raw)
except ValueError:
    sys.exit("unreadable response: " + raw[:400])

if "code" in data or ("message" in data and key not in data and "banners" not in data):
    sys.exit("request failed: " + (data.get("message") or json.dumps(data)))

print(header)

def line(b):
    sev = b.get("severity", "?").replace("STATUS_BANNER_SEVERITY_", "").lower()
    return "  [{}] {}\n      {} -> {}\n      id: {}".format(
        sev, b.get("messageEn", ""), b.get("startsAt", "?"), b.get("endsAt", "?"), b.get("id", "?"))

if key == "banners":
    banners = data.get("banners") or []
    if not banners:
        print("No banners have ever been posted.")
    for b in banners:
        print(line(b))
else:
    b = data.get(key)
    if not b:
        print("Nothing is live right now.")
    else:
        print(line(b))
' "$1" "$2" "$3"
}

# ── Actions ──────────────────────────────────────────────────────────────────

case "$ACTION" in
list)
    TOKEN=$(get_token)
    RESPONSE=$(curl -sS -X POST "$API_URL/wellspent.v1.StatusService/ListStatusBanners" \
        -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"limit":20}')
    show "$RESPONSE" banners "Banners on $TARGET, newest first:"
    exit 0
    ;;

expire)
    TOKEN=$(get_token)
    BODY=$(python3 -c 'import json,sys; print(json.dumps({"id": sys.argv[1]}))' "$EXPIRE_ID")
    RESPONSE=$(curl -sS -X POST "$API_URL/wellspent.v1.StatusService/ExpireStatusBanner" \
        -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "$BODY")
    show "$RESPONSE" banner "Taken down on $TARGET:"
    exit 0
    ;;
esac

# ── Post ─────────────────────────────────────────────────────────────────────

[[ -n "$SEVERITY" ]] || die "missing severity (green, yellow or red) — see --help"
[[ -n "$MESSAGE"  ]] || die "missing message — see --help"

case "$(printf '%s' "$SEVERITY" | tr '[:upper:]' '[:lower:]')" in
    green|info)      PROTO_SEVERITY="STATUS_BANNER_SEVERITY_INFO" ;;
    yellow|warning)  PROTO_SEVERITY="STATUS_BANNER_SEVERITY_WARNING" ;;
    red|critical)    PROTO_SEVERITY="STATUS_BANNER_SEVERITY_CRITICAL" ;;
    *) die "severity must be green/info, yellow/warning or red/critical (got: $SEVERITY)" ;;
esac

[[ "$HOURS" =~ ^[0-9]+$ ]] && [[ "$HOURS" -gt 0 ]] || die "--hours must be a positive whole number (got: $HOURS)"

# The server caps this too; checking here means a too-long message is caught
# before you've typed a password. Counted in characters, matching the server.
python3 -c '
import sys
for label, text in (("message", sys.argv[1]), ("Spanish message", sys.argv[2])):
    if len(text) > 300:
        sys.exit("error: {} is {} characters; the limit is 300".format(label, len(text)))
' "$MESSAGE" "$MESSAGE_ES"

# GNU and BSD date disagree on relative-time flags, so let python do it.
ENDS_AT=$(python3 -c '
import datetime, sys
end = datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(hours=int(sys.argv[1]))
print(end.strftime("%Y-%m-%dT%H:%M:%SZ"))
' "$HOURS")

echo
echo "  target:   $TARGET  ($API_URL)"
echo "  severity: $PROTO_SEVERITY"
echo "  message:  $MESSAGE"
[[ -n "$MESSAGE_ES" ]] && echo "  spanish:  $MESSAGE_ES"
echo "  until:    $ENDS_AT  (${HOURS}h from now)"
echo

# Only prod asks. It's reversible (--expire takes it down), but it is visible
# to every user the moment it lands, so a stray shell-history re-run shouldn't
# silently publish.
if [[ "$TARGET" == "prod" && "$ASSUME_YES" -eq 0 ]]; then
    require_tty "refusing to post to prod with no terminal to confirm on — pass -y if you're sure"
    ask reply "Show this to every user on production? [y/N] "
    [[ "$reply" =~ ^[Yy]$ ]] || { echo "Cancelled."; exit 1; }
fi

TOKEN=$(get_token)

BODY=$(python3 -c '
import json, sys
payload = {
    "severity": sys.argv[1],
    "messageEn": sys.argv[2],
    "endsAt": sys.argv[4],
}
if sys.argv[3]:
    payload["messageEs"] = sys.argv[3]
print(json.dumps(payload))
' "$PROTO_SEVERITY" "$MESSAGE" "$MESSAGE_ES" "$ENDS_AT")

RESPONSE=$(curl -sS -X POST "$API_URL/wellspent.v1.StatusService/CreateStatusBanner" \
    -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "$BODY")

show "$RESPONSE" banner "Posted:"
echo
echo "Take it down early with:  ./scripts/banner.sh --expire <id> --env $TARGET"

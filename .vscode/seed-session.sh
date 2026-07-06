#!/usr/bin/env bash
# Dev helper: there is no capture/upload UI yet, so this creates a session through
# the API and prints a browser-console snippet that stores the host identity and
# opens the session. OCR of the stub image fails (or is skipped if the sidecar is
# down), which lands the session in Review - exactly the M4 host gate to look at.
set -euo pipefail

API="${API_BASE:-http://localhost:5205}"
WEB="${WEB_BASE:-http://localhost:5173}"

tmp="$(mktemp --suffix=.jpg)"
trap 'rm -f "$tmp"' EXIT
# A minimal JPEG header - enough to pass the magic-byte sniff at upload.
printf '\xFF\xD8\xFF\xE0\x00\x10JFIF\x00' >"$tmp"

echo "Creating a session at $API ..."
resp="$(curl -fsS -X POST -F "image=@${tmp};type=image/jpeg" "$API/api/v1/sessions")" || {
  echo "Create failed - is the API (and Redis) running? Start 'dev: all' first."
  exit 1
}

echo "$resp"

id="$(printf '%s' "$resp" | sed -n 's/.*"sessionId":"\([^"]*\)".*/\1/p')"
pid="$(printf '%s' "$resp" | sed -n 's/.*"participantId":"\([^"]*\)".*/\1/p')"
tok="$(printf '%s' "$resp" | sed -n 's/.*"participantToken":"\([^"]*\)".*/\1/p')"

if [ -z "$id" ]; then
  echo "Could not parse the session id from the response."
  exit 1
fi

cat <<EOF

Open ${WEB}/s/${id} then paste this into the browser console to view it as the host:

localStorage.setItem('bs:${id}', JSON.stringify({participantId:'${pid}',participantToken:'${tok}'})); location.reload()
EOF

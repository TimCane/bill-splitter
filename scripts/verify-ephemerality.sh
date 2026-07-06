#!/usr/bin/env bash
# Ephemerality gate (docs/10-security-privacy.md#ephemerality-guarantees, M7 A5).
# One hour after a finalized session, Redis holds no keys and the MinIO bucket is
# empty - if either has state, the delete/TTL path is broken. Run from the repo
# root against the running prod stack, with the deploy .env loaded so the MinIO
# credentials are set.
set -euo pipefail

COMPOSE=${COMPOSE:-docker compose -f docker-compose.prod.yml}
BUCKET=${BUCKET:-bill-splitter}

keys=$($COMPOSE exec -T redis redis-cli --scan | grep -c . || true)
echo "redis keys: $keys"

# minio has no mc; run a throwaway mc container on the stack network instead.
objects=$($COMPOSE run --rm --entrypoint sh minio-init -c "
  mc alias set local http://minio:9000 \$MINIO_ROOT_USER \$MINIO_ROOT_PASSWORD >/dev/null &&
  mc ls --recursive local/$BUCKET | grep -c . || true
")
echo "minio objects: $objects"

if [ "$keys" != "0" ] || [ "$objects" != "0" ]; then
  echo "FAIL: state remains after finalize + TTL - ephemerality is broken" >&2
  exit 1
fi

echo "PASS: redis and the $BUCKET bucket are empty"

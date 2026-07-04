# Deployment

Single VPS, docker compose, reverse proxy with TLS (Coolify/Traefik or
Caddy - anything that terminates TLS and forwards WebSockets). One backend
instance by design ([10-security-privacy.md](10-security-privacy.md) sizes
the defenses to this).

## Topology

```
internet ──TLS──► reverse proxy ──► app (Kestrel :8080)
                                     │  serves SPA static files + /api + /hubs
                                     ├──► ocr   (internal only)
                                     ├──► redis (internal only)
                                     └──► minio (internal only)
```

- The SPA is built (`vite build`) into the backend image and served by
  ASP.NET static files with SPA fallback - one origin, no CORS, one
  deployable.
- `ocr`, `redis`, `minio` publish **no** host ports; only the proxy and
  `app` are reachable.
- The proxy must forward `Upgrade`/`Connection` headers (WebSockets) and
  set `X-Forwarded-For`/`-Proto`; do not log query strings
  (`access_token` - [10-security-privacy.md](10-security-privacy.md#transport-and-headers)).

## Production compose shape

`docker-compose.prod.yml` at repo root (compose-style skill applies):

| Service | Image | Notes |
| --- | --- | --- |
| `app` | built from `backend/Dockerfile` (multi-stage: pnpm build SPA -> dotnet publish -> aspnet runtime) | `restart: unless-stopped`, healthcheck `GET /healthz` |
| `ocr` | `ghcr.io/timcane/bill-splitter-ocr:latest` ([12-ci.md](12-ci.md)) | healthcheck `GET /healthz` |
| `redis` | `redis:7-alpine` | `command: redis-server --save "" --appendonly no` - **no persistence, deliberately**; no volume |
| `minio` | `minio/minio` | volume for `/data` (objects die by lifecycle rule, but MinIO itself needs its config dir); console not exposed |
| `minio-init` | `minio/mc` | one-shot: create bucket `bill-splitter` + set 1-day expiry lifecycle rule |

Redis maxmemory guard: `--maxmemory 256mb --maxmemory-policy allkeys-lru` -
if the box is ever flooded, the oldest sessions die first instead of the
process.

## Environment

`.env.example` at repo root documents every variable; values here are the
required set:

| Variable | Consumer | Example |
| --- | --- | --- |
| `App__PublicBaseUrl` | app | `https://split.example.com` |
| `Redis__ConnectionString` | app | `redis:6379` |
| `Minio__Endpoint` | app | `http://minio:9000` |
| `Minio__AccessKey` / `Minio__SecretKey` | app, minio | generated per deploy |
| `Minio__Bucket` | app | `bill-splitter` |
| `Ocr__BaseUrl` | app | `http://ocr:8000` |
| `Smtp__Host` / `Smtp__Port` / `Smtp__Username` / `Smtp__Password` / `Smtp__From` | app | any transactional relay; leave unset to disable email (finalize hides the field via a `/healthz`-exposed capability flag) |
| `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` | minio | generated per deploy |

Session tuning (`Session__*`, `Ocr__*` limits) has sane defaults baked into
`appsettings.json` ([07-backend-design.md](07-backend-design.md#configuration));
override only with reason.

## Operations

- **Deploy**: `docker compose -f docker-compose.prod.yml up -d --build`
  (or the Coolify equivalent). A deploy restarts `app` and drops live
  SignalR connections; clients auto-reconnect and re-snapshot - mid-meal
  deploys are rude but not destructive. Live sessions survive (they are in
  Redis, which keeps running).
- **Backups**: none. There is nothing to back up; that is the product.
- **Logs**: container stdout, platform-collected. The no-PII logging rule
  ([10-security-privacy.md](10-security-privacy.md#ephemerality-guarantees))
  is what makes this safe.
- **Monitoring (MVP)**: uptime check on `/healthz`; disk alert for the
  MinIO volume (should hover near zero - growth means the delete path is
  broken and ephemerality is silently failing).

## Scale-out (documented exit, not built)

When one instance stops being enough: add the SignalR Redis backplane
(`AddStackExchangeRedis` on the existing Redis), run N `app` replicas
behind the proxy. Session CAS already tolerates multi-writer. Nothing else
changes - this is a config flip plus a package reference, which is why it
is not built now.

# ADR-0004: Ephemeral by design - no database, TTL on everything

Status: accepted, 2026-07-04

## Context

The product handles a receipt, first names, and (transiently) one email
address. Its differentiator is that nothing persists. A conventional
Postgres-backed design would make persistence the default and deletion a
feature to build and verify forever.

## Decision

No database. Redis (persistence disabled: `save ""`, `appendonly no`) is
the only session store; every key carries a TTL from the moment it is
written (24h; shrunk to 1h at finalize). The receipt image lives in MinIO
only between upload and the end of host review, with a 1-day bucket
lifecycle rule as backstop. The email address is never written anywhere -
it exists only inside the finalize request. Logs carry ids and states,
never names, addresses, or image bytes.

## Consequences

- Deletion is structural (TTL at write time), not a cleanup job that can
  silently break.
- A Redis restart or crash loses live sessions - accepted; a table mid-
  split re-scans the receipt. There are no backups because there is
  nothing to back up.
- No analytics, no history, no "past splits" feature is possible without
  revisiting this ADR.
- Honest boundary, stated in the docs and UI: a sent summary email and the
  SMTP provider's delivery logs are outside our control - "no persistent
  data" means our infrastructure retains nothing.

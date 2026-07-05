# ADR-0003: Session as one JSON document with Lua CAS

Status: accepted, 2026-07-04

## Context

Session state (participants, items, claims, bill) needs concurrent-safe
mutation in Redis - simultaneous claims on the same item are the normal
case. Candidates: granular hashes with per-operation Lua scripts, a single
JSON document with optimistic versioning, or in-process locks (single
instance only).

## Decision

Serialize the whole `Session` aggregate as one JSON string at
`session:{id}` with a `version` field. Every write is read -> mutate
in C# -> compare-and-swap via one small Lua script (version check + SET
KEEPTTL), retried up to 5 times on conflict.

## Consequences

- One key = one TTL = the ephemerality story stays trivial.
- Domain rules run in C# on a materialized aggregate - no logic trapped in
  Lua; the only script is the 7-line CAS.
- Full-document rewrite per mutation costs a few KB per write - irrelevant
  at table scale (measured worst case ~40KB).
- Contention is retry-based, not lock-based; it survives scale-out to
  multiple writers unchanged.
- StackExchange.Redis's awkward WATCH/MULTI model is avoided entirely.

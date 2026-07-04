# bill-splitter

Ephemeral bill-splitting app: scan a receipt, the table claims items from
their phones, totals reconcile exactly, everything self-destructs. C#
backend + React SPA + PaddleOCR sidecar; Redis is the only store.

## Read first

`docs/` is the contract, not commentary - it was written to hand this
project off with zero ambiguity. Start at `docs/00-overview.md` (document
map inside). `docs/14-build-order.md` says what to build next and when a
milestone counts as done. Decisions with a "why" live in `docs/adr/`.

## Hard rules

- **No persistence.** No database, no EF Core, no ORM. Redis with TTLs and
  transient MinIO objects only (`docs/adr/0004-ephemeral-by-design.md`).
  Never log display names, email addresses, or image bytes.
- **No payments.** The app reports amounts; it never moves money.
- **Server owns all math.** Totals are computed only in
  `SplitCalculator`; the frontend formats, never calculates.
- **All money is integer minor units** - anywhere a `decimal` or float
  creeps into an amount, it is a bug.
- **Contracts are docs-first.** Changing an endpoint, hub event, Redis key,
  or DTO means updating `docs/03`-`docs/06` in the same PR.

## Conventions

- House skills apply: `dotnet-style` (minus EF/Postgres - this repo has no
  database), `react-style`, `commit-style`, `pr-style`,
  `devcontainer-setup`, `compose-style`.
- Monorepo: `backend/` (.NET 10), `frontend/` (Vite + React, pnpm),
  `ocr/` (Python FastAPI + PaddleOCR).
- Work on feature branches; PRs squash-merge into `main`.

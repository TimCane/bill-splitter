# UX flows and screens

Mobile-first. Wireframes are content contracts: every element listed must
exist; layout/polish is the implementer's (shadcn defaults are fine).
Currency shown as `£12.50` style via `Intl.NumberFormat`.

## Route/state/role matrix

`/s/:sessionId` renders by `(snapshot.state, isHost, hasToken)`:

| State | Host | Participant (token) | Visitor (no token) |
| --- | --- | --- | --- |
| `Processing` | Processing screen | - (none exist yet) | Holding card |
| `Review` | Review screen | - | Holding card |
| `Open` | Claim screen (+ Share sheet, Finalize) | Claim screen | Join prompt -> Claim |
| `Finalized` | Summary | Summary | Summary (read-only) |
| expired/404 | Expired card | Expired card | Expired card |

## 1. Landing / Capture - `/`

```
┌──────────────────────────────┐
│  Bill Splitter               │
│  Split the bill. Keep        │
│  nothing.                    │
│                              │
│   ┌───────────────────────┐  │
│   │  [camera]  Scan a     │  │
│   │            receipt    │  │
│   └───────────────────────┘  │
│                              │
│   Joining someone's split?   │
│   [ Enter code ]             │
│                              │
│   Photos are OCR'd on our    │
│   server and deleted before  │
│   anyone joins. Sessions     │
│   self-destruct in 24h.      │
└──────────────────────────────┘
```

- **Scan a receipt**: file input (`accept="image/*" capture="environment"`)
  -> preview screen: the photo, `Use photo` / `Retake`. `Use photo` runs
  client preprocessing ([08-frontend-design.md](08-frontend-design.md#uploads)),
  uploads with a progress bar, stores the returned token, navigates to
  `/s/:id`.
- **Enter code** -> `/join`.
- States: upload progress (determinate bar), upload error (toast + stay),
  rate-limited (`Too many sessions from this network - try again in a
  few minutes.`).

## 2. Join by code - `/join`

Single input, 6 chars, auto-uppercase, alphabet-filtered; `Join` button
disabled until 6 chars. Unknown code: inline `That code didn't match an
active split.` On resolve -> `/s/:id`.

## 3. Processing - host at `/s/:id`, state `Processing`

```
┌──────────────────────────────┐
│  Reading your receipt...     │
│  [spinner]                   │
│  Usually takes a few seconds.│
└──────────────────────────────┘
```

- Live via `OcrStatusChanged`; no polling, no button.
- `Failed` -> drops into Review with the amber banner (below); there is no
  dead end.

## 4. Review (host gate) - state `Review`

```
┌──────────────────────────────┐
│  Check the items      [photo]│  <- opens receipt image in a sheet
│  You: Host             [edit]│  <- tap to set your own name
│ ┌──────────────────────────┐ │
│ │ (i) Items + extras match │ │  <- checksum banner, green
│ │     the total. £54.50    │ │
│ └──────────────────────────┘ │
│  ITEMS                       │
│  Margherita        £12.50 [>]│  <- tap row = edit sheet
│  2x Peroni 660ml   £11.00 [>]│
│  ...                         │
│  [+ Add item]                │
│                              │
│  EXTRAS                      │
│  Tax £0.00  Tip £5.00        │
│  Service £0.00               │
│  Printed total     £54.50 [>]│  <- edit sheet: extras+total+currency
│                              │
│  [    Open the split    ]    │
└──────────────────────────────┘
```

- Host name: the `You: {name}` row (default `Host`) taps to rename via
  `PUT .../participants/me`
  ([04-api-contract.md](04-api-contract.md#put-apiv1sessionssessionidparticipantsme));
  the host may fix it here or later from the Claim screen header.
- Checksum banner variants (advisory, never blocks):
  - green: `Items + extras match the total. {total}`
  - amber: `Items + extras are {delta} {over|under} the printed total.
    Fix items or edit the total.`
  - amber (OCR failed): `Couldn't read the receipt. Add items by hand, or
    start over with a better photo.`
- Parser warnings render under the banner as dismissible rows (`We skipped
  a line that looked like "{text}".`).
- Edit item sheet: name, quantity (stepper), price (locale money input);
  Delete inside the sheet. Add item = same sheet, empty.
- `Open the split` -> confirm dialog: `Ready? Once open, items can't be
  edited and the receipt photo is deleted.` -> `POST /open` -> Share
  sheet, state now `Open`.

## 5. Share (host sheet, on entering `Open`, reopenable)

```
┌──────────────────────────────┐
│  Get the table in            │
│    ┌──────────────┐          │
│    │   [QR code]  │          │
│    └──────────────┘          │
│  or enter code   K7MPQ2      │
│  at {host}/join              │
│  [ Copy link ]  [ Done ]     │
└──────────────────────────────┘
```

## 6. Join prompt (visitor with no token, state `Open`)

Name input (1-30 chars) + `Join the split`. Errors: session full
(`This split already has 20 people.`), finalized meanwhile -> Summary.

## 7. Claim - state `Open` (the main screen, everyone)

```
┌──────────────────────────────┐
│ Split K7MPQ2        [● live] │  <- short code; connection pill: live/reconnecting
│ You: Tim        Your £20.09  │  <- sticky header, live Open total
├──────────────────────────────┤
│ Margherita            £12.50 │
│ [ Mine ]              Tim ●  │  <- claimed by me: filled; shows share £
│──────────────────────────────│
│ 2x Peroni 660ml       £11.00 │
│ [ Mine ]        Tim ● Sam ●  │  <- avatars = claimant initials
│   your share £5.50  [shares:1│  <- share stepper appears when claimed
│──────────────────────────────│
│ Tiramisu              £6.50  │
│ [ Mine ]          unclaimed  │
├──────────────────────────────┤
│ Unclaimed £6.50 of £54.50    │  <- sticky footer
│ [ Everyone ▾ ]               │  <- expands totals list (all people)
│ [ Finalize ]  (host only)    │
└──────────────────────────────┘
```

Element inventory:

- Connection pill: maps `useSession`'s `connecting | connected |
  reconnecting | disconnected` status
  ([08-frontend-design.md](08-frontend-design.md#server-state)) to labels
  `connecting` (muted, spinner, initial handshake) / `live` (green) /
  `reconnecting` (amber, spinner) / `offline` (red after a connect or
  reconnect gives up; tap = reload).
- Header name: the `You: {name}` label taps to rename via
  `PUT .../participants/me` - the one place the host can fix a still-`Host`
  name once the split is `Open`.
- Item row: name (+`2x` qty prefix), price, claim button (`Mine` /
  `Claimed` filled state), claimant chips with initials, your share amount
  when you claim, shares stepper (`- 1 +`) revealed while claimed by you
  (calls `SetShares`; min 1, max 99; unclaim via the claim button toggle).
- When another claimant joins/leaves an item you claimed, your share
  amount animates (brief highlight) - retroactive change must read as
  intentional ([known risk](00-overview.md#known-risks)).
- Totals drawer (`Everyone`): per person - name, items £, extras £, total
  £; plus the unclaimed line. Data straight from `snapshot.totals`.
- Finalize (host): confirm dialog `Lock the split? Unclaimed £6.50 gets
  split between everyone.` + optional email input labeled `Email me the
  summary (optional)` + note `Deleted after sending. The split stays
  viewable for 1 hour.` -> `POST /finalize`.
- Empty states: no participants yet (host sees `Waiting for people to
  join - show them the QR` + reopen-Share button).

## 8. Summary - state `Finalized`

```
┌──────────────────────────────┐
│  Split locked  ✓             │
│  Total              £54.50   │
│                              │
│  Tim   £21.72                │
│   Margherita £12.50          │
│   Peroni £5.50               │
│   + tip £2.09                │  <- expandable per person
│   + unclaimed £1.63          │
│  Sam   £16.70               ▾│
│                              │
│  Unclaimed £6.50 was split   │
│  between 4 people.           │
│                              │
│  Gone in 58 min. Screenshot  │
│  or email it before then.    │  <- live countdown from TTL
└──────────────────────────────┘
```

- Read-only for everyone including host. Countdown formatted from the
  snapshot's `expiresAt` - the client never computes expiry itself (the
  finalized TTL is server configuration).
- If an email was requested the host sees `Summary sent to t***@e***.com`
  (masked client-side from the address typed in the finalize dialog - the
  API never returns the address, so the confirmation does not survive a
  refresh; that is fine).

## 9. Expired / not found

`This split has expired - bills don't live long here.` + `Start a new
split` button. Served for 404 on load and `session-not-found` mid-session.

## Copy rules

- Sentence case, no exclamation marks, dry tone (examples above are the
  actual copy).
- Amounts always through `formatMinor`; never hand-formatted.
- The ephemerality promise appears in exactly three places: landing
  footnote, open-confirm dialog, summary countdown. Do not sprinkle it
  further.

import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'

import { decodablePng } from '../lib/receipt.mjs'
import {
  addItem,
  claimItem,
  createSession,
  enterAsHost,
  finalize,
  joinAsGuest,
  launchBrowser,
  newPage,
  openSplit,
  readLockedTotals,
  waitForClaimant,
  waitForText,
} from '../lib/flow.mjs'

// Two-phone split (docs/11-testing-strategy.md#end-to-end-puppeteer, M7 A1):
// host creates and opens, a second phone joins and claims, the host sees the
// claim and finalizes, and both phones show identical locked totals.
let browser

before(async () => {
  browser = await launchBrowser()
})

after(async () => {
  await browser?.close()
})

test('two phones split a bill to identical locked totals', async () => {
  const host = await createSession(decodablePng())

  const hostPage = await newPage(browser)
  await enterAsHost(hostPage, host)

  // A decodable but textless image parses to an empty Review; the host adds the
  // one item the two phones will split.
  await waitForText(hostPage, 'Check the items')
  await addItem(hostPage, { name: 'Pizza', price: '20.00' })
  await openSplit(hostPage)

  const guestPage = await newPage(browser)
  await joinAsGuest(guestPage, host.sessionId, 'Riker')

  await claimItem(guestPage, 'Pizza')

  // The realtime path (design SLA: under 2s) carries the claim to the host.
  await waitForClaimant(hostPage, 'Riker')

  await finalize(hostPage)
  await waitForText(guestPage, 'Split locked')

  const hostTotals = await readLockedTotals(hostPage)
  const guestTotals = await readLockedTotals(guestPage)
  assert.ok(hostTotals.length > 0, 'the finalized screen lists per-person totals')
  assert.deepEqual(hostTotals, guestTotals, 'both phones show identical locked totals')
})

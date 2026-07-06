import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'

import { undecodableJpeg } from '../lib/receipt.mjs'
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

// Manual-entry path (docs/11-testing-strategy.md#end-to-end-puppeteer, M7 A1):
// an unreadable image fails OCR, the host builds the bill by hand, and the split
// finalizes like any other.
let browser

before(async () => {
  browser = await launchBrowser()
})

after(async () => {
  await browser?.close()
})

test('a failed OCR parse recovers through manual entry', async () => {
  const host = await createSession(undecodableJpeg())

  const hostPage = await newPage(browser)
  await enterAsHost(hostPage, host)

  // The sidecar cannot decode the image, so the session lands in Review Failed.
  await waitForText(hostPage, "Couldn't read the receipt")

  await addItem(hostPage, { name: 'Starter', price: '6.00' })
  await addItem(hostPage, { name: 'Main', price: '14.00' })
  await addItem(hostPage, { name: 'Dessert', price: '5.00' })
  await openSplit(hostPage)

  const guestPage = await newPage(browser)
  await joinAsGuest(guestPage, host.sessionId, 'Guinan')
  await claimItem(guestPage, 'Main')
  await waitForClaimant(hostPage, 'Guinan')

  await finalize(hostPage)
  await waitForText(guestPage, 'Split locked')

  const hostTotals = await readLockedTotals(hostPage)
  const guestTotals = await readLockedTotals(guestPage)
  assert.ok(hostTotals.length > 0, 'the finalized screen lists per-person totals')
  assert.deepEqual(hostTotals, guestTotals, 'both phones show identical locked totals')
})

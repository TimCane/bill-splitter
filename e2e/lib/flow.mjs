import puppeteer from 'puppeteer'

// The app serves the SPA and the API from one origin (docs/13-deployment.md).
export const baseUrl = process.env.E2E_BASE_URL ?? 'http://localhost:8080'

/**
 * Seed a session straight through the backend. The frontend has no create/upload
 * surface - a receipt is turned into a session by the API, and the host arrives
 * at /s/:id with its token already in hand (docs/04-api-contract.md#post-apiv1sessions).
 */
export async function createSession(imageBuffer) {
  const form = new FormData()
  form.append('image', new Blob([imageBuffer], { type: 'image/jpeg' }), 'receipt.jpg')
  const response = await fetch(`${baseUrl}/api/v1/sessions`, { method: 'POST', body: form })
  if (response.status !== 202) {
    throw new Error(`create session failed: ${response.status} ${await response.text()}`)
  }
  return response.json() // { sessionId, participantId, participantToken, displayName }
}

export async function launchBrowser() {
  return puppeteer.launch({ args: ['--no-sandbox', '--disable-dev-shm-usage'] })
}

/** A fresh, isolated context so the host and a guest never share localStorage. */
export async function newPage(browser) {
  const context = await browser.createBrowserContext()
  return context.newPage()
}

/** Put the host identity in localStorage, then land on the session route. */
export async function enterAsHost(page, identity) {
  await page.goto(baseUrl)
  await page.evaluate((id, value) => {
    localStorage.setItem(`bs:${id}`, value)
  }, identity.sessionId, JSON.stringify({
    participantId: identity.participantId,
    participantToken: identity.participantToken,
    storedAt: Date.now(),
  }))
  await page.goto(`${baseUrl}/s/${identity.sessionId}`)
}

export function waitForText(page, text, timeout = 15000) {
  return page.waitForFunction(
    (needle) => document.body.innerText.includes(needle),
    { timeout },
    text,
  )
}

async function clickByText(page, text, rootSelector = null) {
  await page.waitForFunction(
    (label, root) => {
      const scope = root ? document.querySelector(root) : document
      if (!scope) return false
      return [...scope.querySelectorAll('button, [role="button"], a')]
        .some((el) => el.offsetParent !== null && el.textContent.trim() === label)
    },
    { timeout: 15000 },
    text,
    rootSelector,
  )
  await page.evaluate(
    (label, root) => {
      const scope = root ? document.querySelector(root) : document
      const matches = [...scope.querySelectorAll('button, [role="button"], a')]
        .filter((el) => el.offsetParent !== null && el.textContent.trim() === label)
      // The last match wins: a portalled dialog's confirm is appended after the
      // trigger that shares its label (e.g. "Open the split").
      matches[matches.length - 1].click()
    },
    text,
    rootSelector,
  )
}

/** Host, in Review: add one line item by hand. */
export async function addItem(page, { name, price }) {
  await clickByText(page, 'Add item')
  await page.waitForSelector('#item-name', { visible: true })
  await page.type('#item-name', name)
  await page.type('#item-price', price)
  await clickByText(page, 'Add item', '[role="dialog"]')
  await page.waitForFunction(() => !document.querySelector('#item-name'), { timeout: 10000 })
}

/** Host, in Review: open the split (button then confirm dialog). */
export async function openSplit(page) {
  await clickByText(page, 'Open the split')
  await clickByText(page, 'Open the split', '[role="dialog"]')
  await waitForText(page, 'Split ') // ClaimScreen header "Split {code}"
}

/** Guest: land on the open session, enter a name, join. */
export async function joinAsGuest(page, sessionId, displayName) {
  await page.goto(`${baseUrl}/s/${sessionId}`)
  await page.waitForSelector('#join-name', { visible: true })
  await page.type('#join-name', displayName)
  await clickByText(page, 'Join the split')
  await waitForText(page, 'Split ')
}

/** Claim the row whose item name matches, by clicking its "Mine" toggle. */
export async function claimItem(page, itemName) {
  await page.evaluate((needle) => {
    const row = [...document.querySelectorAll('li')].find((li) => li.textContent.includes(needle))
    if (!row) throw new Error(`no row for item ${needle}`)
    const toggle = [...row.querySelectorAll('button')].find((b) => b.textContent.trim() === 'Mine')
    if (!toggle) throw new Error(`no claim toggle for item ${needle}`)
    toggle.click()
  }, itemName)
}

/** Wait until a claimant's avatar (title = their name) is visible on this page. */
export function waitForClaimant(page, displayName, timeout = 5000) {
  return page.waitForFunction(
    (name) => !!document.querySelector(`[title="${name}"]`),
    { timeout },
    displayName,
  )
}

/** Host: finalize (footer button then the "Lock the split" confirm). */
export async function finalize(page) {
  await clickByText(page, 'Finalize')
  await clickByText(page, 'Lock the split', '[role="dialog"]')
  await waitForText(page, 'Split locked')
}

/** The per-person locked totals as `Name Amount` strings, sorted for comparison. */
export function readLockedTotals(page) {
  return page.evaluate(() =>
    [...document.querySelectorAll('button[aria-expanded]')]
      .map((b) => b.textContent.replace(/\s+/g, ' ').trim())
      .sort(),
  )
}

// Clicks through every screen of the real settings window, over the Chrome DevTools protocol, and fails on any error the
// page shows or logs. Run through smoke-window.ps1, which starts the window with its debugging port open.
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const web = fileURLToPath(new URL('../src/Thermi.Settings.Web/package.json', import.meta.url));
const { chromium } = createRequire(web)('@playwright/test');

const port = process.argv[2] ?? '9222';
const origin = 'https://thermi.invalid';
const settle = 400;
const errors = [];

const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
try {
  const page = browser
    .contexts()
    .flatMap((context) => context.pages())
    .find((one) => one.url().startsWith(origin));
  if (page === undefined) {
    throw new Error(`No page of ${origin} behind port ${port}.`);
  }
  page.on('pageerror', (error) => errors.push(`page error: ${error.stack ?? error.message}`));
  page.on('console', (message) => {
    if (message.type() === 'error') {
      errors.push(`console: ${message.text()}`);
    }
  });

  // Loaded again under watch, so an error as the page starts is caught too.
  await page.reload();
  const rows = page.getByRole('treeitem');
  await rows
    .first()
    .waitFor({ timeout: 15_000 })
    .catch(() => errors.push('the page showed no list'));
  if (errors.length > 0) {
    throw new Error(errors.join('\n\n'));
  }
  const count = await rows.count();
  // Each screen in turn, then back and forth quickly, as a person going down the list does.
  const order = [...Array(count).keys()];
  for (const at of order) {
    await rows.nth(at).click();
    await page.waitForTimeout(settle);
    await expectShown(page, `after row ${at + 1} of ${count}`);
  }
  for (const at of [...order, ...order.toReversed(), ...order]) {
    await rows.nth(at).click({ delay: 0 });
    await page.waitForTimeout(40);
  }
  await page.waitForTimeout(settle * 3);
  await expectShown(page, 'after clicking back and forth');
} finally {
  await browser.close().catch(() => {});
}

if (errors.length > 0) {
  console.error(errors.join('\n\n'));
  process.exit(1);
}
console.log('The window showed every screen without an error.');

// A page that failed to render leaves no tree behind.
async function expectShown(page, when) {
  if ((await page.getByRole('tree').count()) === 0) {
    errors.push(`the page went blank ${when}`);
    throw new Error(errors.join('\n\n'));
  }
}

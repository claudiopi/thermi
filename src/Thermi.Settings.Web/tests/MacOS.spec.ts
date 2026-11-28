import { expect, type FakeState, fakeOf, fakeState, openWindow, test, text, texts } from './fakeBridge';

// Spec 008, A14: the page as the Mac's host serves it, with the service stopped and thermi-cli on no PATH: it offers
// macOS's two fixes, starting the service again and putting thermi-cli on the PATH, no driver to install, and the start
// at login as Open at login, with every text in each translation.
test.fixme('TheWindowOffersTheMacsFixes', async ({ page }) => {
  // What the Mac's host says beside what Windows' says: the OS, and that no shell finds thermi-cli.
  const mac = { os: 'macos', cliOnPath: false };
  await openWindow(page, fakeState({ service: 'stopped', ...mac }));

  await expect(page.getByRole('button', { name: text('WindowText.InstallDriver') })).toHaveCount(0);
  await page.getByRole('button', { name: text('WindowText.AddCliToPath') }).click();
  await expect.poll(async () => ((await fakeOf(page)) as FakeState & typeof mac).cliOnPath).toBe(true);
  await page.getByRole('button', { name: text('WindowText.StartIt') }).click();
  await expect.poll(async () => (await fakeOf(page)).service).toBe('running');
  await page.keyboard.press('Meta+Comma');
  await expect(page.getByRole('switch', { name: text('UiText.OpenAtLogin') })).toBeVisible();
  await expect(page.getByRole('switch', { name: text('UiText.StartAtSignIn') })).toHaveCount(0);
  // The Mac's texts are in every translation, as the repo's rule holds every other.
  for (const language of ['en', 'es'] as const) {
    for (const key of ['WindowText.AddCliToPath', 'UiText.OpenAtLogin']) {
      expect(texts(language)[key]).toBeTruthy();
    }
  }
});

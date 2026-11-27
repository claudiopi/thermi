import { client, dispatched } from '../src/bridge/client.g';
import type { TextKey } from '../src/bridge/texts.g';
import { expect, fakeOf, fakeState, openWindow, test, text } from './fakeBridge';

// Spec 006, A16: the bridge's script over WebKitGTK's messages, as Linux's window hosts the page. The host takes each call
// through the script message handler it registered, and answers, as it sends every event, by running in the page the
// script C# writes for a message.
test('TheBridgeAnswersOverWebKit', async ({ page }) => {
  await page.goto('about:blank');
  await page.exposeFunction('host', ({ id, method }: { id: string; method: string }) => {
    void page.evaluate(dispatched(JSON.stringify({ id, result: method })));
  });
  await page.evaluate(() => {
    const host = window as unknown as { host: (message: unknown) => void; webkit: unknown };
    host.webkit = { messageHandlers: { thermi: { postMessage: (message: unknown) => host.host(message) } } };
  });
  await page.addScriptTag({ content: client });

  const answers = await page.evaluate(async () => {
    const bridge = (window as unknown as { thermi: Record<string, (...args: unknown[]) => unknown> }).thermi;
    const calls = Object.keys(bridge).filter((name) => !['changed', 'picked', 'readings'].includes(name));
    return Promise.all(calls.map(async (name) => [name, await bridge[name]?.()]));
  });
  const events = ['changed', 'picked', 'readings'];
  await page.evaluate((names) => {
    const bridge = (window as unknown as { thermi: Record<string, (listener: (value: unknown) => void) => void> }).thermi;
    const got: string[] = [];
    (window as unknown as { got: string[] }).got = got;
    for (const name of names) {
      bridge[name]?.((value) => got.push(String(value)));
    }
  }, events);
  for (const event of events) {
    await page.evaluate(dispatched(JSON.stringify({ event, value: event })));
  }

  expect(answers.every(([name, answer]) => name === answer)).toBe(true);
  expect(answers.length).toBeGreaterThan(0);
  expect(await page.evaluate(() => (window as unknown as { got: string[] }).got)).toEqual(events);
});

// Spec 006, A17: the page as Linux's host serves it, in each scheme the desktop may have, with a stopped service and a
// GNOME session whose thermi extension is missing, off or of another version, each with its own fix.
test.describe('TheWindowFollowsTheLinuxDesktop', () => {
  // WebKitGTK never forces the system's colors on a page: the host says the desktop's contrast through the bridge.
  const schemes: { name: string; colorScheme: 'light' | 'dark'; contrast: boolean; accent?: string; extension: string; fix: TextKey }[] = [
    { name: 'dark, with the desktop\'s accent', colorScheme: 'dark', contrast: false, accent: '#3584e4', extension: 'missing', fix: 'WindowText.InstallExtension' },
    { name: 'light', colorScheme: 'light', contrast: false, extension: 'disabled', fix: 'WindowText.EnableExtension' },
    { name: 'high contrast', colorScheme: 'dark', contrast: true, extension: 'other-version', fix: 'HostText.GnomeExtensionUpdate' },
  ];
  for (const scheme of schemes) {
    test(scheme.name, async ({ page }) => {
      // What Linux's host says of the desktop, beside what Windows' says: the OS, its accent and contrast, and where the
      // GNOME extension is.
      const desktop = { os: 'linux', accent: scheme.accent ?? null, contrast: scheme.contrast, extension: scheme.extension };
      await page.emulateMedia({ colorScheme: scheme.colorScheme, forcedColors: 'none' });
      await openWindow(page, fakeState({ service: 'stopped', ...desktop }));

      const main = page.getByRole('main');
      expect(await main.evaluate((shown) => getComputedStyle(shown).colorScheme)).toContain(scheme.colorScheme);
      if (scheme.accent !== undefined) {
        const accent = await main.evaluate((shown) => getComputedStyle(shown).getPropertyValue('--colorBrandBackground'));
        expect(accent.trim().toLowerCase()).toBe(scheme.accent);
      }
      // In high contrast the page paints in the system's own colors, as a probe of Canvas and CanvasText in the pane's own
      // scheme reads them: its text, and the first background under it that hides what is behind, as the pane's is a tint.
      const system = await main.evaluate((shown) => {
        const probe = shown.appendChild(Object.assign(document.createElement('div'), { style: 'background: Canvas; color: CanvasText' }));
        let painted: Element = shown;
        while (getComputedStyle(painted).backgroundColor.startsWith('rgba(') && painted.parentElement !== null) {
          painted = painted.parentElement;
        }
        const colors = getComputedStyle(probe);
        const same = colors.backgroundColor === getComputedStyle(painted).backgroundColor && colors.color === getComputedStyle(shown).color;
        probe.remove();
        return same;
      });
      expect(system).toBe(scheme.contrast);
      // The desktop draws no caption around the page, which draws Windows' own buttons in its title row.
      await page.getByRole('button', { name: text('WindowText.Maximize') }).click();
      await expect(page.getByRole('button', { name: text('WindowText.RestoreDown') })).toBeVisible();
      await expect(page.getByRole('button', { name: text('WindowText.Minimize') })).toBeVisible();
      // A press on the title row let go below it moves nothing, nor does a drag that comes into the row from below; one held
      // in the row moves the window once.
      const row = (await page.locator('header').first().boundingBox())!;
      const [x, y] = [row.x + row.width / 2, row.y + row.height - 2];
      for (const [from, to] of [[y, y + 6], [y + 60, y - 10], [y - 10, y - 30]] as const) {
        await page.mouse.move(x, from);
        await page.mouse.down();
        await page.mouse.move(x, to, { steps: 4 });
        await page.mouse.up();
      }
      expect((await fakeOf(page)).moves).toBe(1);
      await page.keyboard.press('Control+Comma');
      await expect(page.getByRole('switch', { name: text('UiText.StartAtSignIn') })).toBeVisible();
      await expect(page.getByRole('button', { name: text('WindowText.InstallDriver') })).toHaveCount(0);
      await page.getByRole('button', { name: text(scheme.fix) }).click();
      await page.getByRole('button', { name: text('WindowText.StartIt') }).click();
      await expect.poll(async () => (await fakeOf(page)).service).toBe('running');
    });
  }
});

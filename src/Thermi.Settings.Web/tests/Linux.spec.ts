import { client } from '../src/bridge/client.g';
import { expect, fakeOf, fakeState, openWindow, test, text } from './fakeBridge';

// Spec 006, A16: the bridge's script over WebKitGTK's messages, as Linux's window hosts the page. The host takes each call
// through the script message handler it registered, and answers, as it sends every event, by running script in the page.
test('TheBridgeAnswersOverWebKit', async ({ page }) => {
  await page.goto('about:blank');
  await page.evaluate(() => {
    type Message = { id: string; method: string };
    const host = window as unknown as {
      webkit: { messageHandlers: { thermi: { postMessage: (message: Message) => void } } };
      answered: Record<string, unknown>;
    };
    host.answered = {};
    host.webkit = {
      messageHandlers: {
        thermi: {
          postMessage: ({ id, method }) =>
            setTimeout(() => window.dispatchEvent(new MessageEvent('thermi', { data: { id, result: method } }))),
        },
      },
    };
  });
  await page.addScriptTag({ content: client });

  const heard = await page.evaluate(async () => {
    const bridge = (window as unknown as { thermi: Record<string, (...args: unknown[]) => unknown> }).thermi;
    const events = ['changed', 'picked', 'readings'];
    const calls = Object.keys(bridge).filter((name) => !events.includes(name));
    const answers = await Promise.all(calls.map(async (name) => [name, await bridge[name]?.()]));
    const got: string[] = [];
    for (const event of events) {
      bridge[event]?.((value: unknown) => got.push(String(value)));
      window.dispatchEvent(new MessageEvent('thermi', { data: { event, value: event } }));
    }
    return { answers, got, events };
  });

  expect(heard.answers.every(([name, answer]) => name === answer)).toBe(true);
  expect(heard.answers.length).toBeGreaterThan(0);
  expect(heard.got).toEqual(heard.events);
});

// Spec 006, A17: the page as Linux's host serves it, in each scheme the desktop may have, with a stopped service and a
// GNOME session that lacks thermi's extension.
test.describe('TheWindowFollowsTheLinuxDesktop', () => {
  const schemes: { name: string; colorScheme: 'light' | 'dark'; forcedColors: 'active' | 'none'; accent?: string }[] = [
    { name: 'dark, with the desktop\'s accent', colorScheme: 'dark', forcedColors: 'none', accent: '#3584e4' },
    { name: 'light', colorScheme: 'light', forcedColors: 'none' },
    { name: 'high contrast', colorScheme: 'dark', forcedColors: 'active' },
  ];
  for (const scheme of schemes) {
    test(scheme.name, async ({ page }) => {
      // What Linux's host says of the desktop, beside what Windows' says: the OS, its accent, and the GNOME extension missing.
      const desktop = { os: 'linux', accent: scheme.accent ?? null, extension: 'missing' };
      await page.emulateMedia({ colorScheme: scheme.colorScheme, forcedColors: scheme.forcedColors });
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
      expect(system).toBe(scheme.forcedColors === 'active');
      await page.keyboard.press('Control+Comma');
      await expect(page.getByRole('switch', { name: text('UiText.StartAtSignIn') })).toBeVisible();
      await expect(page.getByRole('button', { name: text('WindowText.InstallDriver') })).toHaveCount(0);
      await page.getByRole('button', { name: text('WindowText.InstallExtension') }).click();
      await page.getByRole('button', { name: text('WindowText.StartIt') }).click();
      await expect.poll(async () => (await fakeOf(page)).service).toBe('running');
    });
  }
});

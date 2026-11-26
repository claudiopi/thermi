import { type Page } from '@playwright/test';
import {
  axeViolations,
  expect,
  fakeOf,
  fakeState,
  focusFault,
  layoutFaults,
  list,
  openWindow,
  pseudo,
  recording,
  selectedRow,
  setFake,
  settled,
  test,
  text,
  texts,
  windowWith,
  type FakeState,
} from './fakeBridge';
import { focusContract, type Act } from './focusContract';

/** A text by its key, in the language the window shows. */
type Say = (key: string) => string;

/** A view of the window: a page, a dialog, a menu, a bar or a toast, opened from the window's start. */
interface View {
  name: string;
  state?: Partial<FakeState>;
  /** Opens it, finding controls by the texts of the language shown. */
  open: (page: Page, say: Say) => Promise<void>;
  /** The task that builds the view; until it is done, the view is skipped. */
  waits?: string;
}

const pages: View[] = [
  // The first overlay's row, named in the window's language, the long one too.
  { name: 'an overlay', open: (page) => list(page).getByRole('treeitem', { level: 1 }).first().click() },
  { name: 'an item', open: () => Promise.resolve() },
  { name: "an item's more options", open: (page, say) => page.getByRole('button', { name: say('UiText.MoreOptions') }).click() },
  { name: 'Settings', open: (page) => page.keyboard.press('Control+Comma') },
];

const dialogsAndMenus: View[] = [
  { name: 'the catalog', open: (page) => page.keyboard.press('Control+N') },
  // A search marks what matched in each row, and the metric a sensor feeds where its name does not say it.
  { name: 'the catalog searching', open: async (page) => {
    await page.keyboard.press('Control+N');
    await page.getByRole('searchbox').fill('cpu temp');
  } },
  { name: 'the Sensor dropdown filtering', open: async (page, say) => {
    await page.getByRole('main').getByRole('combobox', { name: say('UiText.SensorField') }).press('Alt+ArrowDown');
    await page.keyboard.type('ccd');
  } },
  // Over the Placement column, whose map draws its spots above the page.
  { name: 'the catalog over an overlay', open: async (page) => {
    await list(page).getByRole('treeitem', { level: 1 }).first().click();
    await page.keyboard.press('Control+N');
  } },
  // Opened narrow, it stays open as the window widens and narrows again.
  { name: 'the narrow list', open: async (page, say) => {
    await page.setViewportSize({ width: 760, height: 560 });
    await page.getByRole('button', { name: say('UiText.ShowList') }).click();
  } },
  { name: 'the shortcuts', open: (page) => page.keyboard.press('F1') },
  { name: 'Reset', open: async (page, say) => {
    await page.keyboard.press('Control+Comma');
    await page.getByRole('button', { name: say('WindowText.FileAndReset') }).click();
    await page.getByRole('button', { name: say('UiText.ResetToDefaults') }).press('Enter');
  } },
  { name: 'a row menu', open: async (page) => {
    await selectedRow(page).focus();
    await page.keyboard.press('Shift+F10');
  } },
  { name: 'the title row menu', open: (page, say) => page.getByRole('button', { name: say('UiText.MoreActions') }).press('Enter') },
];

const barsAndToasts: View[] = [
  { name: 'no overlay', state: { overlayRunning: false }, open: () => Promise.resolve() },
  { name: 'the service stopped', state: { service: 'stopped' }, open: () => Promise.resolve() },
  { name: 'stale data', state: { service: 'stale' }, open: () => Promise.resolve() },
  { name: 'a broken file', state: { problem: 'broken' }, open: () => Promise.resolve() },
  { name: 'the driver', state: { sensors: recording('no-admin') }, open: () => Promise.resolve() },
  {
    name: "the driver's dialog",
    state: { sensors: recording('no-admin') },
    open: (page, say) => page.getByRole('button', { name: say('WindowText.WhatIsThis') }).click(),
  },
  { name: 'a toast', open: async (page) => {
    await selectedRow(page).focus();
    await page.keyboard.press('Delete');
  } },
];

async function openOn(page: Page, view: View, say: Say = text): Promise<void> {
  // A broken file shows only its page, with no list to select in.
  if (view.state?.problem === undefined) {
    // The first item, the CPU temperature, named in the window's language, the long one too.
    await list(page).getByRole('treeitem', { level: 3 }).first().click();
  } else {
    await page.getByRole('heading', { level: 2 }).waitFor();
  }
  await view.open(page, say);
}

// A212: every page, dialog and menu at every size, at 100 % and 200 % text, in English and the long pseudo-language.
test('EveryPageFitsAtEverySize', async ({ context }) => {
  // Sixteen views, in two text sizes and two languages, at seven sizes each.
  test.setTimeout(360_000);
  const sizes: { width: number; height: number; folds: boolean }[] = [
    { width: 2560, height: 1440, folds: false },
    { width: 1920, height: 1080, folds: false },
    { width: 1500, height: 900, folds: false },
    { width: 1200, height: 800, folds: false },
    { width: 860, height: 640, folds: false },
    { width: 859, height: 640, folds: true },
    { width: 760, height: 560, folds: true },
  ];
  for (const scale of [1, 2]) {
    for (const language of ['English', 'pseudo'] as const) {
      // Where the list folds depends on the size, the text's and the language alone, so it is checked once for them: each
      // view checking it again doubled a test whose length grows with how busy the PC is.
      let foldsChecked = false;
      for (const view of [...pages, ...dialogsAndMenus, ...barsAndToasts]) {
        await test.step(`${view.name}, text at ${scale * 100} %, ${language}`, async () => {
          const shown = language === 'pseudo' ? pseudo(texts()) : texts();
          const say: Say = (key) => shown[key] ?? key;
          const page = await windowWith(context, fakeState({ texts: shown, ...view.state }));
          // Windows' text size reaches the page as its root font size.
          await page.evaluate((factor) => {
            document.documentElement.style.fontSize = `${factor * 100}%`;
          }, scale);
          // A broken file shows no list, so nothing folds.
          for (const size of view.state?.problem === undefined && !foldsChecked ? sizes : []) {
            await page.setViewportSize(size);
            await expect(page.getByRole('button', { name: say('UiText.ShowList') })).toBeVisible({ visible: size.folds });
          }
          foldsChecked ||= view.state?.problem === undefined;
          await page.setViewportSize({ width: 1920, height: 1080 });
          await openOn(page, view, say);
          for (const size of sizes) {
            await page.setViewportSize(size);
            expect(await layoutFaults(page), `${size.width} × ${size.height}`).toEqual([]);
          }
          await page.close();
        });
      }
    }
  }
});

async function perform(page: Page, act: Act): Promise<void> {
  if (typeof act === 'string') {
    await page.keyboard.press(act);
  } else if ('key' in act) {
    // A key Playwright's keyboard does not have, as Windows sends it to the focused control.
    await page.evaluate(
      (code) => document.activeElement?.dispatchEvent(new KeyboardEvent('keydown', { key: code, code, bubbles: true, cancelable: true })),
      act.key,
    );
  } else if ('type' in act) {
    await page.keyboard.type(typeof act.type === 'string' ? act.type : act.type());
  } else if ('focus' in act) {
    await act.focus(page).focus();
  } else if ('click' in act) {
    const target = act.click(page);
    const box = act.low === true ? await target.boundingBox() : null;
    await target.click({
      ...(box === null ? {} : { position: { x: Math.min(100, box.width / 2), y: box.height - 14 } }),
      ...(act.right === true ? { button: 'right' as const } : {}),
    });
  } else if ('fill' in act) {
    await act.fill(page).fill(act.value);
  } else if ('size' in act) {
    await page.setViewportSize(act.size);
  } else if ('at' in act) {
    await page.mouse.click(act.at.x, act.at.y);
  } else if ('sliding' in act) {
    // Not a wait of some milliseconds, which a slow run would overshoot: the moment an animation runs.
    await page.waitForFunction(() => document.getAnimations().some((animation) => animation.playState === 'running'));
  } else if ('pick' in act) {
    await page.evaluate((pick) => window.thermiFake?.pick(pick), act.pick);
  } else {
    await setFake(page, act.fake);
  }
}

// A213: the design prototype's focus contract, its steps in the page as one table: after each, the focus is on a control
// in view, never the page, a dialog keeps it inside, and a menu or dialog gives it back to what opened it. Its steps on
// Windows' own frame, the caption's maximize and the system menu, no page test can press, so they are A220's by hand.
// A scene is a case; a step that waits for its task is a fixme of its own, so the report shows it.
test.describe('TheFocusContractHolds', () => {
  for (const run of focusContract) {
    for (const step of run.steps.filter((one) => one.waits !== undefined)) {
      test.fixme(`${run.name}: ${step.name}, until ${step.waits}`, () => {});
    }
  }
  for (const run of focusContract) test(run.name, async ({ page }) => {
    await openWindow(page, run.start());
    await page.setViewportSize(run.size);
    await page.emulateMedia({ colorScheme: run.colorScheme, reducedMotion: run.motion === true ? 'no-preference' : 'reduce' });
    // The first key comes once the page has drawn itself at its size, as a person sees the window before typing.
    await page.evaluate(settled);
    for (const step of run.steps.filter((one) => one.waits === undefined)) {
      await test.step(step.name, async () => {
        for (const act of step.acts) {
          await perform(page, act);
        }
        await expect.poll(() => focusFault(page)).toBeNull();
        if (step.focus !== undefined) {
          await expect(step.focus(page)).toBeFocused();
        }
        if (step.away !== undefined) {
          // No element it finds has the focus, which also holds when it finds none, as `:focus` inside a pane.
          await expect(step.away(page).and(page.locator(':focus'))).toHaveCount(0);
        }
        const holds = step.holds;
        if (holds !== undefined) {
          await expect.poll(async () => holds(await fakeOf(page))).toBe(true);
        }
      });
    }
  });
});

// A214: axe finds nothing in any page, dialog, menu, bar or toast, light, dark and under forced colors, also on the
// opaque background Windows 10 gets for want of Mica (R177), and with reduced motion nothing animates. A view is a
// case, and one whose task is not done is a fixme, so the report shows it.
test.describe('AxeFindsNothingInAnyState', () => {
  const modes: { name: string; colorScheme: 'light' | 'dark'; forcedColors: 'active' | 'none'; solid?: boolean }[] = [
    { name: 'light', colorScheme: 'light', forcedColors: 'none' },
    { name: 'dark', colorScheme: 'dark', forcedColors: 'none' },
    { name: 'forced colors', colorScheme: 'dark', forcedColors: 'active' },
    { name: 'light without Mica', colorScheme: 'light', forcedColors: 'none', solid: true },
    { name: 'dark without Mica', colorScheme: 'dark', forcedColors: 'none', solid: true },
  ];
  // Rows: axe judges each page, dialog, menu, bar and toast on its own, as A214 asks of every view.
  for (const view of [...pages, ...dialogsAndMenus, ...barsAndToasts]) {
    if (view.waits !== undefined) {
      test.fixme(`${view.name}, until ${view.waits}`, () => {});
      continue;
    }
    test(view.name, async ({ page }) => {
      await openWindow(page, fakeState(view.state));
      await page.emulateMedia({ reducedMotion: 'reduce' });
      await openOn(page, view);
      for (const mode of modes) {
        await page.emulateMedia({ colorScheme: mode.colorScheme, forcedColors: mode.forcedColors });
        // As the host marks the page where Windows refuses Mica. The page follows the mode on its next render, so axe
        // waits for it: on a solid background, a frame in between would read as text of the other mode.
        await page.evaluate((solid) => {
          if (solid) document.documentElement.setAttribute('data-backdrop', 'solid');
        }, mode.solid === true);
        await page.evaluate(settled);
        expect(await axeViolations(page), mode.name).toEqual([]);
        // The scrollbars Edge draws follow the mode, never white on a dark page; forced colors offer both.
        // Found as an element: an open dialog hides the pane from the roles.
        expect(await page.locator('main').evaluate((main) => getComputedStyle(main).colorScheme), mode.name).toContain(mode.colorScheme);
        expect(await page.evaluate(() => document.getAnimations().filter((animation) => animation.playState === 'running').length), mode.name).toBe(0);
      }
    });
  }
});

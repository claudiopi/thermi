import type { Locator, Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import type { Sensor } from './bridgeTypes';

// What the page's tests read from the repo and check on the page: the recordings, the texts by key, the rows by role,
// and what a person would see as broken.

const repo = new URL('../../../', import.meta.url);

function repoFile(path: string): string {
  return readFileSync(new URL(path, repo), 'utf8');
}

/** A recording's sensors, as the service read them on the author's PC. */
export function recording(name: 'admin' | 'no-admin' | 'admin-net-disk'): Sensor[] {
  const file = repoFile(`tests/Thermi.Tests/Recordings/ryzen-9950x-rtx-5060-ti-${name}.json`);
  return (JSON.parse(file) as { sensors: Sensor[] }).sensors;
}

/**
 * The recorded PC with its adapters and drive, plus a second drive made up from its first, and, for a laptop, a battery
 * as LibreHardwareMonitor reports one: a charge, a rate while it discharges and a time that Windows estimates.
 */
export function withDrivesAndBattery(laptop: boolean): Sensor[] {
  const sensors = recording('admin-net-disk');
  const drive = sensors.filter((sensor) => sensor.hardwareType === 'Storage');
  const second = drive.map((sensor) => ({
    ...sensor,
    id: sensor.id.replace(/^\/(\w+)\/\d+/, '/$1/9'),
    hardware: 'Samsung SSD 870 EVO 1TB',
    value: sensor.value === null ? null : sensor.value / 3,
  }));
  const battery: Sensor[] = [
    { id: '/battery/bat0/level/0', hardwareType: 'Battery', hardware: 'LGC 5B10W51867', type: 'Level', name: 'Charge Level', value: 64 },
    { id: '/battery/bat0/power/0', hardwareType: 'Battery', hardware: 'LGC 5B10W51867', type: 'Power', name: 'Discharge Rate', value: -12.4 },
    { id: '/battery/bat0/timespan/0', hardwareType: 'Battery', hardware: 'LGC 5B10W51867', type: 'TimeSpan', name: 'Remaining Time (Estimated)', value: 8040 },
  ];
  return [...sensors, ...second, ...(laptop ? battery : [])];
}

/** The recorded PC with a game in front, as the service and the overlay read it: its frame rate and its longest frame. */
export function withGame(): Sensor[] {
  return [
    ...recording('admin'),
    { id: '/game/0/framerate/0', hardwareType: 'Game', hardware: 'Game', type: 'FrameRate', name: 'FPS', value: 72 },
    { id: '/game/0/frametime/0', hardwareType: 'Game', hardware: 'Game', type: 'FrameTime', name: 'Frame time', value: 18.4 },
  ];
}

const translations = new Map<string, Record<string, string>>();

/** The texts of a language, keyed `Enum.Member`, as the bridge gives them to the page. */
export function texts(language: 'en' | 'es' = 'en'): Record<string, string> {
  const known = translations.get(language);
  if (known !== undefined) {
    return known;
  }
  const xml = repoFile(`src/Thermi.Core/Translations/Texts${language === 'en' ? '' : `.${language}`}.resx`);
  const read: Record<string, string> = {};
  for (const match of xml.matchAll(/<data name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>/g)) {
    read[match[1] ?? ''] = (match[2] ?? '')
      .replaceAll('&lt;', '<')
      .replaceAll('&gt;', '>')
      .replaceAll('&quot;', '"')
      .replaceAll('&apos;', "'")
      .replaceAll('&amp;', '&');
  }
  translations.set(language, read);
  return read;
}

/** An English text by its key, with its blanks filled, to find an element by its accessible name. */
export function text(key: string, ...blanks: (string | number)[]): string {
  const value = texts()[key];
  if (value === undefined) {
    throw new Error(`Texts.resx has no ${key}.`);
  }
  return value.replace(/\{(\d+)\}/g, (blank: string, index: string) => String(blanks[Number(index)] ?? blank));
}

/** A name that starts with the given one, for rows whose name goes on with their place or count. */
export function startingWith(name: string): RegExp {
  return new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`);
}

/** The pseudo-language: every text 40 % longer and in brackets, so a cut one shows. */
export function pseudo(english: Record<string, string>): Record<string, string> {
  return Object.fromEntries(Object.entries(english).map(([key, value]) => [key, `[${value}${'·'.repeat(Math.ceil(value.length * 0.4))}]`]));
}

/** The list of overlays, pills and items. */
export function list(page: Page): Locator {
  return page.getByRole('tree');
}

/** The row the list has selected. */
export function selectedRow(page: Page): Locator {
  return list(page).getByRole('treeitem', { selected: true });
}

/** Selects a row of the list by its name, as a click would. */
export async function select(page: Page, name: string): Promise<void> {
  await list(page).getByRole('treeitem', { name: startingWith(name) }).first().click();
}

/** A toast: passing news that moves nothing. */
export function toast(page: Page): Locator {
  return page.getByRole('status').filter({ visible: true });
}

/**
 * Runs in the page: done once a frame, and then the work the page queued before it, changed nothing, which is what a
 * person sees. Two frames are not enough: a busy browser draws its next frame before the page's queued work, as React's
 * render is, so a check two frames on can meet the page halfway through. A message posted after a frame comes after it.
 */
export async function settled(): Promise<void> {
  await new Promise<void>((done) => {
    let changed = true;
    const changes = new MutationObserver(() => {
      changed = true;
    });
    changes.observe(document.body, { subtree: true, childList: true, attributes: true, characterData: true });
    const next = (): void => {
      requestAnimationFrame(() => {
        const after = new MessageChannel();
        after.port1.onmessage = () => {
          after.port1.close();
          if (changed) {
            changed = false;
            next();
          } else {
            changes.disconnect();
            done();
          }
        };
        after.port2.postMessage(null);
      });
    };
    next();
  });
}

/** What a person would see as broken: the page scrolling sideways, a text cut, or two controls overlapping. */
export async function layoutFaults(page: Page): Promise<string[]> {
  // What the window shows once C# answered what the view asked, as a delete's: until then it still shows the page before.
  await page.waitForFunction(() => window.thermiFake?.caughtUp() !== false, undefined, { timeout: 5000 });
  // After a resize, once the page measured its new size and drew again.
  await page.evaluate(settled);
  return page.evaluate(async () => {
    // And once what was opening has finished sliding in.
    const ending = document.getAnimations().filter((animation) => animation.effect?.getComputedTiming().endTime !== Infinity);
    await Promise.all(ending.map((animation) => animation.finished.catch(() => undefined)));
    const faults: string[] = [];
    const named = (element: Element): string => element.getAttribute('aria-label') ?? (element.textContent ?? '').trim().slice(0, 40);
    const root = document.scrollingElement;
    if (root !== null && root.scrollWidth > root.clientWidth) {
      faults.push('the page scrolls sideways');
    }
    // A control that shows only under the pointer or the focus, like a look's "…" in its tile's corner, is not seen, nor
    // a text clipped away for Narrator alone.
    const shown = [...document.querySelectorAll<HTMLElement>('body *')].filter(
      (element) =>
        element.checkVisibility({ opacityProperty: true, visibilityProperty: true }) &&
        element.getClientRects().length > 0 &&
        getComputedStyle(element).clipPath !== 'inset(50%)',
    );
    for (const element of shown) {
      const overflow = getComputedStyle(element).overflowX;
      const scrolls = overflow === 'auto' || overflow === 'scroll';
      if (!scrolls && element.childElementCount === 0 && (element.textContent ?? '').trim() !== '' && element.scrollWidth > element.clientWidth + 1) {
        faults.push(`cut: ${named(element)}`);
      }
      // A box's text is its value, as a dropdown shows its choice.
      if (element instanceof HTMLInputElement && element.value !== '' && element.scrollWidth > element.clientWidth + 1) {
        faults.push(`cut: ${element.value}`);
      }
    }
    const controls = shown.filter((element) =>
      element.matches(
        'button, input, select, textarea, a[href], [role="button"], [role="switch"], [role="slider"], ' +
          '[role="combobox"], [role="radio"], [role="menuitem"], [role="treeitem"]',
      ),
    );
    // What shows of a control: its box, less what the boxes that scroll or clip around it hide.
    const seen = (element: HTMLElement): { left: number; top: number; right: number; bottom: number } => {
      const box = element.getBoundingClientRect();
      let seenBox = { left: box.left, top: box.top, right: box.right, bottom: box.bottom };
      for (let around = element.parentElement; around !== null; around = around.parentElement) {
        const style = getComputedStyle(around);
        if (style.overflowX !== 'visible' || style.overflowY !== 'visible') {
          const clip = around.getBoundingClientRect();
          seenBox = {
            left: Math.max(seenBox.left, clip.left),
            top: Math.max(seenBox.top, clip.top),
            right: Math.min(seenBox.right, clip.right),
            bottom: Math.min(seenBox.bottom, clip.bottom),
          };
        }
      }
      return seenBox;
    };
    // A control or a text that a box clips sideways is cut, as chips scrolled out of a row that shows no scrollbar.
    const texts = shown.filter((element) => element.childElementCount === 0 && (element.textContent ?? '').trim() !== '');
    for (const control of new Set([...controls, ...texts])) {
      const box = control.getBoundingClientRect();
      const part = seen(control);
      if (part.right - part.left < box.width - 1 && part.bottom > part.top) {
        faults.push(`cut sideways: ${named(control)}`);
      }
    }
    // The narrow list opens over the pane, as a layer of its own.
    const pane = document.querySelector('main')?.getBoundingClientRect();
    const overPane = [...document.querySelectorAll<HTMLElement>('nav')].filter((nav) => {
      const box = nav.getBoundingClientRect();
      return pane !== undefined && nav.checkVisibility() && box.right > pane.left + 1 && box.left < pane.right - 1 && box.bottom > pane.top + 1;
    });
    // A dialog, a panel, a menu or the open narrow list covers the page: nothing of the page shows above it or through it.
    const covers = [...document.querySelectorAll<HTMLElement>('[role="dialog"], [role="alertdialog"], [role="menu"]')]
      .filter((layer) => layer.checkVisibility())
      .concat(overPane);
    const alphaOf = (color: string): number => {
      const match = /^rgba?\(.*[,/]\s*([\d.]+)\)$/.exec(color);
      return match === null || !color.includes('a(') ? 1 : Number(match[1]);
    };
    // Where an element stacks in the window: the z-index of its outermost ancestor that sets one, as Edge paints it, where
    // a hit test can still find the layer above.
    const stackOf = (element: Element): number => {
      let z = 0;
      for (let at: Element | null = element; at !== null; at = at.parentElement) {
        const index = getComputedStyle(at).zIndex;
        z = index === 'auto' ? z : Number(index);
      }
      return z;
    };
    for (const layer of covers) {
      let back = layer;
      while (getComputedStyle(back).backgroundColor === 'rgba(0, 0, 0, 0)' && back.parentElement !== null) {
        back = back.parentElement;
      }
      if (alphaOf(getComputedStyle(back).backgroundColor) < 1 || getComputedStyle(layer).backdropFilter !== 'none') {
        faults.push(`see-through: ${named(layer)}`);
      }
      const over = layer.getBoundingClientRect();
      for (const control of controls.filter((one) => !covers.some((other) => other.contains(one)))) {
        const part = seen(control);
        const [left, right, top, bottom] = [
          Math.max(part.left, over.left),
          Math.min(part.right, over.right),
          Math.max(part.top, over.top),
          Math.min(part.bottom, over.bottom),
        ];
        const hit = right - left > 2 && bottom - top > 2 ? document.elementFromPoint((left + right) / 2, (top + bottom) / 2) : null;
        if (hit !== null && (control.contains(hit) || stackOf(control) > stackOf(layer))) {
          faults.push(`shows through: ${named(control)}`);
        }
      }
    }
    // A button says something of its own, never its row's name again. Its words only: an icon is a glyph of Windows' font.
    const wordsOf = (element: Element): string => (element.textContent ?? '').replace(/\p{Co}/gu, '').trim();
    for (const button of controls.filter((one) => one.matches('button'))) {
      const own = wordsOf(button);
      let row = button.parentElement;
      for (let up = 0; up < 3 && row !== null && own !== ''; up += 1, row = row.parentElement) {
        const around = row;
        if (texts.some((one) => !button.contains(one) && around.contains(one) && wordsOf(one) === own)) {
          faults.push(`said twice: ${own}`);
          break;
        }
      }
    }
    // A choice drawn without words, as a spot on the map, shows its name in words once chosen.
    const words = new Set(texts.map((element) => (element.textContent ?? '').trim()));
    for (const chosen of controls.filter((one) => one.matches('[role="radio"][aria-checked="true"]') && (one.textContent ?? '').trim() === '')) {
      if (!words.has(chosen.getAttribute('aria-label') ?? '')) {
        faults.push(`unnamed on screen: ${named(chosen)}`);
      }
    }
    // A wide pane leaves no empty strip at its end beyond what it leaves at its start, and the item's chart beside its
    // rows stays in view as they scroll.
    const main = document.querySelector('main');
    if (main !== null && main.clientWidth >= 864) {
      const inMain = controls.filter((control) => main.contains(control)).map(seen);
      const pane = main.getBoundingClientRect();
      const [start, reach] = [Math.min(...inMain.map((box) => box.left)), Math.max(...inMain.map((box) => box.right))];
      if (pane.right - reach - (start - pane.left) > 120) {
        faults.push('a strip of the pane stays empty');
      }
      const plot = main.querySelector<SVGElement>('svg[role="img"]');
      const before = main.scrollTop;
      main.scrollTop = main.scrollHeight;
      await new Promise((drawn) => requestAnimationFrame(drawn));
      if (plot !== null && plot.getBoundingClientRect().top < main.getBoundingClientRect().top - 1) {
        faults.push('the chart scrolls away');
      }
      main.scrollTop = before;
    }
    // Passing news floats over the pane, but never over the control that has the focus, the one just edited.
    const toasts = [...document.querySelectorAll<HTMLElement>('[role="status"] > *')].filter((one) => one.closest('main') === null && one.checkVisibility());
    for (const toast of toasts) {
      const over = toast.getBoundingClientRect();
      const focused = controls.filter((one) => !toast.contains(one) && one.contains(document.activeElement));
      for (const control of focused) {
        const part = seen(control);
        if (Math.min(part.right, over.right) - Math.max(part.left, over.left) > 1 && Math.min(part.bottom, over.bottom) - Math.max(part.top, over.top) > 1) {
          faults.push(`under the toast: ${named(control)}`);
        }
      }
    }
    // A dialog, a panel, a menu or a list that opens covers what is under it on purpose: only controls of one layer clash.
    const layerOf = (element: HTMLElement): Element | null =>
      element.closest('[role="dialog"], [role="alertdialog"], [role="menu"], [role="listbox"], [role="status"]') ??
      overPane.find((nav) => nav.contains(element)) ??
      null;
    controls.forEach((one, index) => {
      const a = seen(one);
      for (const other of controls.slice(index + 1)) {
        if (one.contains(other) || other.contains(one) || layerOf(one) !== layerOf(other)) {
          continue;
        }
        const b = seen(other);
        if (Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1) {
          faults.push(`overlap: ${named(one)} and ${named(other)}`);
        }
      }
    });
    return faults;
  });
}

/** Why the focus breaks R123 and R124 now, or null: it is on the page, out of view, or outside an open dialog. */
export async function focusFault(page: Page): Promise<string | null> {
  return page.evaluate(() => {
    const active = document.activeElement;
    if (active === null || active === document.body || active === document.documentElement) {
      return 'the focus is on the page';
    }
    const box = active.getBoundingClientRect();
    if (box.bottom <= 0 || box.right <= 0 || box.top >= innerHeight || box.left >= innerWidth) {
      return 'the focus is out of view';
    }
    const dialogs = [...document.querySelectorAll('[role="dialog"], [role="alertdialog"], dialog[open]')].filter((dialog) =>
      (dialog as HTMLElement).checkVisibility(),
    );
    const top = dialogs.at(-1);
    if (top !== undefined && !top.contains(active)) {
      return 'the focus left an open dialog';
    }
    return null;
  });
}

/** What axe finds on the page as it is now, one line per element. */
export async function axeViolations(page: Page): Promise<string[]> {
  if (!(await page.evaluate(() => window.axe !== undefined))) {
    await page.evaluate(readFileSync(new URL('../node_modules/axe-core/axe.min.js', import.meta.url), 'utf8'));
  }
  return page.evaluate(async () => {
    if (window.axe === undefined) {
      throw new Error('axe did not load.');
    }
    // Fluent's focus manager keeps hidden sentinels that hand the focus on the moment they get it; axe cannot know.
    const results = await window.axe.run({ exclude: [['[data-tabster-dummy]']] });
    return results.violations.flatMap((violation) => violation.nodes.map((node) => `${violation.id}: ${node.target.join(' ')}`));
  });
}

/** Opens a combobox by keys and picks the option whose name is typed. */
export async function chooseByKeys(page: Page, combobox: Locator, option: string): Promise<void> {
  await combobox.focus();
  await combobox.press('Alt+ArrowDown');
  await page.keyboard.type(option);
  await page.keyboard.press('Enter');
}

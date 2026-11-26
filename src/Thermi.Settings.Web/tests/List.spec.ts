import { type Page } from '@playwright/test';
import {
  chooseByKeys,
  expect,
  fakeState,
  itemOf,
  list,
  openWindow,
  overlayOf,
  recording,
  select,
  selectedRow,
  setFake,
  settingsOf,
  startingWith,
  test,
  text,
  toast,
  withDrivesAndBattery,
  type Overlay,
  type Settings,
} from './fakeBridge';

const ids = (settings: Settings, overlay: number): string[] => overlayOf(settings, overlay).items.map((item) => item.id);

// The browser draws the same row a level or two apart from one frame to the next on a busy run, as it rasterizes the
// backdrop's gradient and the text again; no person sees that, while a focus ring is far past it.
const unseen = 2;

/** How far apart two shots of the same size are: the most any channel of any pixel differs, in levels of 255. */
async function levelsApart(page: Page, one: string, other: string): Promise<number> {
  return page.evaluate(async (shots) => {
    const pixels = await Promise.all(
      shots.map(async (shot) => {
        const picture = new Image();
        picture.src = `data:image/png;base64,${shot}`;
        await picture.decode();
        const canvas = new OffscreenCanvas(picture.width, picture.height);
        const context = canvas.getContext('2d')!;
        context.drawImage(picture, 0, 0);
        return context.getImageData(0, 0, picture.width, picture.height).data;
      }),
    );
    const [first, second] = pixels as [Uint8ClampedArray, Uint8ClampedArray];
    if (first.length !== second.length) return 255;
    let most = 0;
    for (let at = 0; at < first.length; at++) most = Math.max(most, Math.abs(first[at]! - second[at]!));
    return most;
  }, [one, other]);
}

// A203: the list by keys alone, then a drag out to its own pill; each row is a step on the same page.
test('TheListWorksWithTheKeyboard', async ({ page }) => {
  await openWindow(page);
  const cpuLoad = text('MetricName.CpuLoad');
  const steps: {
    name: string;
    keys: string[];
    type?: string;
    selected?: string;
    check?: (settings: Settings) => unknown;
    expected?: unknown;
    then?: (page: Page) => Promise<void>;
  }[] = [
    { name: 'Home goes to the first row', keys: ['Home'], selected: 'Taskbar' },
    { name: 'End goes to the last row', keys: ['End'], selected: text('MetricName.GpuTemperature') },
    { name: 'the arrows never leave the list', keys: ['ArrowDown'], selected: text('MetricName.GpuTemperature') },
    {
      name: 'Left goes to the parent',
      keys: ['Home', 'ArrowLeft'],
      then: async (on) => expect(on.getByRole('treeitem', { selected: true, expanded: false })).toBeVisible(),
    },
    {
      name: 'Right unfolds, then goes to the first child',
      keys: ['ArrowRight', 'ArrowRight'],
      then: async (on) => expect(selectedRow(on)).not.toHaveAccessibleName(startingWith('Taskbar')),
    },
    { name: 'letters go to the next row they start', keys: [], type: cpuLoad.slice(0, 5), selected: cpuLoad },
    { name: 'F2 renames and Enter keeps it', keys: ['F2'], type: 'Cores', check: (settings) => itemOf(settings, 'cpu.load').name, expected: 'Cores' },
    { name: 'Esc puts the name back', keys: ['F2'], type: 'Other', check: (settings) => itemOf(settings, 'cpu.load').name, expected: 'Cores' },
    // Cold review: a move that moved nothing was said, and its row selected, at the next edit of anything.
    { name: 'Alt+Up on the first pill moves nothing', keys: ['ArrowLeft', 'Alt+ArrowUp', 'ArrowRight', 'ArrowDown', 'ArrowDown'], selected: 'Cores' },
    { name: 'and the next edit keeps the selection', keys: ['F2'], type: 'CPU cores', selected: 'CPU cores' },
    { name: 'Alt+Down moves the item within its overlay', keys: ['Alt+ArrowDown'], check: (settings) => ids(settings, 0).indexOf('cpu.load'), expected: 3 },
    { name: 'Alt+Up moves it back', keys: ['Alt+ArrowUp'], check: (settings) => ids(settings, 0).indexOf('cpu.load'), expected: 2 },
    { name: 'Shift+F10 opens the row menu and Esc gives the row back', keys: ['Shift+F10'], then: async (on) => {
      await expect(on.getByRole('menu')).toBeVisible();
      await on.keyboard.press('Escape');
      await expect(selectedRow(on)).toBeFocused();
    } },
    {
      name: 'Delete removes the item and selects the next row',
      keys: ['Delete'],
      selected: text('MetricName.CpuPower'),
      check: (settings) => ids(settings, 0).includes('cpu.load'),
      expected: false,
    },
    { name: 'Ctrl+N opens the catalog', keys: ['Control+N'], then: async (on) => {
      await expect(on.getByRole('dialog', { name: text('UiText.Catalog') })).toBeVisible();
      await on.keyboard.press('Escape');
      await expect(on.getByRole('dialog')).toBeHidden();
    } },
    {
      name: 'Ctrl+, opens Settings',
      keys: ['Control+Comma'],
      then: async (on) => expect(on.getByRole('heading', { name: text('UiText.SettingsPage') })).toBeVisible(),
    },
    { name: 'Ctrl+F goes back to the list', keys: ['Control+F'], then: async (on) => expect(selectedRow(on)).toBeFocused() },
    { name: 'F1 shows the shortcuts', keys: ['F1'], then: async (on) => {
      await expect(on.getByRole('dialog', { name: text('UiText.KeyboardShortcuts') })).toBeVisible();
      await on.keyboard.press('Escape');
    } },
  ];

  // Night QA: the window, opened from the overlay's or the tray's menu, showed the focus ring on its row before any key.
  await test.step('the focused row shows its ring only once a key is pressed', async () => {
    // As a person sees it: focused before any key, the row looks as it does unfocused, and a key changes that.
    const looks = async (): Promise<string> => (await selectedRow(page).screenshot({ animations: 'disabled' })).toString('base64');
    await expect(selectedRow(page)).toBeFocused();
    const focused = await looks();
    await selectedRow(page).evaluate((row) => (row as HTMLElement).blur());
    const unfocused = await looks();
    expect(await levelsApart(page, focused, unfocused)).toBeLessThanOrEqual(unseen);
    await selectedRow(page).evaluate((row) => (row as HTMLElement).focus());
    await page.keyboard.press('Home');
    await expect.poll(() => selectedRow(page).evaluate((row) => row.matches(':focus-visible'))).toBe(true);
    expect(await levelsApart(page, await looks(), unfocused)).toBeGreaterThan(unseen);
  });

  await select(page, 'Taskbar');
  for (const step of steps) {
    await test.step(step.name, async () => {
      for (const key of step.keys) {
        await page.keyboard.press(key);
      }
      if (step.type !== undefined) {
        await page.keyboard.type(step.type);
        if (step.keys.includes('F2')) {
          await page.keyboard.press(step.type === 'Other' ? 'Escape' : 'Enter');
        }
      }
      if (step.selected !== undefined) {
        await expect(selectedRow(page)).toHaveAccessibleName(startingWith(step.selected));
        await expect(selectedRow(page)).toBeFocused();
      }
      const check = step.check;
      if (check !== undefined) {
        await expect.poll(async () => check(await settingsOf(page))).toEqual(step.expected);
      }
      await step.then?.(page);
    });
  }

  await test.step('rows keep their names while values refresh', async () => {
    await select(page, text('MetricName.CpuTemperature'));
    await setFake(page, { sensors: recording('admin').map((sensor) => ({ ...sensor, value: sensor.value === null ? null : sensor.value + 1 })) });
    await expect(selectedRow(page)).toHaveAccessibleName(text('MetricName.CpuTemperature'));
  });

  await test.step('an item dragged out of its pill gets its own', async () => {
    const power = list(page).getByRole('treeitem', { name: text('MetricName.CpuPower') });
    const nextPill = list(page).getByRole('treeitem', { name: startingWith(text('UiText.PillName', text('MetricLabel.Gpu'))) }).first();
    await power.dragTo(nextPill, { targetPosition: { x: 8, y: 1 } });
    await expect.poll(async () => itemOf(await settingsOf(page), 'cpu.power').own).toBe(true);
  });

  const pill = (label: string) => list(page).getByRole('treeitem', { name: startingWith(text('UiText.PillName', text(label))) });
  await test.step('a pill dragged onto another moves before it', async () => {
    await pill('MetricLabel.Ram').dragTo(pill('MetricLabel.Cpu').first(), { targetPosition: { x: 8, y: 4 } });
    await expect.poll(async () => ids(await settingsOf(page), 0)[0]).toBe('ram.used');
  });

  await test.step('Move to takes a pill to another overlay by keys', async () => {
    await pill('MetricLabel.Ram').focus();
    await page.keyboard.press('Shift+F10');
    await page.getByRole('menuitem', { name: text('UiText.MoveToOverlay', 'Top right') }).press('Enter');
    await expect.poll(async () => ids(await settingsOf(page), 1)).toEqual(['gpu.temperature', 'ram.used']);
  });

  await test.step("an item dragged onto another overlay's pill moves there", async () => {
    const theirs = pill('MetricLabel.Gpu').last();
    const high = (await theirs.boundingBox())?.height ?? 0;
    await list(page).getByRole('treeitem', { name: text('MetricName.GpuLoad') }).dragTo(theirs, { targetPosition: { x: 8, y: high - 2 } });
    await expect.poll(async () => ids(await settingsOf(page), 1)).toEqual(['gpu.load', 'gpu.temperature', 'ram.used']);
  });
});

// A204: the catalog opened from each overlay adds there, or where its picker says, with Enter or Ctrl+Enter.
test.describe('TheCatalogAddsWithTheKeyboard', () => {
  // The CPU's own, whose names the catalog shows as they are, so each is the first its name finds.
  const sensors = recording('admin').filter((sensor) => sensor.hardwareType === 'Cpu' && sensor.type === 'Temperature' && sensor.value !== null);
  const first = sensors[0];
  const second = sensors[1];
  if (first === undefined || second === undefined) {
    throw new Error('The recording has fewer than two temperatures.');
  }
  // The smallest window at 200 % text is half as large to the page, as WebView2 draws everything twice as large.
  const rows: { from: Overlay['name']; pick?: string; lands: number; small?: boolean }[] = [
    { from: 'Taskbar', lands: 0 },
    { from: 'Top right', lands: 1 },
    { from: 'Taskbar', pick: 'Top right', lands: 1 },
    { from: 'Taskbar', lands: 0, small: true },
  ];

  for (const row of rows) {
    test(`from ${row.from} to ${row.lands}${row.small === true ? ' in the smallest window at 200 % text' : ''}`, async ({ page }) => {
      await openWindow(page);
      await select(page, row.from);
      if (row.small === true) {
        await page.setViewportSize({ width: 380, height: 280 });
      }
      await page.keyboard.press('Control+N');
      const catalog = page.getByRole('dialog', { name: text('UiText.Catalog') });
      const search = catalog.getByRole('searchbox');
      // Whatever the window's size, the catalog shows a sensor under its search and types.
      await expect(catalog.getByRole('listbox', { name: text('UiText.Catalog') }).getByRole('option').first()).toBeInViewport({ ratio: 0.75 });
      if (row.pick !== undefined) {
        await chooseByKeys(page, catalog.getByRole('combobox', { name: text('CatalogText.CatalogOverlay') }), row.pick);
      }

      await search.fill(first.name);
      await search.press('Enter');
      await expect(catalog).toBeHidden();
      await expect.poll(async () => ids(await settingsOf(page), row.lands)).toContain(first.id);
      // A small window folds its list, so the row added is seen once the window is large again.
      if (row.small === true) {
        await page.setViewportSize({ width: 1920, height: 1080 });
      }
      await expect(selectedRow(page)).toBeInViewport();

      await page.keyboard.press('Control+N');
      await search.fill(second.name);
      await search.press('ArrowDown');
      await page.keyboard.press('Control+Enter');
      await expect.poll(async () => ids(await settingsOf(page), row.lands)).toContain(second.id);
      await expect(catalog).toBeVisible();

      await page.keyboard.press('Escape');
      await expect(search).toHaveValue('');
      await page.keyboard.press('Escape');
      await expect(catalog).toBeHidden();
    });
  }

  // A245: what nobody would add waits behind "Show all", a board's input says its likely name, and F2 renames its sensor.
  test('the catalog hides, names likely and renames', async ({ page }) => {
    const layer = recording('admin').find((sensor) => sensor.hardwareType.startsWith('Gpu') && sensor.name.startsWith('D3D '));
    if (layer === undefined) {
      throw new Error('The recording has no GPU engine layer.');
    }
    await openWindow(page);
    await select(page, 'Taskbar');
    await page.keyboard.press('Control+N');
    const catalog = page.getByRole('dialog', { name: text('UiText.Catalog') });
    const search = catalog.getByRole('searchbox');
    const options = catalog.getByRole('listbox', { name: text('UiText.Catalog') }).getByRole('option');
    const showAll = catalog.getByRole('button', { name: text('CatalogText.ShowAll') });

    await search.fill(layer.name);
    await expect(options).toHaveCount(0);
    await showAll.click();
    await expect(showAll).toHaveAttribute('aria-pressed', 'true');
    await expect(options.first()).toHaveAccessibleName(startingWith(layer.name));

    await search.fill(text('LikelyName.Vrm'));
    await search.press('ArrowDown');
    await expect(options.first()).toHaveAccessibleName(startingWith(text('LikelyName.Vrm')));
    await page.keyboard.press('F2');
    await page.keyboard.type('Socket');
    await page.keyboard.press('Escape');
    await expect(catalog).toBeVisible();
    await expect(options.first()).toBeFocused();
    await page.keyboard.press('F2');
    await page.keyboard.type(`${text('LikelyName.Vrm')} socket`);
    await page.keyboard.press('Enter');
    await expect.poll(async () => (await settingsOf(page)).sensorNames).toEqual({ '/lpc/it8689e/0/temperature/4': `${text('LikelyName.Vrm')} socket` });
    await expect(options.first()).toBeFocused();
    await page.keyboard.press('Enter');
    await expect.poll(async () => itemOf(await settingsOf(page), '/lpc/it8689e/0/temperature/4').name).toBe(`${text('LikelyName.Vrm')} socket`);

    // Live QA: a click in the middle of a row adds it, and its pencil never stands in the way.
    const count = async (): Promise<number> => (await settingsOf(page)).overlays.flatMap((overlay) => overlay.items).length;
    const before = await count();
    await page.keyboard.press('Control+N');
    await search.fill(text('LikelyName.Vrm'));
    await options.first().click();
    await expect.poll(count).toBe(before + 1);
  });

  // A232 and A238: adapters, drives and a battery each make a group named by their role, and only a laptop has a battery.
  const grouped = (key: string): RegExp => startingWith(text('UiText.SensorOn', text(key), ''));
  for (const laptop of [false, true]) {
    test(laptop ? 'a laptop lists its battery' : 'a desktop lists its adapters and drives, and no battery', async ({ page }) => {
      const sensors = withDrivesAndBattery(laptop);
      await openWindow(page, fakeState({ sensors }));
      await select(page, 'Taskbar');
      await page.keyboard.press('Control+N');
      const catalog = page.getByRole('dialog', { name: text('UiText.Catalog') });
      await expect.poll(() => catalog.getByRole('group', { name: grouped('CatalogText.GroupNetwork') }).count()).toBeGreaterThan(1);
      await expect(catalog.getByRole('group', { name: grouped('CatalogText.GroupDisk') })).toHaveCount(2);
      await expect(catalog.getByRole('group', { name: grouped('CatalogText.GroupBattery') })).toHaveCount(laptop ? 1 : 0);

      await catalog.getByRole('radio', { name: text('SensorKind.Throughput') }).check();
      await expect(catalog.getByRole('listbox', { name: text('UiText.Catalog') }).getByRole('option')).toHaveCount(
        sensors.filter((sensor) => sensor.type === 'Throughput').length,
      );
    });
  }
});

// A210: overlays made, duplicated, merged, hidden, shown and deleted from the list and the overlay's page, and where the focus lands.
test('OverlaysAreManagedFromTheList', async ({ page }) => {
  const three = fakeState();
  three.settings.overlays.push({
    name: 'Bottom left',
    snap: { spot: 'bottom-left' },
    items: [{ id: 'cpu.clock', name: text('MetricName.CpuClock'), metric: 'cpu.clock' }],
  });
  await openWindow(page, three);
  const names = async (): Promise<string[]> => (await settingsOf(page)).overlays.map((overlay) => overlay.name);
  const row = (name: string) => list(page).getByRole('treeitem', { name: startingWith(name) });
  const rowMenu = async (name: string, entry: string): Promise<void> => {
    await row(name).first().focus();
    await page.keyboard.press('Shift+F10');
    await page.getByRole('menuitem', { name: text(entry) }).press('Enter');
  };

  await test.step('duplicate', async () => {
    await rowMenu('Top right', 'UiText.DuplicateOverlay');
    await expect.poll(async () => (await names()).length).toBe(4);
    const copy = (await names())[2] ?? '';
    await expect(row(copy)).toBeFocused();
    // QA round 5 (P16): an overlay duplicated, merged or deleted says so in a toast with Undo, as an item removed does.
    await expect(toast(page)).toBeVisible();
  });

  await test.step('no fifth overlay', async () => {
    await page.getByRole('button', { name: text('UiText.MoreActions') }).click();
    await expect(page.getByRole('menuitem', { name: text('UiText.NewOverlay') })).toBeDisabled();
    await page.keyboard.press('Escape');
  });

  await test.step('merge the copy into the taskbar', async () => {
    const copy = (await names())[2] ?? '';
    await rowMenu(copy, 'UiText.MergeInto');
    await page.getByRole('menuitem', { name: 'Taskbar' }).press('Enter');
    await expect.poll(names).toEqual(['Taskbar', 'Top right', 'Bottom left']);
    expect(ids(await settingsOf(page), 0)).toContain('gpu.temperature-2');
    await expect(row('Taskbar')).toBeFocused();
  });

  await test.step('hide from the row, show from the page', async () => {
    await rowMenu('Top right', 'UiText.HideThisOverlay');
    await expect.poll(async () => overlayOf(await settingsOf(page), 1).hidden).toBe(true);
    // Hidden, it shows only in the window: its row and its spot on the map say so.
    await expect(row('Top right').first()).toHaveAccessibleDescription(text('UiText.OverlayHidden'));
    await select(page, 'Top right');
    await expect(
      page.getByRole('radiogroup', { name: text('UiText.MonitorMap') }).getByRole('radio', { checked: true }),
    ).toHaveAccessibleDescription(text('UiText.OverlayHidden'));
    await page.getByRole('button', { name: text('UiText.ShowThisOverlay') }).press('Enter');
    await expect.poll(async () => overlayOf(await settingsOf(page), 1).hidden ?? false).toBe(false);
    await expect(page.getByRole('button', { name: text('UiText.HideThisOverlay') })).toBeFocused();
  });

  await test.step('delete from the page', async () => {
    await page.getByRole('button', { name: text('UiText.DeleteThisOverlay') }).press('Enter');
    await expect.poll(names).toEqual(['Taskbar', 'Bottom left']);
    await expect(row('Bottom left')).toBeFocused();
    await expect(toast(page)).toBeVisible();
  });

  await test.step('new overlay', async () => {
    await page.getByRole('button', { name: text('UiText.MoreActions') }).click();
    await page.getByRole('menuitem', { name: text('UiText.NewOverlay') }).press('Enter');
    await expect.poll(async () => (await names()).length).toBe(3);
    await expect(row((await names())[2] ?? '')).toBeFocused();
  });
});

// Alt+arrows on an overlay snapped to its place move nothing, and the window says why where anyone sees it.
test('AltArrowsOnASnappedOverlaySayWhyTheyMoveNothing', async ({ page }) => {
  await openWindow(page);
  const taskbar = list(page).getByRole('treeitem', { level: 1 }).first();
  const before = overlayOf(await settingsOf(page), 0).position;
  await taskbar.focus();
  await taskbar.press('Alt+ArrowRight');

  await expect(toast(page)).toBeVisible();
  expect(overlayOf(await settingsOf(page), 0).position).toEqual(before);
  await expect(taskbar).toBeFocused();
});

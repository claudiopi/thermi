import { expect, test, type Page } from '@playwright/test';
import { chooseByKeys, fakeState, itemOf, list, openWindow, overlayOf, recording, select, selectedRow, setFake, settingsOf, startingWith, text, windowWith, withDrivesAndBattery, type Overlay, type Settings } from './fakeBridge';

const ids = (settings: Settings, overlay: number): string[] => overlayOf(settings, overlay).items.map((item) => item.id);

// A203: the list by keys alone, then a drag out to its own pill; each row is a step on the same page.
test('TheListWorksWithTheKeyboard', async ({ page }) => {
  await openWindow(page);
  const cpuLoad = text('MetricName.CpuLoad');
  const steps: { name: string; keys: string[]; type?: string; selected?: string; check?: (settings: Settings) => unknown; expected?: unknown; then?: (page: Page) => Promise<void> }[] = [
    { name: 'Home goes to the first row', keys: ['Home'], selected: 'Taskbar' },
    { name: 'End goes to the last row', keys: ['End'], selected: text('MetricName.GpuTemperature') },
    { name: 'the arrows never leave the list', keys: ['ArrowDown'], selected: text('MetricName.GpuTemperature') },
    { name: 'Left goes to the parent', keys: ['Home', 'ArrowLeft'], then: async (on) => expect(on.getByRole('treeitem', { selected: true, expanded: false })).toBeVisible() },
    { name: 'Right unfolds, then goes to the first child', keys: ['ArrowRight', 'ArrowRight'], then: async (on) => expect(selectedRow(on)).not.toHaveAccessibleName(startingWith('Taskbar')) },
    { name: 'letters go to the next row they start', keys: [], type: cpuLoad.slice(0, 5), selected: cpuLoad },
    { name: 'F2 renames and Enter keeps it', keys: ['F2'], type: 'Cores', check: (settings) => itemOf(settings, 'cpu.load').name, expected: 'Cores' },
    { name: 'Esc puts the name back', keys: ['F2'], type: 'Other', check: (settings) => itemOf(settings, 'cpu.load').name, expected: 'Cores' },
    { name: 'Alt+Down moves the item within its overlay', keys: ['Alt+ArrowDown'], check: (settings) => ids(settings, 0).indexOf('cpu.load'), expected: 3 },
    { name: 'Alt+Up moves it back', keys: ['Alt+ArrowUp'], check: (settings) => ids(settings, 0).indexOf('cpu.load'), expected: 2 },
    { name: 'Shift+F10 opens the row menu and Esc gives the row back', keys: ['Shift+F10'], then: async (on) => {
      await expect(on.getByRole('menu')).toBeVisible();
      await on.keyboard.press('Escape');
      await expect(selectedRow(on)).toBeFocused();
    } },
    { name: 'Delete removes the item and selects the next row', keys: ['Delete'], selected: text('MetricName.CpuPower'), check: (settings) => ids(settings, 0).includes('cpu.load'), expected: false },
    { name: 'Ctrl+N opens the catalog', keys: ['Control+N'], then: async (on) => {
      await expect(on.getByRole('dialog', { name: text('UiText.Catalog') })).toBeVisible();
      await on.keyboard.press('Escape');
      await expect(on.getByRole('dialog')).toBeHidden();
    } },
    { name: 'Ctrl+, opens Settings', keys: ['Control+Comma'], then: async (on) => expect(on.getByRole('heading', { name: text('UiText.SettingsPage') })).toBeVisible() },
    { name: 'Ctrl+F goes back to the list', keys: ['Control+F'], then: async (on) => expect(selectedRow(on)).toBeFocused() },
    { name: 'F1 shows the shortcuts', keys: ['F1'], then: async (on) => {
      await expect(on.getByRole('dialog', { name: text('UiText.KeyboardShortcuts') })).toBeVisible();
      await on.keyboard.press('Escape');
    } },
  ];

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
});

// A204: the catalog opened from each overlay adds there, or where its picker says, with Enter or Ctrl+Enter.
test('TheCatalogAddsWithTheKeyboard', async ({ context }) => {
  const sensors = recording('admin').filter((sensor) => sensor.type === 'Temperature' && sensor.value !== null);
  const first = sensors[0];
  const second = sensors[1];
  if (first === undefined || second === undefined) {
    throw new Error('The recording has fewer than two temperatures.');
  }
  const rows: { from: Overlay['name']; pick?: string; lands: number }[] = [
    { from: 'Taskbar', lands: 0 },
    { from: 'Top right', lands: 1 },
    { from: 'Taskbar', pick: 'Top right', lands: 1 },
  ];

  for (const row of rows) {
    await test.step(`from ${row.from} to ${row.lands}`, async () => {
      const page = await windowWith(context, fakeState());
      await select(page, row.from);
      await page.keyboard.press('Control+N');
      const catalog = page.getByRole('dialog', { name: text('UiText.Catalog') });
      const search = catalog.getByRole('searchbox');
      if (row.pick !== undefined) {
        await chooseByKeys(page, catalog.getByRole('combobox', { name: text('CatalogText.CatalogOverlay') }), row.pick);
      }

      await search.fill(first.name);
      await search.press('Enter');
      await expect(catalog).toBeHidden();
      await expect.poll(async () => ids(await settingsOf(page), row.lands)).toContain(first.id);
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
      await page.close();
    });
  }

  // A245: what nobody would add waits behind "Show all", a board's input says its likely name, and F2 renames its sensor.
  await test.step('the catalog hides, names likely and renames', async () => {
    const layer = recording('admin').find((sensor) => sensor.hardwareType.startsWith('Gpu') && sensor.name.startsWith('D3D '));
    if (layer === undefined) {
      throw new Error('The recording has no GPU engine layer.');
    }
    const page = await windowWith(context, fakeState());
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
    await page.close();
  });

  // A232 and A238: adapters, drives and a battery each make a group named by their role, and only a laptop has a battery.
  const grouped = (key: string): RegExp => startingWith(text('UiText.SensorOn', text(key), ''));
  for (const laptop of [false, true]) {
    await test.step(laptop ? 'a laptop lists its battery' : 'a desktop lists its adapters and drives, and no battery', async () => {
      const sensors = withDrivesAndBattery(laptop);
      const page = await windowWith(context, fakeState({ sensors }));
      await select(page, 'Taskbar');
      await page.keyboard.press('Control+N');
      const catalog = page.getByRole('dialog', { name: text('UiText.Catalog') });
      await expect.poll(() => catalog.getByRole('group', { name: grouped('CatalogText.GroupNetwork') }).count()).toBeGreaterThan(1);
      await expect(catalog.getByRole('group', { name: grouped('CatalogText.GroupDisk') })).toHaveCount(2);
      await expect(catalog.getByRole('group', { name: grouped('CatalogText.GroupBattery') })).toHaveCount(laptop ? 1 : 0);

      await catalog.getByRole('radio', { name: text('SensorKind.Throughput') }).check();
      await expect(catalog.getByRole('listbox', { name: text('UiText.Catalog') }).getByRole('option')).toHaveCount(sensors.filter((sensor) => sensor.type === 'Throughput').length);
      await page.close();
    });
  }
});

// A210: overlays made, duplicated, merged, hidden, shown and deleted from the list and the overlay's page, and where the focus lands.
test('OverlaysAreManagedFromTheList', async ({ page }) => {
  const three = fakeState();
  three.settings.overlays.push({ name: 'Bottom left', snap: { spot: 'bottom-left' }, items: [{ id: 'cpu.clock', name: text('MetricName.CpuClock'), metric: 'cpu.clock' }] });
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
    await expect(page.getByRole('radiogroup', { name: text('UiText.MonitorMap') }).getByRole('radio', { checked: true })).toHaveAccessibleDescription(text('UiText.OverlayHidden'));
    await page.getByRole('button', { name: text('UiText.ShowThisOverlay') }).press('Enter');
    await expect.poll(async () => overlayOf(await settingsOf(page), 1).hidden ?? false).toBe(false);
    await expect(page.getByRole('button', { name: text('UiText.HideThisOverlay') })).toBeFocused();
  });

  await test.step('delete from the page', async () => {
    await page.getByRole('button', { name: text('UiText.DeleteThisOverlay') }).press('Enter');
    await expect.poll(names).toEqual(['Taskbar', 'Bottom left']);
    await expect(row('Bottom left')).toBeFocused();
  });

  await test.step('new overlay', async () => {
    await page.getByRole('button', { name: text('UiText.MoreActions') }).click();
    await page.getByRole('menuitem', { name: text('UiText.NewOverlay') }).press('Enter');
    await expect.poll(async () => (await names()).length).toBe(3);
    await expect(row((await names())[2] ?? '')).toBeFocused();
  });
});

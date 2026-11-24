import { type Locator, type Page } from '@playwright/test';
import {
  chooseByKeys,
  expect,
  fakeOf,
  fakeState,
  itemOf,
  openWindow,
  recording,
  select,
  settingsOf,
  setFake,
  test,
  text,
  type FakeState,
  type Item,
} from './fakeBridge';

const itemIn = async (page: Page, id: string): Promise<Item> => itemOf(await settingsOf(page), id);

// Edge draws its scrollbars here as the window does, where headless Edge would hide them.
test.use({ launchOptions: { ignoreDefaultArgs: ['--hide-scrollbars'] } });

const cpuTemperatures = recording('admin').filter(
  (sensor) => sensor.hardwareType === 'Cpu' && sensor.type === 'Temperature' && sensor.value !== null,
);
const [ccd1, ccd2] = cpuTemperatures.slice(-2);
if (ccd1 === undefined || ccd2 === undefined) {
  throw new Error('The recording has fewer than two CPU temperatures.');
}

const fieldOf = (page: Page, role: 'combobox' | 'switch' | 'textbox' | 'spinbutton' | 'button', key: string): Locator =>
  page.getByRole('main').getByRole(role, { name: text(key) });

// A205: each row of the item page, changed by keys, lands in the settings; a row that cannot apply is off with its reason.
test.describe('TheItemPageEditsTheItem', () => {
  // Each from the CPU temperature as it starts; a row behind More options opens it first.
  const rows: { name: string; more?: boolean; act: (page: Page) => Promise<void>; read: (item: Item) => unknown; expected: unknown }[] = [
    {
      name: 'Sensor',
      act: (page) => chooseByKeys(page, fieldOf(page, 'combobox', 'UiText.SensorField'), ccd1.name),
      read: (item) => item.sensor,
      expected: ccd1.id,
    },
    { name: 'Label', act: async (page) => {
      await fieldOf(page, 'textbox', 'UiText.LabelField').fill('CORE');
      await page.keyboard.press('Enter');
    }, read: (item) => item.label, expected: 'CORE' },
    { name: 'Notify me', act: (page) => fieldOf(page, 'switch', 'UiText.NotifyMe').press('Space'), read: (item) => item.notify, expected: true },
    { name: 'Tray icon', act: (page) => fieldOf(page, 'switch', 'UiText.OwnTrayIcon').press('Space'), read: (item) => item.tray, expected: true },
    {
      name: 'What it shows',
      more: true,
      act: (page) => chooseByKeys(page, fieldOf(page, 'combobox', 'UiText.StatField'), text('ItemStat.Max')),
      read: (item) => item.stat,
      expected: 'max',
    },
    {
      name: 'Smoothing',
      more: true,
      act: (page) => chooseByKeys(page, fieldOf(page, 'combobox', 'UiText.SmoothField'), text('UiText.SmoothSeconds', 3)),
      read: (item) => item.smooth,
      expected: 3,
    },
    {
      name: 'Decimals',
      more: true,
      act: (page) => chooseByKeys(page, fieldOf(page, 'combobox', 'UiText.DecimalsField'), '1'),
      read: (item) => item.decimals,
      expected: 1,
    },
    {
      name: 'Graph',
      act: (page) => chooseByKeys(page, fieldOf(page, 'combobox', 'UiText.GraphField'), text('GraphStyle.Line')),
      read: (item) => item.graph,
      expected: 'line',
    },
    {
      name: 'Only on alert',
      more: true,
      act: (page) => fieldOf(page, 'switch', 'UiText.AlertOnly').press('Space'),
      read: (item) => item.alertOnly,
      expected: true,
    },
    { name: 'Offset', more: true, act: async (page) => {
      await fieldOf(page, 'spinbutton', 'UiText.OffsetField').fill('-5');
      await page.keyboard.press('Enter');
    }, read: (item) => item.offset, expected: -5 },
    { name: 'Value color', more: true, act: async (page) => {
      await fieldOf(page, 'button', 'UiText.ValueColor').press('Enter');
      await page.getByRole('textbox', { name: text('UiText.HexColor') }).fill('#FFB000');
      await page.keyboard.press('Enter');
    }, read: (item) => item.color, expected: '#FFB000' },
    { name: 'Combine', more: true, act: async (page) => {
      await fieldOf(page, 'button', 'UiText.Combine').press('Enter');
      const dialog = page.getByRole('dialog');
      await dialog.getByRole('checkbox', { name: ccd1.name }).press('Space');
      await dialog.getByRole('checkbox', { name: ccd2.name }).press('Space');
      await chooseByKeys(page, dialog.getByRole('combobox', { name: text('UiText.CombineField') }), text('Combine.Max'));
      await dialog.getByRole('button', { name: text('UiText.CombineAdd') }).press('Enter');
    }, read: (item) => [item.sensors, item.combine], expected: [[ccd1.id, ccd2.id], 'max'] },
    {
      name: 'No graph',
      act: (page) => chooseByKeys(page, fieldOf(page, 'combobox', 'UiText.GraphField'), text('GraphStyle.None')),
      read: (item) => item.graph,
      expected: 'none',
    },
  ];
  for (const row of rows) {
    test(row.name, async ({ page }) => {
      await openWindow(page);
      await select(page, text('MetricName.CpuTemperature'));
      if (row.more === true) {
        await fieldOf(page, 'button', 'UiText.MoreOptions').press('Enter');
      }
      await row.act(page);
      await expect.poll(async () => row.read(await itemIn(page, 'cpu.temperature'))).toEqual(row.expected);
    });
  }

  // Cold review 4 (Q12): a pill's first item already starts one, so it offers no Separate pill that could not change.
  test("A pill's first item offers no Separate pill", async ({ page }) => {
    await openWindow(page);
    await select(page, text('MetricName.CpuTemperature'));
    await expect(fieldOf(page, 'switch', 'UiText.OwnPill')).toHaveCount(0);
  });

  const off: { name: string; item: string; state: () => FakeState; control: (page: Page) => Locator; reason: string }[] = [
    { name: 'Only on alert warned by nothing', item: 'MetricName.RamUsed', state: () => {
      const state = fakeState();
      const used = state.settings.overlays[0]?.items.find((item) => item.id === 'ram.used');
      if (used !== undefined) {
        delete used.warnBy;
      }
      return state;
    }, control: (page) => fieldOf(page, 'switch', 'UiText.AlertOnly'), reason: 'UiText.AlertOnlyNeedsWarnBy' },
  ];
  for (const row of off) {
    test(row.name, async ({ page }) => {
      await openWindow(page, row.state());
      await select(page, text(row.item));
      await fieldOf(page, 'button', 'UiText.MoreOptions').press('Enter');
      await expect(row.control(page)).toBeDisabled();
      await expect(row.control(page)).toHaveAccessibleDescription(text(row.reason));
    });
  }

  // Warn by offers the item's own reading and its other readings alone: a temperature only itself, a fan its speed and
  // its duty; what thresholds cannot judge is offered no graph and no only on alert.
  const warnings: { name: string; item: string; id: string; offered: number; judged: boolean; pick?: string; expected?: unknown }[] = [
    { name: 'A temperature warns only by itself', item: text('MetricName.CpuTemperature'), id: 'cpu.temperature', offered: 1, judged: true },
    {
      name: 'A fan warns by its speed or its duty',
      item: text('MetricName.CpuFan'),
      id: 'fan.cpu',
      offered: 2,
      judged: true,
      pick: text('SensorKind.Control'),
      expected: { metric: 'fan.cpu.duty' },
    },
    { name: 'Power has nothing to warn by', item: text('MetricName.CpuPower'), id: 'cpu.power', offered: 1, judged: false },
  ];
  for (const row of warnings) {
    test(row.name, async ({ page }) => {
      await openWindow(page);
      await select(page, row.item);
      await expect(fieldOf(page, 'combobox', 'UiText.GraphField')).toHaveCount(row.judged ? 1 : 0);
      await fieldOf(page, 'button', 'UiText.MoreOptions').press('Enter');
      const box = fieldOf(page, 'combobox', 'UiText.WarnByField');
      // A Warn by row that would offer only one choice is not shown.
      await expect(box).toHaveCount(row.offered > 1 ? 1 : 0);
      if (row.offered > 1) {
        await box.press('Alt+ArrowDown');
        await expect(page.getByRole('option')).toHaveCount(row.offered);
        await page.keyboard.press('Escape');
      }
      await expect(fieldOf(page, 'switch', 'UiText.AlertOnly')).toHaveCount(row.judged ? 1 : 0);
      if (row.pick !== undefined) {
        await chooseByKeys(page, box, row.pick);
        await expect.poll(async () => (await itemIn(page, row.id)).warnBy).toEqual(row.expected);
      }
    });
  }

  test('A speed in bits, offered only for a speed', async ({ page }) => {
    const state = fakeState({ sensors: recording('admin-net-disk') });
    const speed = state.sensors.find((sensor) => sensor.type === 'Throughput' && sensor.hardwareType === 'Network' && sensor.value !== null);
    if (speed === undefined) {
      throw new Error('The recording has no network speed.');
    }
    state.settings.overlays[0]?.items.push({ id: speed.id, name: speed.name, sensor: speed.id });
    await openWindow(page, state);
    await select(page, text('MetricName.CpuTemperature'));
    await fieldOf(page, 'button', 'UiText.MoreOptions').press('Enter');
    await expect(fieldOf(page, 'switch', 'UiText.Bits')).toHaveCount(0);
    await select(page, speed.name);
    await fieldOf(page, 'button', 'UiText.MoreOptions').press('Enter');
    await fieldOf(page, 'switch', 'UiText.Bits').press('Space');
    await expect.poll(async () => (await itemIn(page, speed.id)).bits).toBe(true);
  });

  // Live QA: Windows' own items read an address, a time of day or a time since start, which nothing counts on.
  test('A reading that is no number offers none of the number tools', async ({ page }) => {
    const windows = [
      { id: '/system/0/address/0', hardwareType: 'System', hardware: 'Windows', type: 'Address', name: 'Local IP', value: 3232235796 },
      { id: '/system/0/time/0', hardwareType: 'System', hardware: 'Windows', type: 'Time', name: 'Clock', value: 45296 },
      { id: '/system/0/duration/0', hardwareType: 'System', hardware: 'Windows', type: 'Duration', name: 'Uptime', value: 8040 },
    ];
    const state = fakeState({ sensors: [...recording('admin'), ...windows] });
    state.settings.overlays[0]?.items.push(...windows.map((sensor) => ({ id: sensor.id, name: sensor.name, sensor: sensor.id })));
    await openWindow(page, state);
    const main = page.getByRole('main');
    for (const sensor of windows) {
      await select(page, sensor.name);
      await main.getByRole('button', { name: text('UiText.MoreOptions') }).press('Enter');
      await expect(main.getByRole('button', { name: text('UiText.ValueColor') })).toBeVisible();
      const numberTools = [
        ['combobox', 'UiText.StatField'],
        ['combobox', 'UiText.SmoothField'],
        ['combobox', 'UiText.DecimalsField'],
        ['combobox', 'UiText.GraphField'],
        ['spinbutton', 'UiText.OffsetField'],
      ] as const;
      for (const [role, key] of numberTools) {
        await expect(main.getByRole(role, { name: text(key) }), `${sensor.name}: ${key}`).toHaveCount(0);
      }
    }
  });

  // Live QA: the item page keeps the scrollbar's room, so the chart does not jump when More options makes it scroll.
  test('The page keeps its place as it scrolls', async ({ page }) => {
    const state = fakeState();
    const ram = state.settings.overlays[0]?.items.find((item) => item.id === 'ram.used');
    if (ram !== undefined) {
      ram.stat = 'average';
    }
    await openWindow(page, state);
    await page.setViewportSize({ width: 1920, height: 1080 });
    await select(page, text('MetricName.CpuTemperature'));
    // A window just tall enough for the rows beside the chart, which fills the pane's height, so More options makes it scroll.
    const main = page.getByRole('main');
    const more = await main.getByRole('button', { name: text('UiText.MoreOptions') }).boundingBox();
    const spare = await main.evaluate(
      (pane, bottom) => pane.getBoundingClientRect().top + pane.clientHeight - bottom - 24,
      (more?.y ?? 0) + (more?.height ?? 0),
    );
    await page.setViewportSize({ width: 1920, height: Math.ceil(1080 - spare + 4) });
    const chart = page.getByRole('img', { name: text('UiText.ChartOf', text('MetricName.CpuTemperature')) });
    await expect.poll(() => main.evaluate((pane) => pane.scrollHeight > pane.clientHeight)).toBe(false);
    const across = async (): Promise<[number, number]> => {
      const box = await chart.boundingBox();
      return [box?.x ?? 0, box?.width ?? 0];
    };
    const before = await across();
    await main.getByRole('button', { name: text('UiText.MoreOptions') }).press('Enter');
    await expect.poll(() => main.evaluate((pane) => pane.scrollHeight > pane.clientHeight)).toBe(true);
    expect(await across()).toEqual(before);

    // Owner's QA: the page stays where it was scrolled through whatever is done on it, and what it shows goes back to
    // the value now, its default, as any other choice does.
    await page.setViewportSize({ width: 1500, height: 640 });
    await select(page, text('MetricName.RamUsed'));
    await fieldOf(page, 'button', 'UiText.MoreOptions').click();
    // Scrolled so all of More options shows under the pane's top, each control in view, as a person would see it.
    const at = await main.evaluate((pane, top) => {
      pane.scrollTop = Math.min(pane.scrollHeight - pane.clientHeight, top - pane.getBoundingClientRect().top - 8);
      return pane.scrollTop;
    }, (await fieldOf(page, 'button', 'UiText.MoreOptions').boundingBox())?.y ?? 0);
    expect(at).toBeGreaterThan(0);
    const pick = async (key: string, option: string): Promise<void> => {
      await fieldOf(page, 'combobox', key).click();
      await page.getByRole('option', { name: option, exact: true }).click();
    };
    const acts: { name: string; act: () => Promise<void>; read?: (item: Item) => unknown; expected?: unknown }[] = [
      { name: 'Value now', act: () => pick('UiText.StatField', text('ItemStat.Now')), read: (item) => item.stat, expected: undefined },
      { name: 'Smoothing', act: () => pick('UiText.SmoothField', text('UiText.SmoothSeconds', 3)), read: (item) => item.smooth, expected: 3 },
      { name: 'Decimals', act: () => chooseByKeys(page, fieldOf(page, 'combobox', 'UiText.DecimalsField'), '2'), read: (item) => item.decimals, expected: 2 },
      { name: 'a switch', act: () => fieldOf(page, 'switch', 'UiText.AlertOnly').click(), read: (item) => item.alertOnly, expected: true },
      { name: 'undo', act: () => page.keyboard.press('Control+Z'), read: (item) => item.alertOnly, expected: undefined },
      { name: 'redo', act: () => page.keyboard.press('Control+Y'), read: (item) => item.alertOnly, expected: true },
      { name: 'new readings', act: () => setFake(page, { sensors: recording('admin').map((sensor) => ({ ...sensor, value: (sensor.value ?? 0) + 1 })) }) },
    ];
    for (const row of acts) {
      await test.step(row.name, async () => {
        await row.act();
        if (row.read !== undefined) {
          await expect.poll(async () => row.read?.(await itemIn(page, 'ram.used'))).toEqual(row.expected);
        }
        await fakeOf(page);
        expect(await main.evaluate((pane) => pane.scrollTop)).toBe(at);
      });
    }

    // Cold review 4 (Q8): the next item's page opens at its top, its title in view, with its options closed.
    await main.evaluate((pane) => pane.scrollTo({ top: pane.scrollHeight }));
    await select(page, text('MetricName.CpuLoad'));
    await expect(main.getByRole('heading', { level: 2 })).toBeInViewport();
    await expect(main.getByRole('button', { name: text('UiText.MoreOptions') })).toHaveAttribute('aria-expanded', 'false');
  });
});

// A206: "Find it" runs in the CPU fan's Sensor row, is cancelled, runs again, and its pick changes the item's sensor.
test('FindItRunsInTheSensorRow', async ({ page }) => {
  const boardFan = '/lpc/it8689e/0/fan/1';
  await openWindow(page, fakeState({ finderPick: boardFan }));
  await select(page, text('MetricName.CpuFan'));
  const row = page.getByRole('group', { name: text('UiText.SensorField') });
  const warnByBefore = (await itemIn(page, 'fan.cpu')).warnBy;

  // Cold review 4 (Q7): it asks before it loads the CPU, and a person can say no.
  await row.getByRole('button', { name: text('CatalogText.FindIt') }).press('Enter');
  await row.getByRole('button', { name: text('UiText.Cancel') }).press('Enter');
  expect((await fakeOf(page)).finder.phase).not.toBe('running');
  await row.getByRole('button', { name: text('CatalogText.FindIt') }).press('Enter');
  await row.getByRole('button', { name: text('CatalogText.LoadCpu') }).press('Enter');
  await expect(row.getByRole('progressbar')).toBeVisible();
  await row.getByRole('button', { name: text('UiText.Cancel') }).press('Enter');
  await expect.poll(async () => (await fakeOf(page)).finder.phase).toBe('cancelled');
  await expect(row.getByRole('progressbar')).toBeHidden();

  await row.getByRole('button', { name: text('CatalogText.FindIt') }).press('Enter');
  await row.getByRole('button', { name: text('CatalogText.LoadCpu') }).press('Enter');
  await setFake(page, { finder: { phase: 'done', pick: boardFan } });
  await row.getByRole('button', { name: text('CatalogText.UseIt') }).press('Enter');
  await expect.poll(async () => (await itemIn(page, 'fan.cpu')).sensor).toBe(boardFan);
  expect((await itemIn(page, 'fan.cpu')).warnBy).toEqual(warnByBefore);
});

// A207: the chart's knobs by keys, box and pointer, a push past the other line, a delete, and the chart once readings stop.
test('TheChartsKnobsWorkByPointerAndKeys', async ({ page }) => {
  const state = fakeState();
  const temperature = state.settings.overlays[0]?.items.find((item) => item.id === 'cpu.temperature');
  if (temperature !== undefined) {
    // Not v0.1's 70/85, which a version 2 file trades for this version's defaults.
    temperature.threshold = { warning: 70, critical: 84 };
  }
  await openWindow(page, state);
  await select(page, text('MetricName.CpuTemperature'));
  const warning = page.getByRole('slider', { name: text('UiText.WarningFrom') });
  const critical = page.getByRole('slider', { name: text('UiText.CriticalFrom') });
  const lines = async (): Promise<unknown> => (await itemIn(page, 'cpu.temperature')).threshold;

  const keys: { keys: string[]; type?: string; expected: { warning?: number; critical?: number } }[] = [
    { keys: ['ArrowUp'], expected: { warning: 71, critical: 84 } },
    { keys: ['PageUp'], expected: { warning: 81, critical: 84 } },
    { keys: ['Shift+ArrowUp'], expected: { warning: 86, critical: 91 } },
    { keys: ['ArrowDown'], expected: { warning: 85, critical: 91 } },
    { keys: ['Home'], expected: { warning: 0, critical: 91 } },
    { keys: ['Enter'], type: '75', expected: { warning: 75, critical: 91 } },
    { keys: ['F2'], type: '60', expected: { warning: 75, critical: 91 } },
    { keys: ['Enter'], type: '150', expected: { warning: 75, critical: 91 } },
    { keys: ['Enter'], type: 'warm', expected: { warning: 75, critical: 91 } },
    { keys: ['8'], type: '0', expected: { warning: 80, critical: 91 } },
  ];
  for (const row of keys) {
    await test.step(`${row.keys.join(' ')} ${row.type ?? ''}`, async () => {
      await warning.focus();
      for (const key of row.keys) {
        await page.keyboard.press(key);
      }
      if (row.type !== undefined) {
        const box = page.getByRole('spinbutton', { name: text('UiText.WarningFrom') });
        await expect(box).toBeFocused();
        await page.keyboard.type(row.type);
        await page.keyboard.press(row.keys[0] === 'F2' ? 'Escape' : 'Enter');
      }
      await expect.poll(lines).toEqual(row.expected);
    });
  }

  await test.step('a drag is one step', async () => {
    const box = await warning.boundingBox();
    if (box === null) {
      throw new Error('The warning knob is not shown.');
    }
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2, box.y - 10, { steps: 5 });
    await page.mouse.move(box.x + box.width / 2, box.y - 20, { steps: 5 });
    await page.mouse.up();
    await expect.poll(async () => ((await itemIn(page, 'cpu.temperature')).threshold?.warning ?? 0) > 80).toBe(true);
    await page.keyboard.press('Control+Z');
    await expect.poll(lines).toEqual({ warning: 80, critical: 91 });
  });

  await test.step('a click in the plot moves the nearest line', async () => {
    const plot = page.getByRole('img', { name: text('UiText.ChartOf', text('MetricName.CpuTemperature')) });
    await plot.click({ position: { x: 40, y: 4 } });
    await expect.poll(async () => (await itemIn(page, 'cpu.temperature')).threshold?.critical).not.toBe(91);
    await page.keyboard.press('Control+Z');
  });

  await test.step('Delete turns the line off and leaves the focus on its Add', async () => {
    await critical.focus();
    await page.keyboard.press('Delete');
    await expect.poll(lines).toEqual({ warning: 80 });
    await expect(page.getByRole('button', { name: text('UiText.AddCriticalLine') })).toBeFocused();
  });

  await test.step('without readings the chart keeps its lines', async () => {
    await setFake(page, { service: 'stopped' });
    await expect(warning).toBeVisible();
  });
});

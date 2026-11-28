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
  selectedRow,
  settingsOf,
  setFake,
  test,
  text,
  type FakeState,
  type Item,
} from './fakeBridge';
import { scrollbarsShown } from './edge';

const itemIn = async (page: Page, id: string): Promise<Item> => itemOf(await settingsOf(page), id);

// The scrollbars show here as the window draws them.
test.use({ launchOptions: scrollbarsShown });

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
  // Each from the CPU temperature as it starts; a row behind More options opens it first. What the value is, Shows and
  // Smoothing, is never behind it (owner, 2026-11-24).
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
      act: (page) => chooseByKeys(page, fieldOf(page, 'combobox', 'UiText.StatField'), text('ItemStat.Max')),
      read: (item) => item.stat,
      expected: 'max',
    },
    {
      name: 'Smoothing',
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
    // Night QA: the dialog starts with what the item reads checked, and once used, the Sensor box says the combination.
    { name: 'Combine', more: true, act: async (page) => {
      await fieldOf(page, 'button', 'UiText.Combine').press('Enter');
      const dialog = page.getByRole('dialog');
      const reading = dialog.getByRole('checkbox', { checked: true });
      await expect(reading).toHaveCount(1);
      await reading.press('Space');
      await dialog.getByRole('checkbox', { name: ccd1.name }).press('Space');
      await dialog.getByRole('checkbox', { name: ccd2.name }).press('Space');
      await chooseByKeys(page, dialog.getByRole('combobox', { name: text('UiText.CombineField') }), text('Combine.Max'));
      await dialog.getByRole('button', { name: text('UiText.CombineUse') }).press('Enter');
      await expect(fieldOf(page, 'combobox', 'UiText.SensorField')).not.toHaveValue('');
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

  // A322: the owner's list said "RAM used 31.8 GB" while his overlay showed its average, 40.1 GB, and the RAM's Label box
  // offered "GPU", its bar's first label: the list says the value C# gives with what it is, and the box the label drawn for it.
  test('TheListAndTheLabelBoxSayWhatTheOverlayDraws', async ({ page }) => {
    const state = fakeState();
    const [overlay] = state.settings.overlays;
    if (overlay === undefined) {
      throw new Error('The fake has no overlay.');
    }
    overlay.style = { ...overlay.style, layout: 'one' };
    await openWindow(page, state);
    await select(page, text('MetricName.RamUsed'));
    await chooseByKeys(page, fieldOf(page, 'combobox', 'UiText.StatField'), text('ItemStat.Average'));
    await expect.poll(async () => (await itemIn(page, 'ram.used')).stat).toBe('average');
    const shown = await page.evaluate(async () => (await window.thermi?.view())?.items['ram.used'] ?? null);
    if (shown?.value == null || shown.tag === undefined || shown.label === undefined) {
      throw new Error('The view says no value, tag or label for the RAM.');
    }
    await expect(selectedRow(page)).toHaveAccessibleDescription(`${shown.value} ${shown.tag}`);
    await expect(fieldOf(page, 'textbox', 'UiText.LabelField')).toHaveAttribute('placeholder', shown.label);
  });

  // Cold review 4 (Q12): a pill's first item already starts one, so it offers no Separate pill that could not change;
  // nor does any item in one bar, which ignores it (night QA).
  test("A pill's first item offers no Separate pill", async ({ page }) => {
    await openWindow(page);
    await select(page, text('MetricName.CpuTemperature'));
    await expect(fieldOf(page, 'switch', 'UiText.OwnPill')).toHaveCount(0);
    await select(page, text('MetricName.CpuPower'));
    await expect(fieldOf(page, 'switch', 'UiText.OwnPill')).toHaveCount(1);

    const { settings } = fakeState();
    const first = settings.overlays[0];
    if (first !== undefined) {
      first.style = { ...first.style, layout: 'one' };
    }
    await setFake(page, { settings });
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
    const chart = page.getByRole('slider', { name: text('UiText.ChartOf', text('MetricName.CpuTemperature')) });
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
    // Scrolled so the rows from the one given show under the pane's top, each control in view, as a person would see it,
    // or with the one given just above the pane's foot, where passing news floats. Gives where that control then shows.
    type Anchor = { role: 'button' | 'combobox' | 'switch'; key: string; foot?: boolean };
    const shownAt = async ({ role, key }: Anchor): Promise<number> => (await fieldOf(page, role, key).boundingBox())?.y ?? 0;
    const scrolledTo = async (anchor: Anchor): Promise<number> => {
      const at = await main.evaluate(
        (pane, [top, foot]) => {
          const view = pane.getBoundingClientRect();
          const to = foot ? top - view.top - pane.clientHeight + 60 : top - view.top - 16;
          pane.scrollTop = Math.min(pane.scrollHeight - pane.clientHeight, pane.scrollTop + to);
          return pane.scrollTop;
        },
        [await shownAt(anchor), anchor.foot === true] as const,
      );
      expect(at).toBeGreaterThan(0);
      return shownAt(anchor);
    };
    const pick = async (key: string, option: string): Promise<void> => {
      await fieldOf(page, 'combobox', key).click();
      await page.getByRole('option', { name: option, exact: true }).click();
    };
    type Act = { name: string; act: () => Promise<void>; read?: (item: Item) => unknown; expected?: unknown };
    // What the value is sits under Sensor, the rest in More options, so each group is done scrolled to its own rows. What
    // the person looks at, the control given, stays where it shows, though rows above it come or go.
    const groups: { from: Anchor; acts: Act[] }[] = [
      {
        from: { role: 'combobox', key: 'UiText.StatField' },
        acts: [
          { name: 'Value now', act: () => pick('UiText.StatField', text('ItemStat.Now')), read: (item) => item.stat, expected: undefined },
          { name: 'Smoothing', act: () => pick('UiText.SmoothField', text('UiText.SmoothSeconds', 3)), read: (item) => item.smooth, expected: 3 },
        ],
      },
      {
        from: { role: 'button', key: 'UiText.MoreOptions' },
        acts: [
          {
            name: 'Decimals',
            act: () => chooseByKeys(page, fieldOf(page, 'combobox', 'UiText.DecimalsField'), '2'),
            read: (item) => item.decimals,
            expected: 2,
          },
          { name: 'a switch', act: () => fieldOf(page, 'switch', 'UiText.AlertOnly').click(), read: (item) => item.alertOnly, expected: true },
          { name: 'undo', act: () => page.keyboard.press('Control+Z'), read: (item) => item.alertOnly, expected: undefined },
          { name: 'redo', act: () => page.keyboard.press('Control+Y'), read: (item) => item.alertOnly, expected: true },
          { name: 'new readings', act: () => setFake(page, { sensors: recording('admin').map((sensor) => ({ ...sensor, value: (sensor.value ?? 0) + 1 })) }) },
        ],
      },
      // At the pane's foot, where a list opens past it, the Graph row above goes and comes back, and an undo's news floats.
      {
        from: { role: 'combobox', key: 'UiText.WarnByField', foot: true },
        acts: [
          { name: 'Warn by Off', act: () => pick('UiText.WarnByField', text('UiText.WarnByOff')), read: (item) => item.warnBy, expected: undefined },
          { name: 'its undo', act: () => page.keyboard.press('Control+Z'), read: (item) => item.warnBy, expected: { metric: 'ram.load' } },
        ],
      },
      {
        from: { role: 'switch', key: 'UiText.AlertOnly', foot: true },
        acts: [
          {
            name: 'a switch at the foot',
            act: () => fieldOf(page, 'switch', 'UiText.AlertOnly').press('Space'),
            read: (item) => item.alertOnly,
            expected: undefined,
          },
          { name: 'its undo', act: () => page.keyboard.press('Control+Z'), read: (item) => item.alertOnly, expected: true },
        ],
      },
    ];
    for (const { from, acts } of groups) {
      // A person reaches a control at the foot once the news there is gone.
      if (from.foot === true) {
        await expect(page.getByRole('status').filter({ hasText: /./ })).toHaveCount(0, { timeout: 15_000 });
      }
      const at = await scrolledTo(from);
      for (const row of acts) {
        await test.step(row.name, async () => {
          await row.act();
          if (row.read !== undefined) {
            await expect.poll(async () => row.read?.(await itemIn(page, 'ram.used'))).toEqual(row.expected);
          }
          await fakeOf(page);
          expect(await shownAt(from)).toBe(at);
        });
      }
    }

    // Cold review 4 (Q8): the next item's page opens at its top, its title in view, with its options closed.
    await main.evaluate((pane) => pane.scrollTo({ top: pane.scrollHeight }));
    await select(page, text('MetricName.CpuLoad'));
    await expect(main.getByRole('heading', { level: 2 })).toBeInViewport();
    await expect(main.getByRole('button', { name: text('UiText.MoreOptions') })).toHaveAttribute('aria-expanded', 'false');
  });
});

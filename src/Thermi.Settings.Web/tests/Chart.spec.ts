import { type Page } from '@playwright/test';
import { expect, fakeState, itemOf, openWindow, recording, select, settingsOf, setFake, test, text, type Item } from './fakeBridge';

const itemIn = async (page: Page, id: string): Promise<Item> => itemOf(await settingsOf(page), id);

// Edge draws its scrollbars here as the window does, where headless Edge would hide them.
test.use({ launchOptions: { ignoreDefaultArgs: ['--hide-scrollbars'] } });

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
    const now = async (): Promise<number> => (await itemIn(page, 'cpu.temperature')).threshold?.warning ?? 0;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    // Whole moves, each landed before the next: Edge merges moves that come faster than it draws, as on a busy PC, and the
    // page then sends fewer edits than the oracle recorded.
    for (const above of [10, 20]) {
      const before = await now();
      await page.mouse.move(box.x + box.width / 2, box.y - above);
      await expect.poll(now).toBeGreaterThan(before);
    }
    await page.mouse.up();
    await expect.poll(now).toBeGreaterThan(80);
    await page.keyboard.press('Control+Z');
    await expect.poll(lines).toEqual({ warning: 80, critical: 91 });
  });

  await test.step('a click in the plot moves the nearest line', async () => {
    const plot = page.getByRole('slider', { name: text('UiText.ChartOf', text('MetricName.CpuTemperature')) });
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

// A207 on a fan, whose track ends far past its speed: a line added comes above the speed, the keys move it a step, and
// a drag keeps the knob under the pointer while the scale holds, selecting nothing on the way.
test('AFansLineStaysOnItsScale', async ({ page }) => {
  await openWindow(page);
  await select(page, text('MetricName.CpuFan'));
  const speed = recording('admin').find((sensor) => sensor.id === '/lpc/it8689e/0/fan/0')?.value ?? 0;
  const warning = page.getByRole('slider', { name: text('UiText.WarningFrom') });
  const line = async (): Promise<number> => itemOf(await settingsOf(page), 'fan.cpu').threshold?.warning ?? 0;
  const middle = async (): Promise<{ x: number; y: number }> => {
    const box = await warning.boundingBox();
    if (box === null) {
      throw new Error('The warning knob is not shown.');
    }
    return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
  };

  await page.getByRole('button', { name: text('UiText.AddWarningLine') }).press('Enter');
  await expect.poll(line).toBeGreaterThan(speed);
  const added = await line();
  expect(added).toBeLessThan(speed * 1.5);

  await warning.press('ArrowUp');
  await expect.poll(line).toBe(added + 50);
  await warning.press('ArrowDown');
  await expect.poll(line).toBe(added);

  const from = await middle();
  await page.mouse.move(from.x, from.y);
  await page.mouse.down();
  for (const by of [-12, -24, -36]) {
    await page.mouse.move(from.x, from.y + by, { steps: 4 });
    await expect.poll(async () => Math.abs((await middle()).y - (from.y + by))).toBeLessThan(9);
  }
  await page.mouse.up();
  expect(await line()).toBeGreaterThan(added);

  // A press under the knob, in its column, and a drag from it across the chart.
  await page.mouse.move(from.x, from.y + 80);
  await page.mouse.down();
  await page.mouse.move(from.x - 200, from.y + 120, { steps: 4 });
  await page.mouse.up();
  expect(await page.evaluate(() => document.getSelection()?.toString() ?? '')).toBe('');
});

// A352: a point of the chart shows the bridge's text of the second it is on, under the pointer within a second of where it
// aims, a second being under a pixel wide, and by keys exactly; a screen reader hears the point the keys move to, as the
// slider's value, and never the pointer's.
test('AChartsPointSaysItsReadingAndWhen', async ({ page }) => {
  await openWindow(page);
  await select(page, text('MetricName.CpuTemperature'));
  const chart = await page.evaluate(async () => window.thermi?.chart('cpu.temperature'));
  const [points, stats] = [chart?.points ?? [], chart?.stats ?? ''];
  const last = points.length - 1;
  const plot = page.getByRole('slider', { name: text('UiText.ChartOf', text('MetricName.CpuTemperature')) });
  const shown = page.getByRole('tooltip');
  const near = (ago: number): string[] => [ago - 1, ago, ago + 1].map((each) => points[last - each] ?? '');
  const aimAt = async (ago: number): Promise<void> => {
    const box = await plot.boundingBox();
    if (box === null) {
      throw new Error('The chart is not shown.');
    }
    await page.mouse.move(box.x + ((last - ago) / last) * box.width, box.y + box.height / 2);
  };

  // 200 s ago is in the fake's gap, and 40 s ago is not.
  for (const ago of [200, 40]) {
    await aimAt(ago);
    await expect.poll(async () => near(ago).includes((await shown.textContent()) ?? '')).toBe(true);
    await expect(plot).toHaveAttribute('aria-valuetext', text('UiText.ChartNow'));
  }
  const under = (await page.getByText(stats).boundingBox())?.y ?? 0;
  const label = await shown.boundingBox();
  expect((label?.y ?? under) + (label?.height ?? 0)).toBeLessThanOrEqual(under);

  await page.mouse.move(0, 0);
  await expect(shown).toHaveCount(0);
  await plot.focus();
  for (const [key, ago] of [['End', 0], ['ArrowLeft', 1], ['Home', last]] as const) {
    await page.keyboard.press(key);
    await expect(shown).toHaveText(points[last - ago] ?? '');
    await expect(plot).toHaveAttribute('aria-valuetext', points[last - ago] ?? '');
  }
  await page.keyboard.press('Escape');
  await expect(shown).toHaveCount(0);
});

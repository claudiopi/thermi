import { type Page } from '@playwright/test';
import { expect, fakeState, itemOf, openWindow, select, settingsOf, setFake, test, text, type Item } from './fakeBridge';

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

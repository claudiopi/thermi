import { expect, fakeOf, fakeState, hostile, list, openWindow, overlayOf, releaseReplies, select, setFake, startingWith, test, text } from './fakeBridge';

// What real hardware does, and a bridge that answers late and out of order: on each the page keeps working, and throws
// nothing. Working is what the real overlay is told: the item a click selects.
test.describe('ThePageHoldsUpToRealHardware', () => {
  for (const row of hostile) {
    test(row.name, async ({ page }) => {
      const errors: Error[] = [];
      page.on('pageerror', (error) => errors.push(error));
      const start = row.start();
      await openWindow(page, start);
      if (row.then !== undefined) {
        await setFake(page, row.then(start));
      }

      await select(page, text('MetricName.CpuTemperature'));

      await expect.poll(async () => (await fakeOf(page)).editing?.selected).toBe('cpu.temperature');
      expect(errors).toEqual([]);
    });
  }

  // The answers read before a change arrive after those read for it: the page shows what the settings hold now.
  test('LateAnswersArriveOutOfOrder', async ({ page }) => {
    await openWindow(page, fakeState({ lateReplies: true }));
    const renamed = fakeState().settings;
    renamed.overlays[0] = { ...overlayOf(renamed, 0), name: 'Desk' };

    await setFake(page, { settings: renamed });
    await releaseReplies(page);

    await expect(list(page).getByRole('treeitem', { name: startingWith('Desk') })).toBeVisible();
    await expect(list(page).getByRole('treeitem', { name: startingWith('Taskbar') })).toHaveCount(0);
  });
});

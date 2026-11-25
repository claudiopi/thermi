import { expect, fakeOf, fakeState, itemOf, openWindow, select, setFake, settingsOf, test, text, type Item } from './fakeBridge';
import { type Page } from '@playwright/test';

const itemIn = async (page: Page, id: string): Promise<Item> => itemOf(await settingsOf(page), id);

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

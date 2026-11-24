import { expect, fakeOf, fakeState, itemOf, list, openWindow, recording, select, settingsOf, setFake, test, text, toast, windowWith, type FakeState } from './fakeBridge';

// A201: each state that keeps the window from working offers its fix, and the state moves on as the C# side answers.
test('TheWindowSaysWhatIsWrongAndOffersTheFix', async ({ context }) => {
  const cpuReadsZero = (state: FakeState): boolean => state.sensors.some((sensor) => sensor.hardwareType === 'Cpu' && sensor.type === 'Temperature' && sensor.value === 0);
  const rows: { name: string; state: Partial<FakeState>; fixes: { fix: string; answer?: Partial<FakeState>; then: (state: FakeState) => boolean }[] }[] = [
    { name: 'no overlay', state: { overlayRunning: false }, fixes: [{ fix: 'UiText.StartOverlay', then: (state) => state.overlayRunning }] },
    { name: 'the service stopped', state: { service: 'stopped' }, fixes: [{ fix: 'WindowText.StartIt', then: (state) => state.service === 'running' }] },
    { name: 'the service refused, then started', state: { service: 'stopped', serviceAnswer: 'refused' }, fixes: [
      { fix: 'WindowText.StartIt', then: (state) => state.service === 'stopped' },
      { fix: 'WindowText.StartIt', answer: { serviceAnswer: 'accepted' }, then: (state) => state.service === 'running' },
    ] },
    { name: 'a broken file', state: { problem: 'broken' }, fixes: [{ fix: 'UiText.RestoreSettings', then: (state) => state.problem === null }] },
    { name: 'a CPU sensor needing the driver', state: { sensors: recording('no-admin') }, fixes: [{ fix: 'WindowText.InstallDriver', then: (state) => !cpuReadsZero(state) }] },
    { name: 'the driver refused, then installed', state: { sensors: recording('no-admin'), driverAnswer: 'refused' }, fixes: [
      { fix: 'WindowText.InstallDriver', then: cpuReadsZero },
      { fix: 'WindowText.TryAgain', answer: { driverAnswer: 'accepted' }, then: (state) => !cpuReadsZero(state) },
    ] },
    { name: 'the driver failed, then installed', state: { sensors: recording('no-admin'), driverAnswer: 'failed' }, fixes: [
      { fix: 'WindowText.InstallDriver', then: cpuReadsZero },
      { fix: 'WindowText.TryAgain', answer: { driverAnswer: 'accepted' }, then: (state) => !cpuReadsZero(state) },
    ] },
  ];

  for (const row of rows) {
    await test.step(row.name, async () => {
      const page = await windowWith(context, fakeState(row.state));
      const before = await settingsOf(page);
      if (row.state.problem !== undefined) {
        await page.keyboard.press('Control+N');
        await page.keyboard.press('Control+Z');
        expect(await settingsOf(page)).toEqual(before);
      }
      for (const step of row.fixes) {
        if (step.answer !== undefined) {
          await setFake(page, step.answer);
        }
        await page.getByRole('button', { name: text(step.fix) }).click();
        await expect.poll(async () => step.then(await fakeOf(page))).toBe(true);
      }
      await page.close();
    });
  }
});

// A202: undo and redo from the keys, also with a slider focused, and from the title row; the toast moves nothing and goes.
test('UndoAndRedoWorkFromTheKeysAndTheTitleRow', async ({ page }) => {
  await page.clock.install();
  await openWindow(page);
  const tray = async (): Promise<boolean> => itemOf(await settingsOf(page), 'cpu.temperature').tray ?? false;
  const undo = page.getByRole('button', { name: text('UiText.Undo') });
  const redo = page.getByRole('button', { name: text('UiText.Redo') });

  await select(page, text('MetricName.CpuTemperature'));
  await page.getByRole('switch', { name: text('UiText.OwnTrayIcon') }).press('Space');
  await expect.poll(tray).toBe(true);

  await page.keyboard.press('Control+Z');
  await expect.poll(tray).toBe(false);
  await page.keyboard.press('Control+Y');
  await expect.poll(tray).toBe(true);

  const knob = page.getByRole('slider', { name: text('UiText.CriticalFrom') });
  await knob.focus();
  await page.keyboard.press('Control+Z');
  await expect.poll(tray).toBe(false);
  expect(itemOf(await settingsOf(page), 'cpu.temperature').threshold).toEqual({ warning: 90, critical: 95 });
  await expect(knob).toBeFocused();

  await select(page, text('UiText.SettingsPage'));
  await redo.click();
  await expect.poll(tray).toBe(true);
  await expect(page.getByRole('heading', { name: text('MetricName.CpuTemperature') })).toBeVisible();

  const listBox = await list(page).boundingBox();
  await undo.click();
  await expect.poll(tray).toBe(false);
  await expect(toast(page)).toBeVisible();
  expect(await list(page).boundingBox()).toEqual(listBox);

  await expect(undo).toBeDisabled();
  await undo.focus();
  await expect(undo).toBeFocused();

  await page.clock.fastForward(6_000);
  await expect(toast(page)).toBeHidden();
});

// A265: minimized, the window draws no readings, and once it shows again it draws the latest.
test('AHiddenWindowShowsTheLatestReadingsOnceItShowsAgain', async ({ page }) => {
  await openWindow(page);
  const row = list(page).getByRole('treeitem', { name: text('MetricName.CpuTemperature') });
  const before = await row.getAttribute('aria-description');
  const hidden = async (hide: boolean): Promise<void> =>
    page.evaluate((hide) => {
      Object.defineProperty(document, 'hidden', { configurable: true, get: () => hide });
      document.dispatchEvent(new Event('visibilitychange'));
    }, hide);

  await hidden(true);
  await setFake(page, { sensors: recording('admin').map((sensor) => ({ ...sensor, value: sensor.value === null ? null : sensor.value + 3 })) });
  await page.waitForTimeout(300);
  await expect(row).toHaveAttribute('aria-description', before ?? '');

  await hidden(false);
  await expect(row).not.toHaveAttribute('aria-description', before ?? '');
});

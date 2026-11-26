import { chooseByKeys, expect, fakeOf, fakeState, list, openWindow, setFake, settingsOf, startingWith, test, text, texts } from './fakeBridge';

// A211: the Settings page changes thermi's own fields; Start with Windows stays outside undo, hotkeys are recorded, refused,
// cleared and cancelled while thermi's rest, and Reset asks with Cancel as its default.
test('TheSettingsPageChangesThermi', async ({ page }) => {
  await openWindow(page, fakeState({ heldHotkeys: ['Ctrl+Alt+J'], failingHotkeys: ['Ctrl+Alt+K'] }));
  await list(page).waitFor();
  await page.keyboard.press('Control+Comma');
  const pane = page.getByRole('main');
  const hotkey = pane.getByRole('button', { name: text('WindowText.HotkeyToggle'), exact: true });
  const open = (key: string): Promise<void> => pane.getByRole('button', { name: startingWith(text(key)), expanded: false }).click();
  const toggle = async (): Promise<string | undefined> => (await settingsOf(page)).hotkeys?.toggle;

  await test.step('language', async () => {
    await chooseByKeys(page, pane.getByRole('combobox', { name: text('UiText.LanguageField') }), texts('es')['UiText.LanguageName'] ?? '');
    await expect.poll(async () => (await settingsOf(page)).language).toBe('es');
    await chooseByKeys(
      page,
      pane.getByRole('combobox', { name: text('UiText.LanguageField') }),
      text('UiText.LanguageFromWindows', text('UiText.LanguageName')),
    );
    await expect.poll(async () => (await settingsOf(page)).language).toBeUndefined();
  });

  await test.step('the unit, and Start with Windows outside undo', async () => {
    await pane.getByRole('radio', { name: /°F/ }).press('Space');
    await expect.poll(async () => (await settingsOf(page)).temperature).toBe('f');
    await pane.getByRole('switch', { name: text('UiText.StartWithWindows') }).press('Space');
    await expect.poll(async () => (await fakeOf(page)).startWithWindows).toBe(true);
    await page.keyboard.press('Control+Z');
    await expect.poll(async () => (await settingsOf(page)).temperature).toBeUndefined();
    expect((await fakeOf(page)).startWithWindows).toBe(true);
  });

  await test.step('the window\'s theme', async () => {
    await pane.getByRole('radio', { name: text('WindowTheme.Dark') }).press('Space');
    await expect.poll(async () => (await settingsOf(page)).windowTheme).toBe('dark');
    await page.keyboard.press('Control+Z');
    await expect.poll(async () => (await settingsOf(page)).windowTheme).toBeUndefined();
  });

  const presses: { name: string; keys: string[]; expected: string | undefined }[] = [
    { name: 'recorded', keys: ['Control+Alt+H'], expected: 'Ctrl+Alt+H' },
    { name: 'refused as a common shortcut', keys: ['Control+C'], expected: 'Ctrl+Alt+H' },
    { name: 'refused as another app\'s', keys: ['Control+Alt+J'], expected: 'Ctrl+Alt+H' },
    { name: 'recorded when Windows cannot tell whether another app holds it', keys: ['Control+Alt+K'], expected: 'Ctrl+Alt+K' },
    { name: 'cancelled', keys: ['Escape'], expected: 'Ctrl+Alt+K' },
    { name: 'cleared', keys: ['Backspace'], expected: undefined },
  ];
  await open('WindowText.HotkeysField');
  for (const press of presses) {
    await test.step(`a hotkey ${press.name}`, async () => {
      await hotkey.press('Enter');
      await expect.poll(async () => (await fakeOf(page)).hotkeysResting).toBe(true);
      for (const key of press.keys) {
        await page.keyboard.press(key);
      }
      await expect.poll(toggle).toBe(press.expected);
      await expect.poll(async () => (await fakeOf(page)).hotkeysResting).toBe(false);
      await expect(hotkey).toBeFocused();
    });
  }

  await test.step('Reset asked and cancelled, then done', async () => {
    const before = await settingsOf(page);
    await open('WindowText.FileAndReset');
    await pane.getByRole('button', { name: text('UiText.ResetToDefaults') }).press('Enter');
    const dialog = page.getByRole('alertdialog');
    await expect(dialog.getByRole('button', { name: text('UiText.Cancel') })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(dialog).toBeHidden();
    expect(await settingsOf(page)).toEqual(before);

    await pane.getByRole('button', { name: text('UiText.ResetToDefaults') }).press('Enter');
    await dialog.getByRole('button', { name: text('WindowText.Reset'), exact: true }).press('Enter');
    await expect.poll(async () => (await settingsOf(page)).overlays.length).toBe(1);
  });
});

// A29: AI agents says how to connect an agent, each setup copied as C# wrote it; its switch is an edit undo takes back;
// and the last changes agents made show, with a link to their log, only once there are some.
test('TheAgentsSectionConnectsAndShowsWhatAgentsDid', async ({ page }) => {
  const start = fakeState();
  await openWindow(page, { ...start, agents: { ...start.agents, changes: [] } });
  await list(page).waitFor();
  await page.keyboard.press('Control+Comma');
  const pane = page.getByRole('main');
  const log = pane.getByRole('button', { name: text('AgentText.OpenLog') });

  await test.step('each setup copied', async () => {
    for (const [row, copied] of [
      ['AgentText.ClaudeCode', start.agents.command],
      ['AgentText.ByFile', start.agents.file],
    ] as const) {
      await pane.getByRole('button', { name: text('AgentText.CopyOf', text(row)) }).press('Enter');
      await expect.poll(async () => (await fakeOf(page)).clipboard).toBe(copied);
    }
  });

  await test.step('the switch, and undo', async () => {
    const letThem = pane.getByRole('switch', { name: text('AgentText.LetThemChange') });
    await expect(letThem).toBeChecked();
    await letThem.press('Space');
    await expect.poll(async () => (await settingsOf(page)).agents).toBe('read');
    await page.keyboard.press('Control+Z');
    await expect.poll(async () => (await settingsOf(page)).agents).toBeUndefined();
  });

  await test.step('the last changes, once an agent made some', async () => {
    await expect(log).toHaveCount(0);
    await setFake(page, { agents: start.agents });
    await expect(log).toBeVisible();
    await expect(pane.getByRole('listitem')).toHaveCount(start.agents.changes.length);
  });
});

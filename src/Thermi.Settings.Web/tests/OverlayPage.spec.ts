import { type Locator, type Page } from '@playwright/test';
import { chooseByKeys, expect, fakeOf, fakeState, openWindow, overlayOf, secondMonitor, select, test, text, windowWith, type FakeState } from './fakeBridge';

const pressOn = async (control: Locator, ...keys: string[]): Promise<void> => {
  await control.focus();
  for (const key of keys) {
    await control.press(key);
  }
};

const nameAndEnter = async (page: Page, name: string): Promise<void> => {
  await page.getByRole('dialog').getByRole('textbox').fill(name);
  await page.keyboard.press('Enter');
};

// A208: each control of Appearance, changed by keys, lands in the settings or in the saved looks.
test('TheAppearancePageChangesTheLook', async ({ page }) => {
  await openWindow(page);
  await select(page, 'Taskbar');
  const pane = page.getByRole('main');
  const control = (role: 'radio' | 'slider' | 'switch' | 'combobox' | 'button', key: string, ...blanks: (string | number)[]): Locator =>
    pane.getByRole(role, { name: text(key, ...blanks) });
  const lookMenu = async (look: string, entry: string): Promise<void> => {
    await pressOn(pane.getByRole('button', { name: text('OverlayPageText.LookActions', look) }), 'Enter');
    await page.getByRole('menuitem', { name: text(entry) }).press('Enter');
  };
  const rows: { name: string; act: () => Promise<void>; read: (state: FakeState) => unknown; expected: unknown }[] = [
    {
      name: 'a Look tile',
      act: () => pressOn(control('radio', 'OverlayPageText.LookTile', 'Night'), 'Space'),
      read: (state) => overlayOf(state.settings, 0).style?.name,
      expected: 'Night',
    },
    {
      name: 'Size',
      act: () => pressOn(control('slider', 'UiText.SizeField'), 'ArrowRight'),
      read: (state) => overlayOf(state.settings, 0).size,
      expected: 1.05,
    },
    {
      name: 'Theme',
      act: () => pressOn(control('radio', 'PillTheme.Light'), 'Space'),
      read: (state) => overlayOf(state.settings, 0).style?.pills,
      expected: 'light',
    },
    {
      name: 'Use on all overlays',
      act: () => pressOn(control('button', 'OverlayPageText.UseOnAllOverlays'), 'Enter'),
      read: (state) => overlayOf(state.settings, 1).style?.pills,
      expected: 'light',
    },
    {
      name: 'Layout',
      act: () => chooseByKeys(page, control('combobox', 'UiText.LayoutField'), text('PillLayout.Each')),
      read: (state) => overlayOf(state.settings, 0).style?.layout,
      expected: 'each',
    },
    {
      name: 'Direction',
      act: () => pressOn(control('radio', 'Orientation.Vertical'), 'Space'),
      read: (state) => overlayOf(state.settings, 0).style?.orientation,
      expected: 'vertical',
    },
    {
      name: 'Background opacity',
      act: () => pressOn(control('slider', 'UiText.BackgroundField'), 'Home'),
      read: (state) => overlayOf(state.settings, 0).style?.opacity?.background,
      expected: 0,
    },
    { name: 'Text size', act: async () => {
      await pressOn(control('button', 'OverlayPageText.TextAndShape'), 'Enter');
      await chooseByKeys(page, control('combobox', 'UiText.LabelsField'), '14 px');
    }, read: (state) => [overlayOf(state.settings, 0).style?.preset, overlayOf(state.settings, 0).style?.label?.size], expected: ['custom', 14] },
    {
      name: 'Font',
      act: () => chooseByKeys(page, control('combobox', 'OverlayPageText.FontField'), text('StyleFont.Bahnschrift')),
      read: (state) => overlayOf(state.settings, 0).style?.font,
      expected: 'bahnschrift',
    },
    {
      name: 'Corners',
      act: () => pressOn(control('radio', 'CornerStyle.Soft'), 'Space'),
      read: (state) => overlayOf(state.settings, 0).style?.corners,
      expected: 'soft',
    },
    {
      name: 'Gap',
      act: () => pressOn(control('slider', 'OverlayPageText.GapField'), 'ArrowLeft'),
      read: (state) => overlayOf(state.settings, 0).style?.gap,
      expected: 7,
    },
    {
      name: 'Text effect',
      act: () => chooseByKeys(page, control('combobox', 'OverlayPageText.EffectField'), text('TextEffect.Outline')),
      read: (state) => overlayOf(state.settings, 0).style?.effect,
      expected: 'outline',
    },
    {
      name: 'Show units',
      act: () => pressOn(control('switch', 'UiText.UnitsField'), 'Space'),
      read: (state) => overlayOf(state.settings, 0).style?.units,
      expected: false,
    },
    {
      name: 'Show labels',
      act: () => pressOn(control('switch', 'OverlayPageText.ShowLabels'), 'Space'),
      read: (state) => overlayOf(state.settings, 0).style?.noLabels,
      expected: true,
    },
    { name: 'a color', act: async () => {
      await pressOn(control('button', 'OverlayPageText.ColorsField'), 'Enter');
      await pressOn(control('button', 'StyleColor.Warning'), 'Enter');
      await page.getByRole('textbox', { name: text('UiText.HexColor') }).fill('#FFC000');
      await page.keyboard.press('Enter');
    }, read: (state) => overlayOf(state.settings, 0).style?.colors?.warning, expected: '#FFC000' },
    {
      name: 'Colorblind-safe',
      act: () => pressOn(control('switch', 'OverlayPageText.ColorblindSafe'), 'Space'),
      read: (state) => overlayOf(state.settings, 0).style?.colors?.normal,
      expected: '#0072B2',
    },
    { name: 'Save as my look', act: async () => {
      await pressOn(control('button', 'OverlayPageText.SaveAsMyLook'), 'Enter');
      await nameAndEnter(page, 'Mine');
    }, read: (state) => Object.keys(state.looks).includes('Mine'), expected: true },
    { name: 'a look\'s Rename', act: async () => {
      await lookMenu('Mine', 'UiText.RenameStyle');
      await nameAndEnter(page, 'Evening');
    }, read: (state) => Object.keys(state.looks).sort(), expected: ['Evening', 'Night'] },
    {
      name: 'a look\'s Use on all overlays',
      act: () => lookMenu('Night', 'OverlayPageText.UseOnAllOverlays'),
      read: (state) => state.settings.overlays.map((overlay) => overlay.style?.name),
      expected: ['Night', 'Night'],
    },
    {
      name: 'a look\'s Export',
      act: () => lookMenu('Night', 'UiText.ExportStyle'),
      read: (state) =>
        Object.keys((JSON.parse(state.exported ?? '{}') as { style?: object }).style ?? {}).some(
          (field) => field === 'layout' || field === 'orientation',
        ),
      expected: false,
    },
    { name: 'a look\'s Delete', act: () => lookMenu('Evening', 'UiText.DeleteStyle'), read: (state) => Object.keys(state.looks), expected: ['Night'] },
  ];

  for (const row of rows) {
    await test.step(row.name, async () => {
      await row.act();
      await expect.poll(async () => row.read(await fakeOf(page))).toEqual(row.expected);
    });
  }
});

// A209: each control of Placement, chosen by keys on two monitors, lands in the settings.
test('ThePlacementPageMovesTheOverlay', async ({ context }) => {
  const spot = (key: string, monitor?: string): { spot: string; monitor?: string } => {
    const name = key.replace('SnapSpot.', '').replace(/[A-Z]/g, (letter, index: number) => `${index === 0 ? '' : '-'}${letter.toLowerCase()}`);
    return monitor === undefined ? { spot: name } : { spot: name, monitor };
  };
  const mapSpot = (page: Page, key: string, monitor: number): Locator =>
    page.getByRole('radiogroup', { name: text('UiText.MonitorMap') }).getByRole('radio', { name: text('OverlayPageText.SpotOnMonitor', text(key), monitor) });
  const spots = [
    'SnapSpot.TaskbarLeft',
    'SnapSpot.TaskbarCenter',
    'SnapSpot.TaskbarRight',
    'SnapSpot.TopLeft',
    'SnapSpot.TopRight',
    'SnapSpot.BottomLeft',
    'SnapSpot.BottomRight',
  ];

  await test.step('each spot of the map, on each monitor', async () => {
    const page = await windowWith(context, fakeState());
    await select(page, 'Taskbar');
    for (const [monitor, id] of [[1, undefined], [2, secondMonitor]] as const) {
      for (const key of spots) {
        await pressOn(mapSpot(page, key, monitor), 'Space');
        await expect.poll(async () => overlayOf((await fakeOf(page)).settings, 0).snap).toEqual(spot(key, id));
      }
    }
    await page.close();
  });

  await test.step('a monitor that is gone lights its spot on the main one and keeps the file', async () => {
    const state = fakeState();
    state.settings.overlays[1] = { ...overlayOf(state.settings, 1), snap: { spot: 'top-right', monitor: 'GONE-0000-1' } };
    const page = await windowWith(context, state);
    await select(page, 'Top right');
    await expect(mapSpot(page, 'SnapSpot.TopRight', 1)).toBeChecked();
    expect(overlayOf((await fakeOf(page)).settings, 1).snap).toEqual({ spot: 'top-right', monitor: 'GONE-0000-1' });
    await page.close();
  });

  await test.step('vertical offers no taskbar spot', async () => {
    const state = fakeState();
    state.settings.overlays[0] = { ...overlayOf(state.settings, 0), style: { orientation: 'vertical' } };
    const page = await windowWith(context, state);
    await select(page, 'Taskbar');
    await expect(mapSpot(page, 'SnapSpot.TaskbarRight', 1)).toHaveCount(0);
    await page.close();
  });

  await test.step('the switches and When to show', async () => {
    const page = await windowWith(context, fakeState({ crowded: [0] }));
    await select(page, 'Taskbar');
    const pane = page.getByRole('main');
    await pressOn(pane.getByRole('button', { name: text('OverlayPageText.LockGroup') }), 'Enter');
    const rows: { act: () => Promise<void>; read: (state: FakeState) => unknown; expected: unknown }[] = [
      {
        act: () =>
          chooseByKeys(
            page,
            pane.getByRole('combobox', { name: text('OverlayPageText.WhenToShowField') }),
            text('Visibility.HideInFullscreen'),
          ),
        read: (state) => overlayOf(state.settings, 0).visibility,
        expected: 'hide-in-fullscreen',
      },
      {
        act: () => pressOn(pane.getByRole('switch', { name: text('UiText.LockPosition') }), 'Space'),
        read: (state) => overlayOf(state.settings, 0).locked,
        expected: true,
      },
      {
        act: () => pressOn(pane.getByRole('switch', { name: text('UiText.ClickThrough') }), 'Space'),
        read: (state) => overlayOf(state.settings, 0).clickThrough,
        expected: true,
      },
      {
        act: () => pressOn(pane.getByRole('switch', { name: text('OverlayPageText.OledCare') }), 'Space'),
        read: (state) => overlayOf(state.settings, 0).oled,
        expected: true,
      },
      {
        act: () => pressOn(pane.getByRole('switch', { name: text('OverlayPageText.Fade') }), 'Space'),
        read: (state) => overlayOf(state.settings, 0).fade,
        expected: true,
      },
      {
        act: () => pressOn(pane.getByRole('button', { name: text('FitText.HideLabels') }), 'Enter'),
        read: (state) => overlayOf(state.settings, 0).style?.noLabels,
        expected: true,
      },
    ];
    for (const row of rows) {
      await row.act();
      await expect.poll(async () => row.read(await fakeOf(page))).toEqual(row.expected);
    }
    // Without its labels the overlay fits, and the notice goes with its button.
    await expect(pane.getByRole('button', { name: text('FitText.HideLabels') })).toHaveCount(0);
    await expect(pane.getByRole('status')).toHaveCount(0);
    await page.close();
  });

  await test.step('an overlay too wide even without its labels keeps the notice, with no way out', async () => {
    const state = fakeState({ crowdedWithoutLabels: [0] });
    state.settings.overlays[0] = { ...overlayOf(state.settings, 0), style: { noLabels: true } };
    const page = await windowWith(context, state);
    await select(page, 'Taskbar');
    const pane = page.getByRole('main');
    await expect(pane.getByRole('status')).toBeVisible();
    await expect(pane.getByRole('button', { name: text('FitText.HideLabels') })).toHaveCount(0);
    await page.close();
  });
});

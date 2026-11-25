import { type Locator, type Page } from '@playwright/test';
import {
  chooseByKeys,
  expect,
  fakeOf,
  fakeState,
  openWindow,
  overlayOf,
  secondMonitor,
  select,
  test,
  text,
  windowWith,
  type FakeState,
} from './fakeBridge';

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
  const tile = (look: string): Locator => control('radio', 'OverlayPageText.LookTile', look);
  const checkedTiles = (): Locator => pane.getByRole('radiogroup', { name: text('UiText.LookField') }).getByRole('radio', { checked: true });
  let savedFrom = 0;
  const rows: { name: string; act: () => Promise<void>; read: (state: FakeState) => unknown; expected: unknown }[] = [
    {
      name: 'a Look tile',
      act: () => pressOn(control('radio', 'OverlayPageText.LookTile', 'Night'), 'Space'),
      read: (state) => overlayOf(state.settings, 0).style?.name,
      expected: 'Night',
    },
    // Night QA: Default takes the whole look back as one step, and is the tile checked only while the overlay wears it.
    {
      name: 'the Default tile',
      act: () => pressOn(tile(text('OverlayPageText.DefaultLook')), 'Space'),
      read: async (state) => [
        overlayOf(state.settings, 0).style?.name,
        overlayOf(state.settings, 0).style?.pills,
        await tile(text('OverlayPageText.DefaultLook')).isChecked(),
      ],
      expected: [undefined, undefined, true],
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
      read: async (state) => [overlayOf(state.settings, 0).style?.pills, await checkedTiles().count()],
      expected: ['light', 0],
    },
    {
      name: 'Use on all overlays',
      act: () => pressOn(control('button', 'OverlayPageText.UseOnAllOverlays'), 'Enter'),
      read: (state) => overlayOf(state.settings, 1).style?.pills,
      expected: 'light',
    },
    // Night QA: the look's "…" menu opens over the Position map, which drew through it; the menu takes the pointer there.
    {
      name: 'the look file menu over the map',
      act: async () => {
        await page.setViewportSize({ width: 1500, height: 900 });
        await pane.getByRole('button', { name: text('OverlayPageText.LookFileActions') }).click();
      },
      read: async () => {
        await page.getByRole('menuitem', { name: text('WindowText.CopyLookToAll') }).click({ trial: true, timeout: 1000 });
        await page.keyboard.press('Escape');
        return true;
      },
      expected: true,
    },
    {
      name: 'Layout',
      act: () => chooseByKeys(page, control('combobox', 'UiText.LayoutField'), text('PillLayout.Each')),
      read: (state) => overlayOf(state.settings, 0).style?.layout,
      expected: 'each',
    },
    {
      name: 'Graph',
      act: () => chooseByKeys(page, control('combobox', 'UiText.GraphField'), text('GraphStyle.Bars')),
      read: (state) => overlayOf(state.settings, 0).graph,
      expected: 'bars',
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
    { name: 'Reset colors', act: () => pressOn(control('button', 'UiText.ResetColors'), 'Enter'), read: async (state) => [
      overlayOf(state.settings, 0).style?.colors ?? {},
      await control('button', 'UiText.ResetColors').count(),
    ], expected: [{}, 0] },
    // Saved, the look is the one the overlay wears, and the page stays where it was.
    { name: 'Save as my look', act: async () => {
      await pressOn(control('button', 'OverlayPageText.SaveAsMyLook'), 'Enter');
      savedFrom = await pane.evaluate((main) => main.scrollTop);
      await nameAndEnter(page, 'Mine');
    }, read: async (state) => [
      Object.keys(state.looks).includes('Mine'),
      await tile('Mine').isChecked(),
      await pane.evaluate((main) => main.scrollTop) - savedFrom,
    ], expected: [true, true, 0] },
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

// A328: the preview at the top of Appearance shows an edit of the look as C# paints it, and its undo.
test('ThePreviewShowsEachEditOfTheOverlay', async ({ page }) => {
  await openWindow(page);
  await select(page, 'Taskbar');
  const pane = page.getByRole('main');
  const preview = pane.getByRole('img', { name: text('OverlayPageText.Preview', 'Taskbar') });
  // The picture the page shows, and the one C# paints of the overlay now: the page draws none of its own.
  const shown = async (): Promise<string | null> => preview.getAttribute('src');
  const painted = async (): Promise<string | null> => page.evaluate(async () => (await window.thermi?.preview(0, 0))?.png ?? null);
  await expect.poll(async () => (await shown()) === (await painted())).toBe(true);
  const before = await shown();

  await pressOn(pane.getByRole('radio', { name: text('PillTheme.Light') }), 'Space');
  await expect.poll(shown).not.toBe(before);
  await expect.poll(async () => (await shown()) === (await painted())).toBe(true);

  await page.keyboard.press('Control+Z');
  await expect.poll(shown).toBe(before);

  // QA round 5: the preview was the picture shrunk to the column, and blurred. It shows one of C#'s pixels on each of the
  // page's device pixels: as wide as the overlay where it fits, and where it does not, painted by C# as wide as it shows.
  const pixels = async (): Promise<{ shown: number; whole: number; fitted: number; within: boolean }> => {
    const box = (await preview.boundingBox()) ?? { x: 0, width: 0 };
    const main = (await pane.boundingBox()) ?? { x: 0, width: 0 };
    return page.evaluate(
      async ({ width, within }) => {
        const device = Math.round(width * window.devicePixelRatio);
        const whole = (await window.thermi?.preview(0, 0))?.width ?? 0;
        return { shown: device, whole, fitted: (await window.thermi?.preview(0, device))?.width ?? 0, within };
      },
      { width: box.width, within: box.x + box.width <= main.x + main.width },
    );
  };
  await test.step('as wide as the overlay where it fits', async () => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await expect.poll(async () => { const now = await pixels(); return now.shown === now.whole && now.within; }).toBe(true);
  });
  await test.step('painted to the column where it does not', async () => {
    await page.setViewportSize({ width: 760, height: 560 });
    await pressOn(pane.getByRole('slider', { name: text('UiText.SizeField') }), 'End');
    await expect.poll(async () => { const now = await pixels(); return now.shown < now.whole && now.shown === now.fitted && now.within; }).toBe(true);
  });
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
    'SnapSpot.Taskbar',
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
    await expect(mapSpot(page, 'SnapSpot.Taskbar', 1)).toHaveCount(0);
    await page.close();
  });

  await test.step('the switches and When to show', async () => {
    const page = await windowWith(context, fakeState());
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
      // Owner's QA: the default, which the page sends as null, can be chosen again.
      {
        act: () => chooseByKeys(page, pane.getByRole('combobox', { name: text('OverlayPageText.WhenToShowField') }), text('Visibility.Always')),
        read: (state) => overlayOf(state.settings, 0).visibility,
        expected: undefined,
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
    ];
    for (const row of rows) {
      await row.act();
      await expect.poll(async () => row.read(await fakeOf(page))).toEqual(row.expected);
    }
    await page.close();
  });
});

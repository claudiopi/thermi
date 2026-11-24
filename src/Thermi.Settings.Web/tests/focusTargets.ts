import type { Locator, Page } from '@playwright/test';
import { fakeState, itemOf, list, overlayOf, recording, selectedRow, startingWith, text, type DesktopPick, type FakeState, type Item } from './fakeBridge';

// The design prototype's focus contract, its 102 steps in its order, as data: what a person does, where the focus
// must land, and what the settings hold after. Every step also checks that the focus is on a control in view, never
// the page, and inside an open dialog. A prototype step that checked a text checks the settings instead.

/** A control as Narrator finds it, by its role and name. */
export type Target = (page: Page) => Locator;

/**
 * What a person does: a key as Playwright names it, or one it does not have by its code, typed text, a control focused,
 * clicked (low in it, or with the right button) or filled, the window resized, the C# side moving on, or a value or an
 * overlay's edge picked on a real overlay, a person acting while what closes still slides away, or a click at a point
 * of the window.
 */
export type Act =
  | string
  | { key: string }
  | { type: string | (() => string) }
  | { focus: Target }
  | { click: Target; low?: true; right?: true }
  | { fill: Target; value: string }
  | { size: { width: number; height: number } }
  | { fake: Partial<FakeState> }
  | { pick: DesktopPick }
  | { sliding: true }
  | { at: { x: number; y: number } };

export interface FocusStep {
  /** The prototype's step. */
  name: string;
  acts: Act[];
  /** The control that must have the focus after. */
  focus?: Target;
  /** A control that must not have it. */
  away?: Target;
  holds?: (fake: FakeState) => boolean;
  /** Windows' own frame does it, not the page, so a person checks it (A220). */
  frame?: true;
  /** The task that builds what the step needs; until it is done, the step is skipped. */
  waits?: string;
}

export interface FocusRun {
  name: string;
  start: () => FakeState;
  size: { width: number; height: number };
  colorScheme: 'light' | 'dark';
  /** With Windows' animation effects on, so what closes takes its time; off otherwise. */
  motion?: true;
  steps: FocusStep[];
}

export const wide = { width: 1440, height: 900 };
export const narrowest = { width: 760, height: 560 };

export const role =
  (kind: Parameters<Page['getByRole']>[0], key: string): Target =>
  (page) =>
    page.getByRole(kind, { name: text(key) });
export const button = (key: string): Target => role('button', key);
export const row =
  (name: () => string): Target =>
  (page) =>
    list(page)
      .getByRole('treeitem', { name: startingWith(name()) })
      .first();
export const metric = (key: string): Target => row(() => text(key));
export const overlay = (name: string): Target => row(() => name);
export const pill =
  (expanded?: boolean): Target =>
  (page) =>
    list(page)
      .getByRole('treeitem', { level: 2, ...(expanded === undefined ? {} : { expanded }) })
      .first();
export const focusIn =
  (target: Target): Target =>
  (page) =>
    target(page).locator(':focus');
export const selected: Target = (page) => selectedRow(page);
export const main: Target = (page) => page.getByRole('main');
export const menu: Target = (page) => page.getByRole('menu');
export const menuItem =
  (name: () => string): Target =>
  (page) =>
    page.getByRole('menuitem', { name: name() });
export const renameBox: Target = (page) => list(page).getByRole('textbox');
export const hotkey: Target = (page) => page.getByRole('button', { name: text('WindowText.HotkeyToggle'), exact: true });
export const clearHotkey: Target = (page) => page.getByRole('button', { name: text('WindowText.ClearHotkey', text('WindowText.HotkeyToggle')) });
/** Opens a closed section of the Settings page, as the prototype's test opens it before using what it holds. */
export const opened = (key: string): Act => ({ click: (page) => page.getByRole('button', { name: startingWith(text(key)), expanded: false }) });
export const theme: Target = (page) => role('radiogroup', 'OverlayPageText.ThemeField')(page).getByRole('radio', { checked: true });
export const knob = (key: 'UiText.WarningFrom' | 'UiText.CriticalFrom'): Target => role('slider', key);
export const box = (key: 'UiText.WarningFrom' | 'UiText.CriticalFrom'): Target => role('spinbutton', key);
export const plot: Target = (page) => page.getByRole('img', { name: text('UiText.ChartOf', text('MetricName.CpuTemperature')) });
export const catalogSearch: Target = (page) => page.getByRole('dialog', { name: text('UiText.Catalog') }).getByRole('searchbox');
export const catalogResults: Target = (page) => focusIn((on) => on.getByRole('dialog', { name: text('UiText.Catalog') }).getByRole('listbox'))(page);
export const sensorGroup =
  (key: string): Target =>
  (page) =>
    page.getByRole('group', { name: text('UiText.SensorField') }).getByRole('button', { name: text(key) });
export const toastUndo: Target = (page) => page.getByRole('status').getByRole('button', { name: text('UiText.Undo') });

/** A board temperature from the recording, found by typing its name in the catalog. */
export const boardTemperature = (): string =>
  recording('admin').find((sensor) => sensor.hardwareType === 'SuperIO' && sensor.type === 'Temperature')?.name ?? '';
export const items = (fake: FakeState): Item[] => fake.settings.overlays.flatMap((one) => one.items);
export const ids = (fake: FakeState, index: number): string =>
  overlayOf(fake.settings, index)
    .items.map((item) => item.id)
    .join();
export const threshold = (fake: FakeState) => itemOf(fake.settings, 'cpu.temperature').threshold ?? {};
export const warmer = (): Partial<FakeState> => ({
  sensors: recording('admin').map((sensor) => ({ ...sensor, value: sensor.value === null ? null : sensor.value + 1 })),
});
export const withHotkeys = (hotkeys: NonNullable<FakeState['settings']['hotkeys']>) => (): FakeState => {
  const state = fakeState();
  state.settings.hotkeys = hotkeys;
  return state;
};
export const boardFan = '/lpc/it8689e/0/fan/1';
export const renamedHot = (fake: FakeState): boolean => Object.values(fake.settings.sensorNames ?? {}).some((name) => name.endsWith(' hot'));
/** The top right overlay set free at 400, 300 and named Floating. */
export const withFloating = (change: Partial<FakeState> = {}): FakeState => {
  const state = fakeState(change);
  const { snap: _snap, ...free } = overlayOf(state.settings, 1);
  state.settings.overlays[1] = { ...free, name: 'Floating', position: { x: 400, y: 300 } };
  return state;
};
export const placed = (fake: FakeState, index: number): string => {
  const at = overlayOf(fake.settings, index).position;
  return at === undefined ? '' : `${at.x},${at.y}`;
};
export const refused = (combination: string): Act[] => [{ focus: hotkey }, 'Enter', combination];
export const noToggle = (fake: FakeState): boolean => fake.settings.hotkeys?.toggle === undefined;
export const fromTheList = (target: Target): Act[] => [{ focus: button('UiText.ShowList') }, 'Enter', { focus: target }, 'Enter'];

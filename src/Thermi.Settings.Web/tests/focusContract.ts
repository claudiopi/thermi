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

const wide = { width: 1440, height: 900 };
const narrowest = { width: 760, height: 560 };

const role = (kind: Parameters<Page['getByRole']>[0], key: string): Target => (page) => page.getByRole(kind, { name: text(key) });
const button = (key: string): Target => role('button', key);
const row = (name: () => string): Target => (page) => list(page).getByRole('treeitem', { name: startingWith(name()) }).first();
const metric = (key: string): Target => row(() => text(key));
const overlay = (name: string): Target => row(() => name);
const pill = (expanded?: boolean): Target => (page) => list(page).getByRole('treeitem', { level: 2, ...(expanded === undefined ? {} : { expanded }) }).first();
const focusIn = (target: Target): Target => (page) => target(page).locator(':focus');
const selected: Target = (page) => selectedRow(page);
const main: Target = (page) => page.getByRole('main');
const menu: Target = (page) => page.getByRole('menu');
const menuItem = (name: () => string): Target => (page) => page.getByRole('menuitem', { name: name() });
const renameBox: Target = (page) => list(page).getByRole('textbox');
const hotkey: Target = (page) => page.getByRole('button', { name: text('WindowText.HotkeyToggle'), exact: true });
const clearHotkey: Target = (page) => page.getByRole('button', { name: text('WindowText.ClearHotkey', text('WindowText.HotkeyToggle')) });
/** Opens a closed section of the Settings page, as the prototype's test opens it before using what it holds. */
const opened = (key: string): Act => ({ click: (page) => page.getByRole('button', { name: startingWith(text(key)), expanded: false }) });
const theme: Target = (page) => role('radiogroup', 'OverlayPageText.ThemeField')(page).getByRole('radio', { checked: true });
const knob = (key: 'UiText.WarningFrom' | 'UiText.CriticalFrom'): Target => role('slider', key);
const box = (key: 'UiText.WarningFrom' | 'UiText.CriticalFrom'): Target => role('spinbutton', key);
const plot: Target = (page) => page.getByRole('img', { name: text('UiText.ChartOf', text('MetricName.CpuTemperature')) });
const catalogSearch: Target = (page) => page.getByRole('dialog', { name: text('UiText.Catalog') }).getByRole('searchbox');
const catalogResults: Target = (page) => focusIn((on) => on.getByRole('dialog', { name: text('UiText.Catalog') }).getByRole('listbox'))(page);
const sensorGroup = (key: string): Target => (page) => page.getByRole('group', { name: text('UiText.SensorField') }).getByRole('button', { name: text(key) });
const toastUndo: Target = (page) => page.getByRole('status').getByRole('button', { name: text('UiText.Undo') });

/** A board temperature from the recording, found by typing its name in the catalog. */
const boardTemperature = (): string => recording('admin').find((sensor) => sensor.hardwareType === 'SuperIO' && sensor.type === 'Temperature')?.name ?? '';
const items = (fake: FakeState): Item[] => fake.settings.overlays.flatMap((one) => one.items);
const ids = (fake: FakeState, index: number): string => overlayOf(fake.settings, index).items.map((item) => item.id).join();
const threshold = (fake: FakeState) => itemOf(fake.settings, 'cpu.temperature').threshold ?? {};
const warmer = (): Partial<FakeState> => ({ sensors: recording('admin').map((sensor) => ({ ...sensor, value: sensor.value === null ? null : sensor.value + 1 })) });
const withHotkeys = (hotkeys: NonNullable<FakeState['settings']['hotkeys']>) => (): FakeState => {
  const state = fakeState();
  state.settings.hotkeys = hotkeys;
  return state;
};
const boardFan = '/lpc/it8689e/0/fan/1';
const renamedHot = (fake: FakeState): boolean => Object.values(fake.settings.sensorNames ?? {}).some((name) => name.endsWith(' hot'));
/** The top right overlay set free at 400, 300 and named Floating. */
const withFloating = (change: Partial<FakeState> = {}): FakeState => {
  const state = fakeState(change);
  const { snap: _snap, ...free } = overlayOf(state.settings, 1);
  state.settings.overlays[1] = { ...free, name: 'Floating', position: { x: 400, y: 300 } };
  return state;
};
const placed = (fake: FakeState, index: number): string => {
  const at = overlayOf(fake.settings, index).position;
  return at === undefined ? '' : `${at.x},${at.y}`;
};

const firstRun: FocusStep[] = [
  { name: 'Tab into the list', acts: [{ focus: metric('MetricName.CpuTemperature') }], focus: metric('MetricName.CpuTemperature') },
  { name: 'Down to the next row', acts: ['ArrowDown'], focus: metric('MetricName.CpuFan') },
  { name: 'Enter in a text field commits and stays', acts: [{ focus: role('textbox', 'UiText.LabelField') }, { type: 'X' }, 'Enter'], focus: role('textbox', 'UiText.LabelField'), holds: (fake) => itemOf(fake.settings, 'fan.cpu').label?.endsWith('X') === true },
  // The undo selects what it changed back, the pill's first item, whose label is the pill's; the next step waits for it.
  { name: 'Ctrl+Z undoes', acts: ['Control+Z'], holds: (fake) => itemOf(fake.settings, 'fan.cpu').label?.endsWith('X') !== true && fake.editing?.selected === 'cpu.temperature' },
  { name: 'Tab out of an edited field moves on', acts: [{ focus: role('textbox', 'UiText.LabelField') }, { type: 'Q' }, 'Tab'], away: role('textbox', 'UiText.LabelField'), holds: (fake) => itemOf(fake.settings, 'fan.cpu').label?.endsWith('Q') === true },
  { name: 'Shift+Tab out of it, the same', acts: [{ focus: role('textbox', 'UiText.LabelField') }, { type: 'R' }, 'Shift+Tab'], away: role('textbox', 'UiText.LabelField'), holds: (fake) => itemOf(fake.settings, 'fan.cpu').label?.endsWith('R') === true },
  { name: 'A digit on a threshold knob opens its box', acts: [{ click: metric('MetricName.CpuTemperature') }, { focus: knob('UiText.WarningFrom') }, '1'], focus: box('UiText.WarningFrom') },
  { name: 'Out of range is refused, the box stays', acts: [{ type: '50' }, 'Enter'], focus: box('UiText.WarningFrom'), holds: (fake) => threshold(fake).warning === 90 },
  { name: 'Esc puts the knob back', acts: ['Escape'], focus: knob('UiText.WarningFrom') },
  { name: 'Critical under Warning pushes it', acts: [{ focus: knob('UiText.CriticalFrom') }, '7', { type: '0' }, 'Enter'], focus: knob('UiText.CriticalFrom'), holds: (fake) => threshold(fake).critical === 70 && threshold(fake).warning === 65 },
  { name: 'End on a knob, the knob keeps focus', acts: ['End'], focus: knob('UiText.CriticalFrom'), holds: (fake) => (threshold(fake).critical ?? 0) > 70 },
  { name: 'A menu choice returns to its opener', acts: [{ focus: role('combobox', 'UiText.MeterField') }, 'Enter', 'ArrowUp', 'Enter'], focus: role('combobox', 'UiText.MeterField') },
  { name: 'Esc closes a menu back to its opener', acts: ['Enter', 'Escape'], focus: role('combobox', 'UiText.MeterField') },
  { name: 'Delete in the list goes to the next row', acts: [{ focus: metric('MetricName.CpuLoad') }, 'Delete'], focus: metric('MetricName.CpuPower'), holds: (fake) => !items(fake).some((item) => item.id === 'cpu.load') },
  { name: 'The toast\'s Undo, by keyboard', acts: [{ focus: toastUndo }, 'Enter'], holds: (fake) => items(fake).some((item) => item.id === 'cpu.load') },
  { name: 'Row menu: Move to a new overlay', acts: [{ focus: metric('MetricName.GpuLoad') }, 'Shift+F10', { focus: menuItem(() => text('UiText.MoveToNewOverlay')) }, 'Enter'], focus: metric('MetricName.GpuLoad'), holds: (fake) => fake.settings.overlays.at(-1)?.items.some((item) => item.id === 'gpu.load') === true },
  { name: 'F2 on an overlay row opens its name', acts: [{ focus: overlay('Taskbar') }, 'F2'], focus: renameBox },
  { name: 'Enter keeps the name, back on the row', acts: [{ type: 'Main' }, 'Enter'], focus: overlay('Main'), holds: (fake) => overlayOf(fake.settings, 0).name === 'Main' },
  { name: 'Delete on an overlay row', acts: [{ focus: overlay('Top right') }, 'Delete'], focus: selected, holds: (fake) => !fake.settings.overlays.some((one) => one.name === 'Top right') },
  { name: 'Arrows choose in a group of choices', acts: [{ click: overlay('Main') }, { focus: theme }, 'ArrowRight'], focus: theme, holds: (fake) => overlayOf(fake.settings, 0).style?.pills !== undefined },
  { name: 'F1 opens the sheet', acts: ['F1'], focus: focusIn(role('dialog', 'UiText.KeyboardShortcuts')) },
  { name: 'Esc closes it, back where it was', acts: ['Escape'], focus: theme },
  { name: 'Ctrl+, goes to Settings', acts: ['Control+Comma'], focus: row(() => text('UiText.SettingsPage')) },
  { name: 'A hotkey starts listening', acts: [opened('WindowText.HotkeysField'), { focus: hotkey }, 'Enter'] },
  { name: 'A combination ends it, on its button', acts: ['Control+Alt+H'], focus: hotkey, holds: (fake) => fake.settings.hotkeys?.toggle === 'Ctrl+Alt+H' },
  { name: 'Reset… opens a dialog on Cancel', acts: [opened('WindowText.FileAndReset'), { focus: button('UiText.ResetToDefaults') }, 'Enter'], focus: (page) => page.getByRole('alertdialog').getByRole('button', { name: text('UiText.Cancel') }) },
  { name: 'Esc closes it, back on Reset…', acts: ['Escape'], focus: button('UiText.ResetToDefaults') },
];

const fourthReview: FocusStep[] = [
  { name: 'Delete on a knob: the line off, on its Add button', acts: [{ click: metric('MetricName.CpuTemperature') }, { focus: knob('UiText.WarningFrom') }, 'Delete'], focus: button('UiText.AddWarningLine'), holds: (fake) => threshold(fake).warning === undefined },
  { name: 'Undo: the line back', acts: ['Control+Z'], focus: knob('UiText.WarningFrom'), holds: (fake) => threshold(fake).warning === 90 },
  { name: 'Enter on a knob opens its box', acts: [{ focus: knob('UiText.WarningFrom') }, 'Enter'], focus: box('UiText.WarningFrom') },
  { name: 'An emptied box and Enter: off, on its Add button', acts: ['Backspace', 'Enter'], focus: button('UiText.AddWarningLine'), holds: (fake) => threshold(fake).warning === undefined },
  { name: 'Add warning line, on its knob', acts: ['Enter'], focus: knob('UiText.WarningFrom'), holds: (fake) => threshold(fake).warning !== undefined },
  { name: 'Tab from a typed Warning: kept, on Critical', acts: ['8', { type: '0' }, 'Tab'], focus: knob('UiText.CriticalFrom'), holds: (fake) => threshold(fake).warning === 80 },
  { name: 'A click on a knob opens its box', acts: [{ click: knob('UiText.CriticalFrom') }], focus: box('UiText.CriticalFrom') },
  { name: 'A click in the plot moves the nearest line', acts: ['Escape', { click: plot, low: true }], focus: knob('UiText.WarningFrom'), holds: (fake) => (threshold(fake).warning ?? 80) < 80 },
  { name: 'Arrow nudges undo as one step', acts: [{ focus: knob('UiText.CriticalFrom') }, 'ArrowDown', 'ArrowDown', 'Control+Z'], focus: knob('UiText.CriticalFrom'), holds: (fake) => threshold(fake).critical === 95 },
  { name: 'The chart\'s … resets, back on …', acts: [{ focus: button('UiText.ChartActions') }, 'Enter', { focus: menuItem(() => text('UiText.ResetThreshold')) }, 'Enter'], focus: button('UiText.ChartActions') },
  { name: 'Tab in a menu closes it and moves on', acts: [{ focus: role('combobox', 'UiText.MeterField') }, 'Enter', 'Tab'], away: role('combobox', 'UiText.MeterField') },
  { name: 'Alt+Space opens the system menu', acts: [], frame: true },
  { name: 'Esc gives the focus back', acts: [], frame: true },
  { name: 'X in it maximizes', acts: [], frame: true },
  { name: 'A pill is a row of its own', acts: [{ focus: overlay('Taskbar') }, 'ArrowDown'], focus: pill() },
  { name: 'Right goes into the pill, its first item', acts: ['ArrowRight'], focus: (page) => list(page).getByRole('treeitem', { level: 3 }).first() },
  { name: 'Hide this overlay: the same button, now Show', acts: [{ click: overlay('Taskbar') }, { focus: button('UiText.HideThisOverlay') }, 'Enter'], focus: button('UiText.ShowThisOverlay'), holds: (fake) => overlayOf(fake.settings, 0).hidden === true },
  { name: 'Show: the same button, Hide again', acts: ['Enter'], focus: button('UiText.HideThisOverlay'), holds: (fake) => overlayOf(fake.settings, 0).hidden !== true },
  { name: 'An unsafe hotkey is refused', acts: ['Control+Comma', opened('WindowText.HotkeysField'), { focus: hotkey }, 'Enter', 'Control+C'], focus: hotkey, holds: (fake) => fake.settings.hotkeys?.toggle !== 'Ctrl+C' },
  { name: 'Reset…: Cancel is focused', acts: [opened('WindowText.FileAndReset'), { focus: button('UiText.ResetToDefaults') }, 'Enter'], focus: (page) => page.getByRole('alertdialog').getByRole('button', { name: text('UiText.Cancel') }) },
  { name: 'Esc closes it, back on Reset…', acts: ['Escape'], focus: button('UiText.ResetToDefaults') },
];

const refused = (combination: string): Act[] => [{ focus: hotkey }, 'Enter', combination];
const noToggle = (fake: FakeState): boolean => fake.settings.hotkeys?.toggle === undefined;
const fromTheList = (target: Target): Act[] => [{ focus: button('UiText.ShowList') }, 'Enter', { focus: target }, 'Enter'];

export const focusContract: FocusRun[] = [
  { name: 'the list, fields and menus', start: fakeState, size: wide, colorScheme: 'dark', steps: firstRun },
  { name: 'the catalog', start: fakeState, size: wide, colorScheme: 'light', steps: [
    { name: 'Ctrl+N opens the catalog on its search', acts: [{ focus: metric('MetricName.CpuTemperature') }, 'Control+N'], focus: catalogSearch },
    { name: 'Down goes into the results', acts: [{ type: 'gpu temp' }, 'ArrowDown'], focus: catalogResults },
    { name: 'F2 renames a sensor, back on its row', acts: ['F2', 'End', { type: ' hot' }, 'Enter'], focus: catalogResults, holds: renamedHot },
    { name: 'Ctrl+Z in the search undoes the rename at once', acts: [{ focus: catalogSearch }, 'Control+Z'], focus: catalogSearch, holds: (fake) => !renamedHot(fake) },
    { name: 'Ctrl+Y on a row redoes it', acts: ['ArrowDown', 'Control+Y'], focus: catalogResults, holds: renamedHot },
    { name: 'Esc closes it, back where it opened', acts: ['Escape'], focus: metric('MetricName.CpuTemperature') },
    { name: 'Enter adds the first result and selects it', acts: ['Control+N', { type: boardTemperature }, 'Enter'], focus: selected, holds: (fake) => items(fake).length === 8 },
  ] },
  { name: 'live values refresh in place', start: () => fakeState({ finderPick: boardFan }), size: wide, colorScheme: 'dark', steps: [
    { name: 'A threshold knob keeps focus through refreshes', acts: [{ click: metric('MetricName.CpuTemperature') }, { focus: knob('UiText.CriticalFrom') }, { fake: warmer() }], focus: knob('UiText.CriticalFrom') },
    { name: 'Arrow on the knob moves the line, focus stays', acts: ['ArrowDown'], focus: knob('UiText.CriticalFrom'), holds: (fake) => threshold(fake).critical === 94 },
    { name: 'Find it moves to Cancel', acts: [{ click: metric('MetricName.CpuFan') }, { focus: sensorGroup('CatalogText.FindIt') }, 'Enter'], focus: sensorGroup('UiText.Cancel') },
    { name: 'When it finishes, the result takes it', acts: [{ fake: { finder: { phase: 'done', pick: boardFan } } }], focus: sensorGroup('CatalogText.UseIt') },
  ] },
  { name: 'the driver', start: () => fakeState({ sensors: recording('no-admin'), driverAnswer: 'refused' }), size: wide, colorScheme: 'dark', steps: [
    { name: 'Install driver: Windows asks', acts: [{ focus: button('WindowText.InstallDriver') }, 'Enter'] },
    { name: 'No: the failure, on Try again', acts: [], focus: button('WindowText.TryAgain') },
    { name: 'Yes: it installs and the list keeps focus', acts: [{ fake: { driverAnswer: 'accepted' } }, 'Enter'], focus: selected },
  ] },
  { name: 'the narrowest window', start: fakeState, size: narrowest, colorScheme: 'dark', steps: [
    { name: 'Maximize keeps focus on its button', acts: [], frame: true },
    { name: 'Restore down, the same', acts: [], frame: true },
    { name: 'Narrow: the button opens the list on its row', acts: [{ focus: button('UiText.ShowList') }, 'Enter'], focus: selected },
    { name: 'Enter on a row folds it and goes into the pane', acts: ['ArrowDown', 'Enter'], focus: focusIn(main) },
    { name: 'A click on a row folds it, on the button', acts: [{ click: button('UiText.ShowList') }, { click: metric('MetricName.CpuLoad') }], focus: button('UiText.ShowList') },
    { name: 'A click beside the open list folds it, on the button', acts: [{ click: button('UiText.ShowList') }, { at: { x: 600, y: 400 } }], focus: button('UiText.ShowList') },
  ] },
  { name: 'the third review', start: withHotkeys({ toggle: 'Ctrl+Alt+H', clickThrough: 'Ctrl+Alt+T' }), size: wide, colorScheme: 'dark', steps: [
    { name: 'Clear a hotkey, focus stays on it', acts: ['Control+Comma', opened('WindowText.HotkeysField'), { focus: clearHotkey }, 'Enter'], focus: hotkey, holds: noToggle },
    { name: 'While capturing, thermi\'s hotkeys rest', acts: [{ focus: hotkey }, 'Enter', 'Control+Alt+T'], focus: hotkey, holds: (fake) => !fake.settings.overlays.some((one) => one.clickThrough === true) },
    { name: 'Color menu: Tab reaches the hex box', acts: [{ click: overlay('Taskbar') }, { focus: button('OverlayPageText.ColorsField') }, 'Enter', { focus: button('StyleColor.Warning') }, 'Enter', 'Tab'], focus: role('textbox', 'UiText.HexColor') },
    { name: 'A bad hex stays', acts: [{ type: 'zz' }, 'Enter'], focus: role('textbox', 'UiText.HexColor'), holds: (fake) => overlayOf(fake.settings, 0).style?.colors?.warning === undefined },
    { name: 'A good one returns to its opener', acts: [{ fill: role('textbox', 'UiText.HexColor'), value: '#123456' }, 'Enter'], focus: button('StyleColor.Warning'), holds: (fake) => overlayOf(fake.settings, 0).style?.colors?.warning === '#123456' },
    { name: 'New overlay: its row', acts: [{ focus: button('UiText.MoreActions') }, 'Enter', { focus: menuItem(() => text('UiText.NewOverlay')) }, 'Enter'], focus: overlay('Bottom right'), holds: (fake) => fake.settings.overlays.length === 3 },
    { name: 'Merge into: the target\'s row', acts: [{ focus: overlay('Bottom right') }, 'Shift+F10', { focus: menuItem(() => text('UiText.MergeInto')) }, 'Enter', { focus: menuItem(() => 'Taskbar') }, 'Enter'], focus: overlay('Taskbar'), holds: (fake) => fake.settings.overlays.length === 2 },
    { name: 'Delete this overlay: the next overlay\'s row', acts: [{ click: overlay('Top right') }, { focus: button('UiText.DeleteThisOverlay') }, 'Enter'], focus: overlay('Taskbar'), holds: (fake) => fake.settings.overlays.length === 1 },
  ] },
  { name: 'renaming in the narrow list', start: fakeState, size: narrowest, colorScheme: 'dark', steps: [
    { name: 'Narrow: F2 renames in the open list', acts: [{ focus: button('UiText.ShowList') }, 'Enter', 'F2'], focus: renameBox },
    { name: 'Enter keeps the name, the list stays open', acts: ['Control+A', { type: 'Die' }, 'Enter'], focus: selected, holds: (fake) => [...fake.settings.overlays, ...items(fake)].some((one) => one.name === 'Die') },
    { name: 'Esc cancels a rename first', acts: ['F2', { type: 'zzz' }, 'Escape'], focus: selected, holds: (fake) => ![...fake.settings.overlays, ...items(fake)].some((one) => one.name?.includes('zzz') === true) },
    { name: 'The pane under the open list is inert', acts: ['Tab'], away: focusIn(main) },
    { name: 'Win+Up widens it: the list stays, focus on the row', acts: [{ focus: metric('MetricName.CpuLoad') }, { size: wide }], focus: metric('MetricName.CpuLoad') },
  ] },
  { name: 'widened on the list\'s button', start: fakeState, size: narrowest, colorScheme: 'dark', steps: [
    { name: 'Win+Up on the list\'s button: the selected row', acts: [{ focus: button('UiText.ShowList') }, { size: wide }], focus: selected },
  ] },
  { name: 'the fourth review', start: fakeState, size: wide, colorScheme: 'dark', steps: fourthReview },
  { name: 'the narrow window never drops the focus', start: fakeState, size: narrowest, colorScheme: 'dark', steps: [
    { name: 'Narrow: Ctrl+, lands on a control in view', acts: ['Control+Comma'] },
    { name: 'Narrow: Ctrl+N, Enter adds, focus in view', acts: ['Control+N', { type: boardTemperature }, 'Enter'], holds: (fake) => items(fake).length === 8 },
    { name: 'Ctrl+Z with a slider focused undoes', acts: [...fromTheList(overlay('Taskbar')), { focus: role('slider', 'UiText.SizeField') }, 'ArrowRight', 'Control+Z'], focus: role('slider', 'UiText.SizeField'), holds: (fake) => (overlayOf(fake.settings, 0).size ?? 1) === 1 },
  ] },
  { name: 'deleting in the narrow window', start: fakeState, size: narrowest, colorScheme: 'dark', steps: [
    { name: 'Narrow: Delete this overlay, focus in view', acts: [...fromTheList(overlay('Top right')), { focus: button('UiText.DeleteThisOverlay') }, 'Enter'], holds: (fake) => fake.settings.overlays.length === 1 },
  ] },
  { name: 'a crowded overlay in the narrow window', start: () => fakeState({ crowded: [0] }), size: narrowest, colorScheme: 'dark', steps: [
    { name: 'Narrow: Hide labels, its button goes', acts: [...fromTheList(overlay('Taskbar')), { focus: button('FitText.HideLabels') }, 'Enter'], holds: (fake) => overlayOf(fake.settings, 0).style?.noLabels === true },
    { name: 'Narrow: Ctrl+Z brings the labels back', acts: ['Control+Z'], holds: (fake) => overlayOf(fake.settings, 0).style?.noLabels !== true },
  ] },
  { name: 'pills and hotkeys', start: fakeState, size: wide, colorScheme: 'dark', steps: [
    { name: 'Left on a pill row folds it', acts: [{ focus: pill() }, 'ArrowLeft'], focus: pill(false) },
    { name: 'Right unfolds it', acts: ['ArrowRight'], focus: pill(true) },
    { name: 'Shift+F10 on a pill opens its menu', acts: ['Shift+F10'], focus: focusIn(menu) },
    { name: 'Esc: back on the pill row', acts: ['Escape'], focus: pill() },
    { name: 'End stays in the tree', acts: ['End'], focus: focusIn(list), away: row(() => text('UiText.SettingsPage')) },
    { name: 'Win+H is refused: Win belongs to Windows', acts: ['Control+Comma', opened('WindowText.HotkeysField'), ...refused('Meta+H')], focus: hotkey, holds: noToggle },
    { name: 'Alt+H is refused: it opens menus', acts: refused('Alt+H'), focus: hotkey, holds: noToggle },
    { name: 'Ctrl+Shift+Esc is refused: Task Manager', acts: refused('Control+Shift+Escape'), focus: hotkey, holds: noToggle },
    { name: 'F13 alone is taken', acts: [{ focus: hotkey }, 'Enter', { key: 'F13' }], focus: hotkey, holds: (fake) => fake.settings.hotkeys?.toggle === 'F13' },
  ] },
  // The keyboard layout is Windows', read through the bridge; with the fake's, the refusal is Core's test and this
  // step keeps only the focus.
  { name: 'a Spanish keyboard', start: fakeState, size: wide, colorScheme: 'dark', steps: [
    { name: 'Spanish keyboard: Ctrl+Alt+2 types @, refused', acts: ['Control+Comma', opened('WindowText.HotkeysField'), ...refused('Control+Alt+2')], focus: hotkey },
  ] },
  { name: 'the final polish', start: fakeState, size: wide, colorScheme: 'dark', steps: [
    // Undo waits for the step it takes back, as a person's press does: the answer comes over the bridge.
    { name: 'Tray icon on', acts: [{ click: metric('MetricName.CpuFan') }, { focus: role('switch', 'UiText.OwnTrayIcon') }, 'Space'], focus: role('switch', 'UiText.OwnTrayIcon'), holds: (fake) => itemOf(fake.settings, 'fan.cpu').tray === true },
    { name: 'Title-bar Undo of the last step keeps focus', acts: [{ focus: button('UiText.Undo') }, 'Enter'], focus: button('UiText.Undo'), holds: (fake) => itemOf(fake.settings, 'fan.cpu').tray !== true },
    { name: 'Title-bar Redo, the same', acts: [{ focus: button('UiText.Redo') }, 'Enter'], focus: button('UiText.Redo'), holds: (fake) => itemOf(fake.settings, 'fan.cpu').tray === true },
    { name: 'Separate pill on, Ctrl+Z turns it off again', acts: [{ focus: role('switch', 'UiText.OwnPill') }, 'Space', 'Control+Z'], focus: role('switch', 'UiText.OwnPill'), holds: (fake) => itemOf(fake.settings, 'fan.cpu').own !== true },
    { name: 'Separate pill is off on a pill\'s first item', acts: [{ click: metric('MetricName.CpuFan') }], focus: metric('MetricName.CpuFan') },
    { name: 'Alt+Down moves the focused row, not the selected', acts: [{ focus: metric('MetricName.CpuLoad') }, 'Alt+ArrowDown'], focus: metric('MetricName.CpuLoad'), holds: (fake) => ids(fake, 0) === 'cpu.temperature,fan.cpu,cpu.power,cpu.load,gpu.load,ram.used' },
    { name: 'Alt+Down on a pill row moves nothing', acts: [{ focus: pill() }, 'Alt+ArrowDown'], focus: pill(), holds: (fake) => ids(fake, 0) === 'cpu.temperature,fan.cpu,cpu.power,cpu.load,gpu.load,ram.used' },
    { name: 'Down on the last row stays in the tree', acts: [{ focus: metric('MetricName.GpuTemperature') }, 'ArrowDown'], focus: metric('MetricName.GpuTemperature') },
  ] },
  // "Hide overlays" is the overlay's hotkey, registered with Windows, so in the page its keys change nothing.
  { name: 'hiding overlays', start: withHotkeys({ toggle: 'Ctrl+Alt+O' }), size: wide, colorScheme: 'dark', steps: [
    { name: 'Hide overlays and back: one hidden stays hidden', acts: [{ click: overlay('Top right') }, { focus: button('UiText.HideThisOverlay') }, 'Enter', 'Control+Alt+O', 'Control+Alt+O'], focus: button('UiText.ShowThisOverlay'), holds: (fake) => overlayOf(fake.settings, 1).hidden === true && overlayOf(fake.settings, 0).hidden !== true },
  ] },
  // Live QA: what the real overlays and the pointer do, beyond the prototype's steps.
  { name: 'the desktop', start: () => withFloating({ start: { overlay: 0, item: 'gpu.load', how: 'double-click' } }), size: wide, colorScheme: 'dark', steps: [
    { name: 'Opened from a value: on its row, the overlay told', acts: [], focus: metric('MetricName.GpuLoad'), holds: (fake) => fake.editing?.overlay === 0 && fake.editing.selected === 'gpu.load' },
    { name: 'A click on a value selects its row', acts: [{ pick: { overlay: 1, item: 'gpu.temperature', how: 'click' } }], focus: metric('MetricName.GpuTemperature'), holds: (fake) => fake.editing?.overlay === 1 && fake.editing.selected === 'gpu.temperature' },
    { name: 'A double-click on an edge selects its overlay', acts: [{ pick: { overlay: 0, item: null, how: 'double-click' } }], focus: overlay('Taskbar'), holds: (fake) => fake.editing?.overlay === 0 && fake.editing.selected === null },
    { name: 'Alt+Right on a floating overlay\'s row moves it 8 px', acts: [{ focus: overlay('Floating') }, 'Alt+ArrowRight'], focus: overlay('Floating'), holds: (fake) => placed(fake, 1) === '408,300' },
    { name: 'Shift+Alt+Up moves it 1 px', acts: ['Shift+Alt+ArrowUp'], focus: overlay('Floating'), holds: (fake) => placed(fake, 1) === '408,299' },
    { name: 'Ctrl+Z puts it back as one step', acts: ['Control+Z'], focus: overlay('Floating'), holds: (fake) => placed(fake, 1) === '400,300' },
    { name: 'A right-click on a row opens its menu', acts: [{ click: metric('MetricName.CpuLoad'), right: true }], focus: focusIn(menu) },
    { name: 'Esc: back on that row, selected', acts: ['Escape'], focus: metric('MetricName.CpuLoad') },
  ] },
  { name: 'a closing panel', start: fakeState, size: wide, colorScheme: 'dark', motion: true, steps: [
    { name: 'Ctrl+N opens the catalog', acts: [{ focus: metric('MetricName.CpuTemperature') }, 'Control+N'], focus: catalogSearch },
    { name: 'Esc, then F1 while it slides away, opens the sheet', acts: ['Escape', { sliding: true }, 'F1'], focus: focusIn(role('dialog', 'UiText.KeyboardShortcuts')) },
    { name: 'Esc closes the sheet, back where the catalog opened', acts: ['Escape'], focus: metric('MetricName.CpuTemperature') },
  ] },
];

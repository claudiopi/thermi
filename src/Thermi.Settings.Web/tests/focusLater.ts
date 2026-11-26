import { fakeState, itemOf, list, overlayOf, text } from './fakeBridge';
import {
  wide,
  narrowest,
  role,
  button,
  row,
  metric,
  overlay,
  pill,
  focusIn,
  menu,
  hotkey,
  opened,
  catalogSearch,
  boardTemperature,
  items,
  ids,
  withHotkeys,
  withFloating,
  placed,
  refused,
  noToggle,
  fromTheList,
  type FocusRun,
} from './focusTargets';
import { fourthReview } from './focusSteps';

// The focus contract's later runs: the fourth review on, and the live QA's.
export const laterRuns: FocusRun[] = [
  { name: 'the fourth review', start: fakeState, size: wide, colorScheme: 'dark', steps: fourthReview },
  {
    name: 'the narrow window never drops the focus',
    start: fakeState,
    size: narrowest,
    colorScheme: 'dark',
    steps: [
      { name: 'Narrow: Ctrl+, lands on a control in view', acts: ['Control+Comma'] },
      {
        name: 'Narrow: Ctrl+N, Enter adds, focus in view',
        acts: ['Control+N', { type: boardTemperature }, 'Enter'],
        holds: (fake) => items(fake).length === 8,
      },
      {
        name: 'Ctrl+Z with a slider focused undoes',
        acts: [...fromTheList(overlay('Taskbar')), { focus: role('slider', 'UiText.SizeField') }, 'ArrowRight', 'Control+Z'],
        focus: role('slider', 'UiText.SizeField'),
        holds: (fake) => (overlayOf(fake.settings, 0).size ?? 1) === 1,
      },
    ],
  },
  {
    name: 'deleting in the narrow window',
    start: fakeState,
    size: narrowest,
    colorScheme: 'dark',
    steps: [
      {
        name: 'Narrow: Delete this overlay, focus in view',
        acts: [...fromTheList(overlay('Top right')), { focus: button('UiText.DeleteThisOverlay') }, 'Enter'],
        holds: (fake) => fake.settings.overlays.length === 1,
      },
    ],
  },
  {
    name: 'pills and hotkeys',
    start: fakeState,
    size: wide,
    colorScheme: 'dark',
    steps: [
      { name: 'Left on a pill row folds it', acts: [{ focus: pill() }, 'ArrowLeft'], focus: pill(false) },
      { name: 'Right unfolds it', acts: ['ArrowRight'], focus: pill(true) },
      { name: 'Shift+F10 on a pill opens its menu', acts: ['Shift+F10'], focus: focusIn(menu) },
      { name: 'Esc: back on the pill row', acts: ['Escape'], focus: pill() },
      { name: 'End stays in the tree', acts: ['End'], focus: focusIn(list), away: row(() => text('UiText.SettingsPage')) },
      {
        name: 'Win+H is refused: Win belongs to Windows',
        acts: ['Control+Comma', opened('WindowText.HotkeysField'), ...refused('Meta+H')],
        focus: hotkey,
        holds: noToggle,
      },
      { name: 'Alt+H is refused: it opens menus', acts: refused('Alt+H'), focus: hotkey, holds: noToggle },
      { name: 'Ctrl+Shift+Esc is refused: Task Manager', acts: refused('Control+Shift+Escape'), focus: hotkey, holds: noToggle },
      {
        name: 'F13 alone is taken',
        acts: [{ focus: hotkey }, 'Enter', { key: 'F13' }],
        focus: hotkey,
        holds: (fake) => fake.settings.hotkeys?.toggle === 'F13',
      },
    ],
  },
  // The keyboard layout is Windows', read through the bridge; with the fake's, the refusal is Core's test and this
  // step keeps only the focus.
  {
    name: 'a Spanish keyboard',
    start: fakeState,
    size: wide,
    colorScheme: 'dark',
    steps: [
      {
        name: 'Spanish keyboard: Ctrl+Alt+2 types @, refused',
        acts: ['Control+Comma', opened('WindowText.HotkeysField'), ...refused('Control+Alt+2')],
        focus: hotkey,
      },
    ],
  },
  {
    name: 'the final polish',
    start: fakeState,
    size: wide,
    colorScheme: 'dark',
    steps: [
      // Undo waits for the step it takes back, as a person's press does: the answer comes over the bridge.
      {
        name: 'Tray icon on',
        acts: [{ click: metric('MetricName.CpuFan') }, { focus: role('switch', 'UiText.OwnTrayIcon') }, 'Space'],
        focus: role('switch', 'UiText.OwnTrayIcon'),
        holds: (fake) => itemOf(fake.settings, 'fan.cpu').tray === true,
      },
      {
        name: 'Title-bar Undo of the last step keeps focus',
        acts: [{ focus: button('UiText.Undo') }, 'Enter'],
        focus: button('UiText.Undo'),
        holds: (fake) => itemOf(fake.settings, 'fan.cpu').tray !== true,
      },
      {
        name: 'Title-bar Redo, the same',
        acts: [{ focus: button('UiText.Redo') }, 'Enter'],
        focus: button('UiText.Redo'),
        holds: (fake) => itemOf(fake.settings, 'fan.cpu').tray === true,
      },
      {
        name: 'Separate pill on, Ctrl+Z turns it off again',
        acts: [{ focus: role('switch', 'UiText.OwnPill') }, 'Space', 'Control+Z'],
        focus: role('switch', 'UiText.OwnPill'),
        holds: (fake) => itemOf(fake.settings, 'fan.cpu').own !== true,
      },
      { name: "Separate pill is off on a pill's first item", acts: [{ click: metric('MetricName.CpuFan') }], focus: metric('MetricName.CpuFan') },
      {
        name: 'Alt+Down moves the focused row, not the selected',
        acts: [{ focus: metric('MetricName.CpuLoad') }, 'Alt+ArrowDown'],
        focus: metric('MetricName.CpuLoad'),
        holds: (fake) => ids(fake, 0) === 'cpu.temperature,fan.cpu,cpu.power,cpu.load,gpu.load,ram.used',
      },
      {
        name: 'Alt+Down on a pill row moves the pill, which keeps the focus',
        acts: [{ focus: pill() }, 'Alt+ArrowDown'],
        focus: row(() => text('UiText.PillName', text('MetricLabel.Cpu'))),
        holds: (fake) => ids(fake, 0) === 'gpu.load,cpu.temperature,fan.cpu,cpu.power,cpu.load,ram.used',
      },
      {
        name: 'Down on the last row stays in the tree',
        acts: [{ focus: metric('MetricName.GpuTemperature') }, 'ArrowDown'],
        focus: metric('MetricName.GpuTemperature'),
      },
    ],
  },
  // "Hide overlays" is the overlay's hotkey, registered with Windows, so in the page its keys change nothing.
  {
    name: 'hiding overlays',
    start: withHotkeys({ toggle: 'Ctrl+Alt+O' }),
    size: wide,
    colorScheme: 'dark',
    steps: [
      {
        name: 'Hide overlays and back: one hidden stays hidden',
        acts: [{ click: overlay('Top right') }, { focus: button('UiText.HideThisOverlay') }, 'Enter', 'Control+Alt+O', 'Control+Alt+O'],
        focus: button('UiText.ShowThisOverlay'),
        holds: (fake) => overlayOf(fake.settings, 1).hidden === true && overlayOf(fake.settings, 0).hidden !== true,
      },
    ],
  },
  // Live QA: what the real overlays and the pointer do, beyond the prototype's steps.
  {
    name: 'the desktop',
    start: () => withFloating({ start: { overlay: 0, item: 'gpu.load', how: 'double-click' } }),
    size: wide,
    colorScheme: 'dark',
    steps: [
      {
        name: 'Opened from a value: on its row, the overlay told',
        acts: [],
        focus: metric('MetricName.GpuLoad'),
        holds: (fake) => fake.editing?.overlay === 0 && fake.editing.selected === 'gpu.load',
      },
      {
        name: 'A click on a value selects its row',
        acts: [{ pick: { overlay: 1, item: 'gpu.temperature', how: 'click' } }],
        focus: metric('MetricName.GpuTemperature'),
        holds: (fake) => fake.editing?.overlay === 1 && fake.editing.selected === 'gpu.temperature',
      },
      {
        name: 'A double-click on an edge selects its overlay',
        acts: [{ pick: { overlay: 0, item: null, how: 'double-click' } }],
        focus: overlay('Taskbar'),
        holds: (fake) => fake.editing?.overlay === 0 && fake.editing.selected === null,
      },
      {
        name: "Alt+Right on a floating overlay's row moves it 8 px",
        acts: [{ focus: overlay('Floating') }, 'Alt+ArrowRight'],
        focus: overlay('Floating'),
        holds: (fake) => placed(fake, 1) === '408,300',
      },
      { name: 'Shift+Alt+Up moves it 1 px', acts: ['Shift+Alt+ArrowUp'], focus: overlay('Floating'), holds: (fake) => placed(fake, 1) === '408,299' },
      { name: 'Ctrl+Z puts it back as one step', acts: ['Control+Z'], focus: overlay('Floating'), holds: (fake) => placed(fake, 1) === '400,300' },
      { name: 'A right-click on a row opens its menu', acts: [{ click: metric('MetricName.CpuLoad'), right: true }], focus: focusIn(menu) },
      { name: 'Esc: back on that row, selected', acts: ['Escape'], focus: metric('MetricName.CpuLoad') },
    ],
  },
  {
    name: 'a closing panel',
    start: fakeState,
    size: wide,
    colorScheme: 'dark',
    motion: true,
    steps: [
      { name: 'Ctrl+N opens the catalog', acts: [{ focus: metric('MetricName.CpuTemperature') }, 'Control+N'], focus: catalogSearch },
      {
        name: 'Esc, then F1 while it slides away, opens the sheet',
        acts: ['Escape', { sliding: true }, 'F1'],
        focus: focusIn(role('dialog', 'UiText.KeyboardShortcuts')),
      },
      { name: 'Esc closes the sheet, back where the catalog opened', acts: ['Escape'], focus: metric('MetricName.CpuTemperature') },
    ],
  },
];

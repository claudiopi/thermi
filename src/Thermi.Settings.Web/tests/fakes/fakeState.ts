import type { FakeState, Item, Overlay } from '../bridgeTypes';
import { recording, text, texts } from '../pageChecks';

// The state a test starts the fake from: what the C# side would hold on the author's PC.

function defaultItems(): Item[] {
  return [
    {
      id: 'cpu.temperature',
      name: text('MetricName.CpuTemperature'),
      metric: 'cpu.temperature',
      meter: { metric: 'cpu.temperature' },
      threshold: { warning: 90, critical: 95 },
    },
    { id: 'fan.cpu', name: text('MetricName.CpuFan'), metric: 'fan.cpu', meter: { metric: 'fan.cpu' } },
    { id: 'cpu.load', name: text('MetricName.CpuLoad'), metric: 'cpu.load', meter: { metric: 'cpu.load' } },
    { id: 'cpu.power', name: text('MetricName.CpuPower'), metric: 'cpu.power' },
    { id: 'gpu.load', name: text('MetricName.GpuLoad'), metric: 'gpu.load', meter: { metric: 'gpu.load' } },
    { id: 'ram.used', name: text('MetricName.RamUsed'), metric: 'ram.used', meter: { metric: 'ram.load' } },
  ];
}

/** The second monitor's EDID id; the primary one is left out of a snap. */
export const secondMonitor = 'DEL-A1B2-7XQ9K13';

/** Two overlays, the default one on the taskbar and a GPU one at the top right, two monitors and the admin recording. */
export function fakeState(change: Partial<FakeState> = {}): FakeState {
  const taskbar: Overlay = { name: 'Taskbar', items: defaultItems() };
  const topRight: Overlay = {
    name: 'Top right',
    snap: { spot: 'top-right' },
    items: [
      {
        id: 'gpu.temperature',
        name: text('MetricName.GpuTemperature'),
        metric: 'gpu.temperature',
        meter: { metric: 'gpu.temperature' },
        threshold: { warning: 80, critical: 88 },
      },
    ],
  };
  return {
    settings: { version: 2, overlays: [taskbar, topRight] },
    defaultOverlay: structuredClone(taskbar),
    sensors: recording('admin'),
    service: 'running',
    overlayRunning: true,
    problem: null,
    driverAnswer: 'accepted',
    serviceAnswer: 'accepted',
    monitors: [
      {
        id: 'SAM-7F0A-H4ZR900123',
        primary: true,
        bounds: { x: 0, y: 0, width: 2560, height: 1440 },
        workArea: { x: 0, y: 0, width: 2560, height: 1392 },
        scale: 1.5,
        taskbar: true,
      },
      {
        id: secondMonitor,
        primary: false,
        bounds: { x: 2560, y: 0, width: 1920, height: 1080 },
        workArea: { x: 2560, y: 0, width: 1920, height: 1032 },
        scale: 1,
        taskbar: true,
      },
    ],
    looks: { Night: { pills: 'dark', opacity: { background: 0.4 }, font: 'bahnschrift', corners: 'soft' } },
    exported: null,
    importFile: null,
    clipboard: null,
    startWithWindows: false,
    heldHotkeys: [],
    failingHotkeys: [],
    hotkeysResting: false,
    crowded: [],
    crowdedWithoutLabels: [],
    finder: { phase: 'idle', pick: null },
    finderPick: null,
    texts: texts(),
    languages: (['en', 'es'] as const).map((code) => ({ code, name: texts(code)['UiText.LanguageName'] ?? code })),
    start: null,
    editing: null,
    lateReplies: false,
    ...change,
  };
}

const reading = (state: FakeState, of: (hardwareType: string) => boolean): FakeState => ({
  ...state,
  sensors: state.sensors.map((sensor) => (of(sensor.hardwareType) ? { ...sensor, value: null } : sensor)),
});

/**
 * What real hardware does that a recording on a good day never shows, each a start of its own or a change the C# side
 * raises once the page is open: a reading that is no number, which the bridge's JSON carries as null; every reading
 * missing; readings that froze, which C# says by calling the service stale; the CPU's sensors gone mid-session, as a
 * driver that resets drops them.
 */
export const hostile: { name: string; start: () => FakeState; then?: (state: FakeState) => Partial<FakeState> }[] = [
  { name: 'NoNumber', start: () => reading(fakeState(), (type) => type === 'Cpu') },
  { name: 'NothingReads', start: () => reading(fakeState(), () => true) },
  { name: 'Frozen', start: () => fakeState({ service: 'stale' }) },
  { name: 'SensorsGo', start: fakeState, then: (state) => ({ sensors: state.sensors.filter((sensor) => sensor.hardwareType !== 'Cpu') }) },
];

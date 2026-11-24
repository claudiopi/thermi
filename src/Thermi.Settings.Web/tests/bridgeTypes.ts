// What the page's tests fake behind the bridge, whose shapes the build generates from the C# side.

import type { DesktopPick, Edit, FinderView, Language, Monitor, Overlay, Problem, Sensor, Service, Settings, Style } from '../src/bridge/bridge.g';

export type * from '../src/bridge/bridge.g';

/** The chart's edits of a line, which the fake's views apply. */
export type LineEdit = Extract<Edit, { kind: 'line' | 'typedLine' | 'addLine' | 'resetLines' }>;

/** A sensor renamed in the catalog, which the fake's catalog applies. */
export type SensorNameEdit = Extract<Edit, { kind: 'sensorName' }>;

/** Everything the C# side holds that the page can see, which a test sets and reads back. */
export interface FakeState {
  settings: Settings;
  defaultOverlay: Overlay;
  sensors: Sensor[];
  service: Service;
  overlayRunning: boolean;
  problem: Problem | null;
  /** Failed is the call itself failing, as an exception on the C# side rejects it. */
  driverAnswer: 'accepted' | 'refused' | 'failed';
  serviceAnswer: 'accepted' | 'refused';
  monitors: Monitor[];
  looks: Record<string, Style>;
  exported: string | null;
  /** The text of the look file Windows' dialog picks for an import, or null when it is cancelled. */
  importFile: string | null;
  /** The text Windows' clipboard holds for a paste, or null while another app holds the clipboard. */
  clipboard: string | null;
  startWithWindows: boolean;
  heldHotkeys: string[];
  /** Combinations whose check fails, as an exception on the C# side rejects it. */
  failingHotkeys: string[];
  hotkeysResting: boolean;
  /** The overlays that do not fit beside the taskbar's apps while their labels show. */
  crowded: number[];
  /** The overlays that do not fit beside the taskbar's apps even without their labels. */
  crowdedWithoutLabels: number[];
  finder: FinderView;
  finderPick: string | null;
  texts: Record<string, string>;
  /** Every translation, each named in its own language. */
  languages: Language[];
  /** What the window was opened on, as a double-click on a value asks, or null. */
  start: DesktopPick | null;
  /** What the page last told the real overlay it edits: the overlay, the selected item and the item pointed at. */
  editing: { overlay: number | null; selected: string | null; pointed: string | null } | null;
}

interface FakeControl {
  state: FakeState;
  set(change: Partial<FakeState>): void;
  /** A value or an overlay's edge clicked or pointed at on a real overlay. */
  pick(pick: DesktopPick): void;
  /** How many steps undo and redo would take. */
  history(): { done: number; undone: number };
}

interface AxeResults {
  violations: { id: string; nodes: { target: string[] }[] }[];
}

declare global {
  interface Window {
    thermiFake?: FakeControl;
    thermiFakeViews?: { apply(settings: Settings, edit: LineEdit): string | null };
    thermiFakeCatalog?: { rename(settings: Settings, edit: SensorNameEdit): void };
    axe?: { run(context: { exclude: string[][] }): Promise<AxeResults> };
  }
}

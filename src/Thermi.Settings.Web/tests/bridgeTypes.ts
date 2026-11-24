// What the page's tests fake behind the bridge, whose shapes the build generates from the C# side.

import type { DesktopPick, FinderView, Language, Monitor, Overlay, Problem, Sensor, Service, Settings, Style, UndoSteps } from '../src/bridge/bridge.g';

export type * from '../src/bridge/bridge.g';

/** Everything the C# side holds that the page can see, which a test sets and reads back. */
export interface FakeState {
  settings: Settings;
  defaultOverlay: Overlay;
  sensors: Sensor[];
  service: Service;
  overlayRunning: boolean;
  problem: Problem | null;
  /** Failed is the call itself failing, as an exception on the C# side rejects it; restart, a driver that loads only once Windows restarts. */
  driverAnswer: 'accepted' | 'refused' | 'failed' | 'restart';
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
  /** While true, every read is answered with what C# held when asked, but only once a test lets the answers go. */
  lateReplies: boolean;
}

/** A call of the page that changes what the C# side holds, as the bridge's wire carries it. */
export interface OracleCall {
  method: 'edit' | 'gesture' | 'undo' | 'redo' | 'saveLook' | 'renameLook' | 'deleteLook' | 'importLook';
  args: unknown[];
}

/** What the real bridge answered a call, the settings it then showed and its looks when they changed, and its undo steps. */
export interface OracleAnswer {
  result?: unknown;
  error?: string;
  settings?: Settings;
  looks?: Record<string, Style>;
  steps: UndoSteps;
}

/** What the real bridge starts from: the settings file, the saved looks, the readings, and the window's language. */
export interface OracleStart {
  settings: Settings;
  looks: Record<string, Style>;
  /** A recording of tests/Thermi.Tests/Recordings by its name's end, or the sensors themselves. */
  sensors: 'admin' | 'no-admin' | 'admin-net-disk' | Sensor[];
  service: Service;
  texts: 'en' | 'es' | 'pseudo';
  problem: Problem | null;
}

interface FakeControl {
  state: FakeState;
  set(change: Partial<FakeState>): void;
  /** A value or an overlay's edge clicked or pointed at on a real overlay. */
  pick(pick: DesktopPick): void;
  /** Lets the late answers go, the newest first, as a busy bridge's can arrive, and answers on time from then. */
  release?(): void;
  /** Whether the page has read the settings since they last changed. */
  caughtUp(): boolean;
}

interface AxeResults {
  violations: { id: string; nodes: { target: string[] }[] }[];
}

declare global {
  interface Window {
    thermiFake?: FakeControl;
    /** The C# side's answer to a call, from the oracle's table, or why it has none. */
    thermiOracle?: (call: OracleCall) => Promise<OracleAnswer | { missing: string } | null>;
    axe?: { run(context: { exclude: string[][] }): Promise<AxeResults> };
  }
}

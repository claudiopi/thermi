// What the page's tests fake behind the bridge, whose shapes the build generates from the C# side.

import type {
  AgentsView,
  DesktopPick,
  FinderView,
  Language,
  Mark,
  Monitor,
  Overlay,
  Problem,
  Rect,
  Sensor,
  Service,
  Settings,
  Style,
  UndoSteps,
  WornLook,
} from '../src/bridge/bridge.g';

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
  /** Where each overlay shows, as C# works it out; with none, as before C# answers, the map's taskbar spot sits at the offset. */
  places: (Rect | null)[];
  looks: Record<string, Style>;
  /** The look each overlay wears, as C# decides it from the settings and the saved looks. */
  worn: (WornLook | null)[];
  exported: string | null;
  /** The text of the look file Windows' dialog picks for an import, or null when it is cancelled. */
  importFile: string | null;
  /** The text Windows' clipboard holds for a paste, or a copy put there, or null while another app holds the clipboard. */
  clipboard: string | null;
  startWithWindows: boolean;
  /** The AI agents section as C# words it: the setups for thermi-cli, and the last changes agents made. */
  agents: AgentsView;
  heldHotkeys: string[];
  /** Combinations whose check fails, as an exception on the C# side rejects it. */
  failingHotkeys: string[];
  hotkeysResting: boolean;
  finder: FinderView;
  finderPick: string | null;
  texts: Record<string, string>;
  /** The texts the desktop says in its own words, which C# gives the page in place of the language's. */
  words: Record<string, string>;
  /** Every translation, each named in its own language. */
  languages: Language[];
  /** What the window was opened on, as a double-click on a value asks, or null. */
  start: DesktopPick | null;
  /** What the page last told the real overlay it edits: the overlay, the selected item and the item pointed at. */
  editing: { overlay: number | null; selected: string | null; pointed: string | null } | null;
  /** While true, every read is answered with what C# held when asked, but only once a test lets the answers go. */
  lateReplies: boolean;
  /** The OS the host runs on, 'windows' or 'linux' as C# names it, which decides the fixes C# offers. */
  os: string;
  /** The desktop's accent, as Linux's settings portal gives it, or null. */
  accent: string | null;
  /** The bar an overlay snaps onto as onto the taskbar, as C# names it, whose words name it. */
  bar: string;
  /** Whether the window is maximized, which the caption the page draws on Linux says. */
  maximized: boolean;
  /** How many times the page asked the window to move from its title row. */
  moves: number;
  /** Whether the desktop is in high contrast, as Linux's settings portal says. */
  contrast: boolean;
  /** Where thermi's GNOME Shell extension is in a GNOME session, as C# names it, or null for none. */
  extension: string | null;
  /** What GNOME's prompt for the extension answers. */
  extensionAnswer: 'accepted' | 'refused';
}

/** A call of the page that changes what the C# side holds, as the bridge's wire carries it. */
export interface OracleCall {
  method: 'edit' | 'gesture' | 'undo' | 'redo' | 'saveLook' | 'renameLook' | 'deleteLook' | 'importLook';
  args: unknown[];
}

/**
 * A call whose answer holds whatever came before it, which the oracle keeps apart from any start: the bridge's search,
 * or the fake catalog's, which Core answers from the sensors, the language and the names it gives.
 */
export interface PureCall {
  method: 'search' | 'catalogSearch';
  args: unknown[];
}

/** What Core's catalog search finds: each device, with what matched of its name, and its sensors in order with theirs. */
export interface CatalogFound {
  id: string;
  marks: Mark[];
  sensors: { id: string; marks: Mark[]; lineMarks: Mark[]; alias?: string | null; aliasMarks: Mark[] }[];
}

/**
 * What the real bridge answered a call, the settings it then showed, its looks and the looks the overlays wear when they
 * changed, and its undo steps.
 */
export interface OracleAnswer {
  result?: unknown;
  error?: string;
  settings?: Settings;
  looks?: Record<string, Style>;
  worn?: (WornLook | null)[];
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
  /** The oracle's answer to a call, counted as on its way until it comes. */
  answered<T>(answer: Promise<T> | undefined): Promise<T | undefined>;
  /** Whether the page has every answer it asked for, and has read the settings since they last changed. */
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
    /** The C# side's answer to a call that holds whatever came before it, from the oracle's table, or why it has none. */
    thermiPure?: (call: PureCall) => Promise<{ result: unknown } | { missing: string }>;
    axe?: { run(context: { exclude: string[][] }): Promise<AxeResults> };
  }
}

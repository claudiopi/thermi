// Generated from Thermi.Core by src/Thermi.Settings.Types, which the .NET build runs: never edit it by hand.

export type Value = string | number | boolean | null | Value[] | { [key: string]: Value };

export interface About {
  version: string;
  languages: Language[];
  windows: string;
  updates: string;
}

export interface CatalogGroup {
  id: string;
  name: string;
  kind: DeviceKind | null;
  sensors: CatalogRow[];
  marks: Mark[];
}

export interface CatalogRow {
  id: string;
  name: string;
  from: NameFrom;
  kind: SensorKind;
  value: string | null;
  why: NoValue | null;
  minute: (number | null)[];
  note: string | null;
  tip: string | null;
  marks: Mark[];
  lineMarks: Mark[];
  alias: string | null;
  aliasMarks: Mark[];
}

export interface CatalogView {
  groups: CatalogGroup[];
  count: number;
  hidden: number;
  suggestion: string | null;
}

export type ChartEmpty = 'stopped' | 'driver' | 'none';

export interface ChartView {
  max: number;
  step: number;
  from: number;
  to: number;
  readings: (number | null)[];
  warning: number | null;
  critical: number | null;
  warningText: string | null;
  criticalText: string | null;
  colors: ColorStop[];
  stats: string | null;
  summary: string;
  empty: ChartEmpty | null;
  pushed: string | null;
  title: string | null;
  fromText: string;
  toText: string;
  fires: string | null;
  startText: string;
}

export interface ColorStop {
  at: number;
  level: Level;
}

export type Combine = 'max' | 'average' | 'sum';

export type CornerStyle = 'round' | 'soft' | 'square';

export interface DesktopPick {
  overlay: number;
  item: string | null;
  how: SelectHow;
}

export type DeviceKind = 'cpu' | 'gpu' | 'ram' | 'board' | 'ssd' | 'network' | 'battery' | 'system';

export type DriverInstall = 'installed' | 'not-installed' | 'restart-windows';

export type Edit =
  | { kind: 'thermi'; field: string; value: Value }
  | { kind: 'overlay'; overlay: number; field: string; value: Value }
  | { kind: 'style'; overlay: number; field: string; value: Value }
  | { kind: 'item'; item: string; field: string; value: Value }
  | { kind: 'look'; overlay: number | null; name: string }
  | { kind: 'addItem'; overlay: number; sensor: string }
  | { kind: 'removeItem'; item: string }
  | { kind: 'moveItem'; item: string; overlay: number | null; index: number }
  | { kind: 'newOverlay' }
  | { kind: 'duplicateOverlay'; overlay: number }
  | { kind: 'mergeOverlay'; overlay: number; into: number }
  | { kind: 'deleteOverlay'; overlay: number }
  | { kind: 'nudgeOverlay'; overlay: number; dx: number; dy: number }
  | { kind: 'reset' }
  | { kind: 'line'; item: string; line: Line; value: number | null }
  | { kind: 'typedLine'; item: string; line: Line; typed: string }
  | { kind: 'addLine'; item: string; line: Line }
  | { kind: 'resetLines'; item: string }
  | { kind: 'sensorName'; sensor: string; name: string };

export type FanFinderRefusal = 'no-board-fans' | 'driver-may-be-missing';

export interface Findable {
  title: string;
  detail: string;
  aliases: string[];
  words: string[];
}

export type FinderPhase = 'idle' | 'running' | 'done' | 'cancelled';

export interface FinderView {
  phase: FinderPhase;
  pick: string | null;
  progress?: number;
  text?: string;
  detail?: string;
  refusal?: FanFinderRefusal;
}

export interface Found {
  index: number;
  title: Mark[];
  detail: Mark[];
  alias: string | null;
  aliasMarks: Mark[];
}

export type GraphStyle = 'auto' | 'none' | 'bars' | 'line';

export type HotkeyRole = 'toggle' | 'nextStyle' | 'clickThrough' | 'resetMinMax';

export interface Hotkeys {
  toggle?: string;
  nextStyle?: string;
  clickThrough?: string;
  resetMinMax?: string;
}

export interface Item {
  id: string;
  name?: string;
  metric?: string;
  sensor?: string;
  sensors?: string[];
  combine?: Combine;
  auto?: string;
  label?: string;
  own?: boolean;
  tray?: boolean;
  color?: string;
  warnBy?: WarnBy;
  threshold?: Threshold;
  offset?: number;
  stat?: ItemStat;
  smooth?: number;
  decimals?: number;
  bits?: boolean;
  graph?: GraphStyle;
  alertOnly?: boolean;
  notify?: boolean;
}

export type ItemStat = 'now' | 'min' | 'max' | 'average';

export interface ItemView {
  value: string | null;
  level: Level | null;
  type: string;
  auto?: string;
  warnBy: WarnByChoice[];
  warnByName: string | null;
  judged: boolean;
  bits: boolean;
  lit?: number;
  offsetShown?: string;
  offer?: SensorOffer;
  tag?: string;
  label?: string;
}

export interface Language {
  code: string;
  name: string;
}

export type Level = 'normal' | 'warning' | 'critical';

export type Line = 'warning' | 'critical';

export interface Mark {
  from: number;
  to: number;
}

export type MarkState = 'calm' | 'warm' | 'hot' | 'stopped' | 'high-contrast' | 'app';

export interface Monitor {
  id: string;
  primary: boolean;
  bounds: Rect;
  workArea: Rect;
  scale: number;
  taskbar: boolean;
  floats?: TaskbarFloat;
}

export type NameFrom = 'library' | 'likely' | 'own';

export type NoValue = 'reads-nothing' | 'driver-may-be-missing' | 'stopped' | 'resting';

export interface Opacity {
  background?: number;
  overlay?: number;
}

export type Opened = 'settings-file' | 'source' | 'report' | 'notices';

export type Orientation = 'horizontal' | 'vertical';

export interface Overlay {
  size?: number;
  name: string;
  snap?: Snap;
  position?: Position;
  visibility?: Visibility;
  clickThrough?: boolean;
  locked?: boolean;
  oled?: boolean;
  fade?: boolean;
  hidden?: boolean;
  graph?: GraphStyle;
  style?: Style;
  items: Item[];
}

export type PillLayout = 'pills' | 'each' | 'one';

export type PillTheme = 'system' | 'dark' | 'light';

export interface PillView {
  label: string;
  items: string[];
}

export interface Position {
  x: number;
  y: number;
}

export type Problem = 'broken' | 'newer' | 'locked' | 'missing';

export interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export type SelectHow = 'click' | 'double-click' | 'point-in' | 'point-out';

export interface Sensor {
  id: string;
  hardwareType: string;
  hardware: string;
  type: string;
  name: string;
  value: number | null;
}

export type SensorKind = 'temperature' | 'load' | 'clock' | 'power' | 'fan' | 'control' | 'data' | 'throughput' | 'duration' | 'time' | 'address';

export interface SensorOffer {
  first: string[];
  more: string[];
}

export interface SensorView {
  service: Service;
  sensors: Sensor[];
  taken?: boolean;
}

export type Service = 'running' | 'stopped' | 'stale';

export interface Settings {
  version: number;
  hotkeys?: Hotkeys;
  language?: string;
  temperature?: TemperatureUnit;
  windowTheme?: WindowTheme;
  sensorNames?: Record<string, string>;
  overlays: Overlay[];
}

export interface Snap {
  spot: SnapSpot;
  monitor?: string;
  offset?: number;
}

export type SnapSpot = 'taskbar' | 'top-left' | 'top-right' | 'bottom-left' | 'bottom-right';

export interface Style {
  gap?: number;
  units?: boolean;
  name?: string;
  layout?: PillLayout;
  orientation?: Orientation;
  noLabels?: boolean;
  preset?: StylePreset;
  label?: TextStyle;
  value?: TextStyle;
  opacity?: Opacity;
  pills?: PillTheme;
  colors?: StyleColors;
  font?: StyleFont;
  corners?: CornerStyle;
  effect?: TextEffect;
}

export interface StyleColors {
  normal?: string;
  warning?: string;
  critical?: string;
  label?: string;
  value?: string;
}

export type StyleFont = 'segoe' | 'bahnschrift' | 'consolas';

export type StylePreset = 'standard' | 'compact' | 'large' | 'custom';

export type TaskbarFloat = 'side' | 'hides';

export type TemperatureUnit = 'c' | 'f';

export type TextEffect = 'auto' | 'shadow' | 'outline' | 'none';

export interface TextStyle {
  size: number;
  weight: TextWeight;
}

export type TextWeight = 'regular' | 'semibold' | 'bold';

export interface Threshold {
  warning?: number;
  critical?: number;
}

export interface UndoSteps {
  undo: string | null;
  redo: string | null;
}

export interface Undone {
  said: string;
  item: string | null;
  overlay: number | null;
}

export type Visibility = 'always' | 'hide-in-fullscreen' | 'only-in-fullscreen';

export interface WarnBy {
  metric?: string;
  sensor?: string;
  auto?: string;
}

export interface WarnByChoice {
  warnBy?: WarnBy;
  name: string;
}

export interface WindowChange {
  sensors: boolean;
}

export interface WindowReadings {
  view: WindowView;
  status: WindowStatus;
}

export interface WindowStatus {
  overlay: boolean;
  driver: boolean;
  hotkeys: HotkeyRole[];
  failed: HotkeyRole[];
  problem: string | null;
  lightTaskbar: boolean;
}

export type WindowTheme = 'system' | 'light' | 'dark';

export interface WindowView {
  pills: PillView[][];
  items: Record<string, ItemView>;
  values: Record<string, string>;
  mark: MarkState;
}

export interface Bridge {
  settings(): Promise<Settings>;
  problem(): Promise<Problem | null>;
  edit(edit: Edit): Promise<string | null>;
  gesture(active: boolean): Promise<void>;
  view(): Promise<WindowView>;
  chart(id: string): Promise<ChartView | null>;
  exitThermi(): Promise<void>;
  undo(): Promise<Undone | null>;
  redo(): Promise<Undone | null>;
  status(): Promise<WindowStatus>;
  steps(): Promise<UndoSteps>;
  about(): Promise<About>;
  open(what: Opened): Promise<void>;
  pasteLook(overlay: number): Promise<string | null>;
  snapshot(): Promise<SensorView>;
  history(id: string): Promise<(number | null)[]>;
  texts(): Promise<Record<string, string>>;
  monitors(): Promise<Monitor[]>;
  places(): Promise<(Rect | null)[]>;
  report(error: string): Promise<void>;
  looks(): Promise<Record<string, Style>>;
  saveLook(overlay: number, name: string): Promise<string | null>;
  renameLook(name: string, to: string): Promise<string | null>;
  deleteLook(name: string): Promise<string | null>;
  exportLook(overlay: number): Promise<void>;
  exportSavedLook(name: string): Promise<void>;
  importLook(overlay: number, text: string | null): Promise<string | null>;
  catalog(search: string, kinds: SensorKind[], all: boolean): Promise<CatalogView>;
  search(query: string, among: Findable[]): Promise<Found[]>;
  findCpuFan(): Promise<void>;
  cancelFind(): Promise<void>;
  finder(): Promise<FinderView>;
  installDriver(): Promise<DriverInstall>;
  startService(): Promise<boolean>;
  startOverlay(): Promise<void>;
  restore(): Promise<void>;
  startWithWindows(): Promise<boolean>;
  setStartWithWindows(on: boolean): Promise<void>;
  hotkeyHeld(combination: string): Promise<boolean>;
  restHotkeys(rest: boolean): Promise<void>;
  editing(overlay: number | null, selected: string | null, pointed: string | null): Promise<void>;
  start(): Promise<DesktopPick | null>;
  changed(listener: (value: WindowChange) => void): () => void;
  readings(listener: (value: WindowReadings) => void): () => void;
  picked(listener: (value: DesktopPick) => void): () => void;
}

declare global {
  interface Window {
    thermi?: Bridge;
  }
}

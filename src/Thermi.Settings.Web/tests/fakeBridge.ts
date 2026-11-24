import type { BrowserContext, Page } from '@playwright/test';
import type { DesktopPick, Edit, FakeState, Item, Overlay, Settings, Value } from './bridgeTypes';
import { installFakeCatalog } from './fakes/fakeCatalog';
import { installFakeLooks } from './fakes/fakeLooks';
import { fakeState } from './fakes/fakeState';
import { installFakeViews } from './fakes/fakeViews';
import { installFakeWindow } from './fakes/fakeWindow';

export * from './bridgeTypes';
export * from './fakes/fakeState';
export * from './pageChecks';

// The page's tests fake the C# side behind the bridge, from the recorded snapshots, and check what
// the settings hold afterwards: never a text, the DOM's shape, or that a call was made.

// Runs in the page, before its own script, so it may use nothing from this module but its types.
function installFake(initial: FakeState): void {
  const state = initial;
  const listeners: (() => void)[] = [];
  const pickers: ((pick: DesktopPick) => void)[] = [];
  const done: Settings[] = [];
  const undone: Settings[] = [];
  const changed = (): void => listeners.forEach((listener) => listener());

  const put = (target: object, field: string, value: Value): void => {
    const fields = target as Record<string, Value | undefined>;
    if (value === null) {
      delete fields[field];
    } else {
      fields[field] = value;
    }
  };

  const apply = (before: Settings, edit: Edit): Settings | string => {
    const settings = structuredClone(before);
    const overlays = settings.overlays;
    const overlay = (index: number): Overlay => {
      const found = overlays[index];
      if (found === undefined) {
        throw new Error(`The fake has no overlay ${index}.`);
      }
      return found;
    };
    const find = (id: string): { overlay: Overlay; index: number; item: Item } => {
      for (const candidate of overlays) {
        const index = candidate.items.findIndex((item) => item.id === id);
        const item = candidate.items[index];
        if (item !== undefined) {
          return { overlay: candidate, index, item };
        }
      }
      throw new Error(`The fake has no item ${id}.`);
    };
    const full = overlays.length >= 4;

    switch (edit.kind) {
      case 'thermi':
        put(settings, edit.field, edit.value);
        break;
      case 'overlay':
        put(overlay(edit.overlay), edit.field, edit.value);
        break;
      case 'style': {
        const target = overlay(edit.overlay);
        target.style = { ...target.style };
        put(target.style, edit.field, edit.value);
        break;
      }
      case 'item':
        put(find(edit.item).item, edit.field, edit.value);
        break;
      case 'look': {
        const look = state.looks[edit.name];
        if (look === undefined) {
          return 'Refusal.StyleNotFound';
        }
        const targets = edit.overlay === null ? overlays : [overlay(edit.overlay)];
        targets.forEach((target) => {
          target.style = { ...look, name: edit.name, layout: target.style?.layout ?? 'pills', orientation: target.style?.orientation ?? 'horizontal', ...(target.style?.noLabels === true ? { noLabels: true } : {}) };
        });
        break;
      }
      case 'addItem': {
        const sensor = state.sensors.find((candidate) => candidate.id === edit.sensor);
        overlay(edit.overlay).items.push({ id: edit.sensor, name: settings.sensorNames?.[edit.sensor] ?? sensor?.name ?? edit.sensor, sensor: edit.sensor });
        break;
      }
      case 'sensorName':
        window.thermiFakeCatalog?.rename(settings, edit);
        break;
      case 'removeItem': {
        const found = find(edit.item);
        found.overlay.items.splice(found.index, 1);
        break;
      }
      case 'moveItem': {
        if (edit.overlay === null && full) {
          return 'Refusal.TooManyOverlays';
        }
        const found = find(edit.item);
        found.overlay.items.splice(found.index, 1);
        const target = edit.overlay === null ? { name: `Overlay ${overlays.length + 1}`, items: [] } : overlay(edit.overlay);
        if (edit.overlay === null) {
          overlays.push(target);
        }
        target.items.splice(edit.index, 0, found.item);
        settings.overlays = overlays.filter((candidate) => candidate.items.length > 0);
        break;
      }
      case 'newOverlay':
        if (full) {
          return 'Refusal.TooManyOverlays';
        }
        overlays.push({ ...structuredClone(state.defaultOverlay), name: `Overlay ${overlays.length + 1}`, items: [] });
        break;
      case 'duplicateOverlay': {
        if (full) {
          return 'Refusal.TooManyOverlays';
        }
        const copy = structuredClone(overlay(edit.overlay));
        copy.name = `${copy.name} 2`;
        copy.items = copy.items.map((item) => ({ ...item, id: `${item.id}-2` }));
        overlays.splice(edit.overlay + 1, 0, copy);
        break;
      }
      case 'mergeOverlay':
        overlay(edit.into).items.push(...overlay(edit.overlay).items);
        overlays.splice(edit.overlay, 1);
        break;
      case 'deleteOverlay':
        if (overlays.length === 1) {
          return 'Refusal.LastOverlay';
        }
        overlays.splice(edit.overlay, 1);
        break;
      case 'reset':
        settings.overlays = [structuredClone(state.defaultOverlay)];
        break;
      case 'nudgeOverlay': {
        // A snapped overlay stays on its snap, as Core's nudge leaves it.
        const target = overlay(edit.overlay);
        if (target.position !== undefined && target.snap === undefined) {
          target.position = { x: target.position.x + edit.dx, y: target.position.y + edit.dy };
        }
        break;
      }
      default: {
        const refused = window.thermiFakeViews?.apply(settings, edit);
        if (refused !== null && refused !== undefined) {
          return refused;
        }
      }
    }
    return settings;
  };

  // The editor's grouping: a drag is one step, and so are repeated changes of one target within a second.
  let gesture: 'off' | 'started' | 'open' = 'off';
  let last: { target: string; at: number } | null = null;
  const targetOf = (edit: Edit): string => JSON.stringify([edit.kind, 'item' in edit ? edit.item : null, 'field' in edit ? edit.field : null]);
  const joins = (edit: Edit): boolean => {
    const target = targetOf(edit);
    const now = Date.now();
    const joined = gesture === 'open' || (gesture === 'off' && last !== null && last.target === target && now - last.at < 1000);
    last = gesture === 'off' ? { target, at: now } : null;
    gesture = gesture === 'started' ? 'open' : gesture;
    return joined && done.length > 0;
  };

  const write = (next: Settings, history: Settings[]): void => {
    history.push(state.settings);
    state.settings = next;
    changed();
  };

  window.thermiFake = {
    state,
    set(change) {
      Object.assign(state, change);
      changed();
    },
    pick: (pick) => pickers.forEach((picker) => picker(pick)),
    history: () => ({ done: state.problem === null ? done.length : 0, undone: state.problem === null ? undone.length : 0 }),
  };

  // The views, the looks, the catalog, the finder and the window's status are each installed apart, after this.
  const notInstalled = async (): Promise<never> => Promise.reject(new Error('A part of the fake is not installed.'));
  window.thermi = {
    settings: async () => structuredClone(state.settings),
    problem: async () => state.problem,
    edit: async (edit: Edit) => {
      if (state.problem !== null) {
        return 'Refusal.SettingsBroken';
      }
      const next = apply(state.settings, edit);
      if (typeof next === 'string') {
        return next;
      }
      undone.length = 0;
      if (joins(edit)) {
        state.settings = next;
        changed();
      } else {
        write(next, done);
      }
      return null;
    },
    gesture: async (active) => {
      gesture = active ? 'started' : 'off';
      last = null;
    },
    view: notInstalled,
    chart: async () => null,
    exitThermi: async () => undefined,
    undo: async () => {
      last = null;
      const previous = done.pop();
      if (previous !== undefined && state.problem === null) {
        write(previous, undone);
      }
      return null;
    },
    redo: async () => {
      last = null;
      const next = undone.pop();
      if (next !== undefined && state.problem === null) {
        write(next, done);
      }
      return null;
    },
    status: notInstalled,
    steps: notInstalled,
    about: notInstalled,
    open: notInstalled,
    pasteLook: notInstalled,
    snapshot: async () => ({ service: state.service, sensors: state.service === 'stopped' ? [] : state.sensors.map((sensor) => ({ ...sensor, name: state.settings.sensorNames?.[sensor.id] ?? sensor.name })) }),
    history: async (id) => {
      const value = state.service === 'running' ? (state.sensors.find((sensor) => sensor.id === id)?.value ?? null) : null;
      return Array.from({ length: 600 }, () => value);
    },
    texts: async () => state.texts,
    monitors: async () => state.monitors,
    report: async () => undefined,
    crowded: async () =>
      state.settings.overlays.flatMap((one, overlay) => (state.crowdedWithoutLabels.includes(overlay) || (state.crowded.includes(overlay) && one.style?.noLabels !== true) ? [overlay] : [])),
    looks: async () => state.looks,
    saveLook: notInstalled,
    renameLook: notInstalled,
    deleteLook: notInstalled,
    exportLook: notInstalled,
    exportSavedLook: notInstalled,
    importLook: notInstalled,
    catalog: notInstalled,
    findCpuFan: notInstalled,
    cancelFind: notInstalled,
    finder: async () => state.finder,
    installDriver: async () => {
      if (state.driverAnswer === 'failed') {
        throw new Error('The install failed.');
      }
      if (state.driverAnswer === 'refused') {
        return false;
      }
      state.sensors = state.sensors.map((sensor) => (sensor.hardwareType === 'Cpu' && sensor.value === 0 ? { ...sensor, value: 50 } : sensor));
      changed();
      return true;
    },
    startService: async () => {
      if (state.serviceAnswer === 'refused') {
        return false;
      }
      state.service = 'running';
      changed();
      return true;
    },
    startOverlay: async () => {
      state.overlayRunning = true;
      changed();
    },
    restore: async () => {
      state.problem = null;
      changed();
    },
    startWithWindows: async () => state.startWithWindows,
    setStartWithWindows: async (on) => {
      state.startWithWindows = on;
      changed();
    },
    hotkeyHeld: async (combination) => {
      if (state.failingHotkeys.includes(combination)) {
        throw new Error('The check failed.');
      }
      return state.heldHotkeys.includes(combination);
    },
    restHotkeys: async (rest) => {
      state.hotkeysResting = rest;
      changed();
    },
    changed: (listener) => {
      listeners.push(listener);
    },
    // The real overlays are the fake's: what the page tells them it edits is kept, and a test picks on them.
    editing: async (overlay, selected, pointed) => {
      state.editing = { overlay, selected, pointed };
    },
    picked: (listener) => {
      pickers.push(listener);
    },
    start: async () => state.start,
  };
}

/** Opens the page on a fake of the C# side holding the given state. */
export async function openWindow(page: Page, state: FakeState = fakeState()): Promise<void> {
  await page.addInitScript(installFake, state);
  await page.addInitScript(installFakeViews);
  await page.addInitScript(installFakeLooks);
  await page.addInitScript(installFakeCatalog);
  await page.addInitScript(installFakeWindow);
  await page.goto('/');
}

/** A page of its own on the given state, for a table whose rows each start apart. */
export async function windowWith(context: BrowserContext, state: FakeState): Promise<Page> {
  const page = await context.newPage();
  await openWindow(page, state);
  return page;
}

/** What the fake holds now: the settings the page's edits left, and the rest of the C# side. */
export async function fakeOf(page: Page): Promise<FakeState> {
  return page.evaluate(() => {
    if (window.thermiFake === undefined) {
      throw new Error('The fake bridge is not installed.');
    }
    return window.thermiFake.state;
  });
}

export async function settingsOf(page: Page): Promise<Settings> {
  return (await fakeOf(page)).settings;
}

/** Moves the fake's state on, as the C# side would: the service stops, readings change, the finder ends. */
export async function setFake(page: Page, change: Partial<FakeState>): Promise<void> {
  await page.evaluate((next) => window.thermiFake?.set(next), change);
}

export function itemOf(settings: Settings, id: string): Item {
  const item = settings.overlays.flatMap((overlay) => overlay.items).find((candidate) => candidate.id === id);
  if (item === undefined) {
    throw new Error(`The settings have no item ${id}.`);
  }
  return item;
}

export function overlayOf(settings: Settings, index: number): Overlay {
  const overlay = settings.overlays[index];
  if (overlay === undefined) {
    throw new Error(`The settings have no overlay ${index}.`);
  }
  return overlay;
}

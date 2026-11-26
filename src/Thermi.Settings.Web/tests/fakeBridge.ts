import type { BrowserContext, Page } from '@playwright/test';
import type { Bridge, DesktopPick, Edit, FakeState, Item, OracleCall, Overlay, Settings, Undone, UndoSteps, WindowReadings } from './bridgeTypes';
import { installFakeCatalog } from './fakes/fakeCatalog';
import { installFakeLooks } from './fakes/fakeLooks';
import { installFakePictures } from './fakes/fakePictures';
import { fakeState } from './fakes/fakeState';
import { installFakeViews } from './fakes/fakeViews';
import { installFakeWindow } from './fakes/fakeWindow';
import { askOracle } from './oracle';
import { settled } from './pageChecks';

export { expect } from '@playwright/test';
export * from './bridgeTypes';
export * from './fakes/fakeState';
export { test } from './oracle';
export * from './pageChecks';

// The page's tests fake the C# side behind the bridge, from the recorded snapshots, and check what
// the settings hold afterwards: never a text, the DOM's shape, or that a call was made.

// Runs in the page, before its own script, so it may use nothing from this module but its types. What the C# side
// decides, each edit, undo, redo, gesture and saved look, the fake never decides: it asks the oracle for the real
// bridge's answer to the same call from the same start, and takes the settings, looks and undo steps it then showed.
function installFake(initial: FakeState): void {
  const state = initial;
  const listeners: ((change: { sensors: boolean }) => void)[] = [];
  const readingListeners: ((readings: WindowReadings) => void)[] = [];
  const pickers: ((pick: DesktopPick) => void)[] = [];
  // A listener, kept until the page takes it back, as the host's script keeps it.
  const listen = <T>(list: T[], listener: T): (() => void) => {
    list.push(listener);
    return () => {
      list.splice(list.indexOf(listener), list.includes(listener) ? 1 : 0);
    };
  };
  let steps: UndoSteps = { undo: null, redo: null };
  // The settings' changes, and the last of them the page has read, none before its first read: a test reads the
  // settings only once the page has.
  let changes = 0;
  let read = -1;
  // The answers on their way from the oracle, a round trip to the test that a busy PC makes long: until they come, the
  // page still shows what it showed before the call.
  let answering = 0;
  const answered = async <T>(answer: Promise<T> | undefined): Promise<T | undefined> => {
    answering += 1;
    try {
      return await answer;
    } finally {
      answering -= 1;
    }
  };
  // As C# tells it, with whether the sensors or the service may have changed.
  const changed = (sensors = false): void => {
    changes += 1;
    listeners.forEach((listener) => listener({ sensors }));
  };

  // As the real bridge's wire, a missing argument goes as null; a call the oracle lacks rejects, as one C# fails.
  const ask = async (method: OracleCall['method'], ...args: unknown[]): Promise<unknown> => {
    const answer = await answered(window.thermiOracle?.({ method, args: args.map((arg) => arg ?? null) }));
    if (answer === undefined || answer === null || 'missing' in answer) {
      throw new Error(`The oracle has no answer: ${answer?.missing ?? 'it is not installed'}.`);
    }
    state.settings = answer.settings ?? state.settings;
    state.looks = answer.looks ?? state.looks;
    state.worn = answer.worn ?? state.worn;
    steps = answer.steps;
    changed();
    if (answer.error !== undefined) {
      throw new Error(answer.error);
    }
    return answer.result ?? null;
  };

  window.thermiFake = {
    state,
    // New values of the same sensors are a second's readings, as C# raises them; anything else is a change.
    set(change) {
      const ids = (sensors: FakeState['sensors']): string => sensors.map((sensor) => sensor.id).join('\n');
      const readings = Object.keys(change).every((key) => key === 'sensors') && change.sensors !== undefined && ids(change.sensors) === ids(state.sensors);
      Object.assign(state, change);
      if (readings) {
        // With the view and the status they give, as C# sends them.
        const bridge = window.thermi;
        void Promise.all([bridge?.view(), bridge?.status()]).then(([view, status]) => {
          if (view !== undefined && status !== undefined) {
            readingListeners.forEach((listener) => listener({ view, status }));
          }
        });
      } else {
        changed('sensors' in change || 'service' in change);
      }
    },
    pick: (pick) => pickers.forEach((picker) => picker(pick)),
    answered,
    caughtUp: () => answering === 0 && read === changes,
  };

  // The views, the look files, the pictures, the catalog, the finder and the window's status are each installed apart, after this.
  const notInstalled = async (): Promise<never> => Promise.reject(new Error('A part of the fake is not installed.'));
  window.thermi = {
    settings: async () => {
      read = changes;
      return structuredClone(state.settings);
    },
    problem: async () => state.problem,
    edit: async (edit: Edit) => (await ask('edit', edit)) as string | null,
    gesture: async (active) => {
      await ask('gesture', active);
    },
    view: notInstalled,
    preview: notInstalled,
    lookSamples: notInstalled,
    chart: async () => null,
    exitThermi: async () => undefined,
    undo: async () => (await ask('undo')) as Undone | null,
    redo: async () => (await ask('redo')) as Undone | null,
    status: notInstalled,
    steps: async () => steps,
    about: notInstalled,
    checkUpdates: async () => undefined,
    restartToUpdate: async () => undefined,
    open: notInstalled,
    agents: notInstalled,
    copy: notInstalled,
    // Windows' clipboard, held by another app, fails the paste before C# reads any look.
    pasteLook: async (overlay) => (state.clipboard === null ? 'Refusal.PasteFailed' : ((await ask('importLook', overlay, state.clipboard)) as string | null)),
    snapshot: async () => ({
      service: state.service,
      sensors:
        state.service === 'stopped'
          ? []
          : state.sensors.map((sensor) => ({ ...sensor, name: state.settings.sensorNames?.[sensor.id] ?? sensor.name })),
    }),
    history: async (id) => {
      const value = state.service === 'running' ? (state.sensors.find((sensor) => sensor.id === id)?.value ?? null) : null;
      return Array.from({ length: 600 }, () => value);
    },
    texts: async () => state.texts,
    monitors: async () => state.monitors,
    places: async () => state.places,
    report: async () => undefined,
    looks: async () => state.looks,
    worn: async () => state.worn,
    saveLook: async (overlay, name) => (await ask('saveLook', overlay, name)) as string | null,
    renameLook: async (name, to) => (await ask('renameLook', name, to)) as string | null,
    deleteLook: async (name) => (await ask('deleteLook', name)) as string | null,
    exportLook: notInstalled,
    exportSavedLook: notInstalled,
    // Windows' file dialog, cancelled, gives C# no look to read.
    importLook: async (overlay, text) => {
      const picked = text ?? state.importFile;
      return picked === null ? null : ((await ask('importLook', overlay, picked)) as string | null);
    },
    catalog: notInstalled,
    // What a search finds and marks is C#'s, so the fake asks the oracle, as for an edit; it changes nothing.
    search: async (query, among) => {
      const answer = await answered(window.thermiPure?.({ method: 'search', args: [query, among] }));
      if (answer === undefined || 'missing' in answer) {
        throw new Error(`The oracle has no answer: ${answer?.missing ?? 'it is not installed'}.`);
      }
      return answer.result as Awaited<ReturnType<Bridge['search']>>;
    },
    findCpuFan: notInstalled,
    cancelFind: notInstalled,
    finder: async () => state.finder,
    installDriver: async () => {
      if (state.driverAnswer === 'failed') {
        throw new Error('The install failed.');
      }
      if (state.driverAnswer === 'refused') {
        return 'not-installed';
      }
      if (state.driverAnswer === 'restart') {
        return 'restart-windows';
      }
      state.sensors = state.sensors.map((sensor) => (sensor.hardwareType === 'Cpu' && sensor.value === 0 ? { ...sensor, value: 50 } : sensor));
      changed(true);
      return 'installed';
    },
    startService: async () => {
      if (state.serviceAnswer === 'refused') {
        return false;
      }
      state.service = 'running';
      changed(true);
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
    changed: (listener) => listen(listeners, listener),
    readings: (listener) => listen(readingListeners, listener),
    // The real overlays are the fake's: what the page tells them it edits is kept, and a test picks on them.
    editing: async (overlay, selected, pointed) => {
      state.editing = { overlay, selected, pointed };
    },
    picked: (listener) => listen(pickers, listener),
    start: async () => state.start,
  };
}

// Runs in the page after every other part of the fake. While the state holds replies late, each read is answered with
// what the C# side held when it was asked, as a busy bridge answers, and the answers wait for the test to let them go.
function installLateReplies(): void {
  const fake = window.thermiFake;
  const bridge = window.thermi as unknown as Record<string, (...args: unknown[]) => Promise<unknown>> | undefined;
  if (fake === undefined || bridge === undefined) {
    throw new Error('The fake bridge is not installed.');
  }
  const held: (() => void)[] = [];
  for (const method of ['settings', 'view', 'snapshot', 'texts', 'status', 'steps', 'problem', 'startWithWindows', 'chart', 'catalog']) {
    const answer = bridge[method];
    if (answer === undefined) {
      throw new Error(`The fake has no ${method}.`);
    }
    bridge[method] = async (...args: unknown[]) => {
      const now = await answer(...args);
      return fake.state.lateReplies ? new Promise((resolve) => held.push(() => resolve(now))) : now;
    };
  }
  fake.release = () => {
    fake.state.lateReplies = false;
    held.splice(0).reverse().forEach((answer) => answer());
  };
}

/** Opens the page on a fake of the C# side holding the given state, its settings as C# shows that file. */
export async function openWindow(page: Page, state: FakeState = fakeState()): Promise<void> {
  await page.addInitScript(installFake, await askOracle(page, state));
  await page.addInitScript(installFakeViews);
  await page.addInitScript(installFakeLooks);
  await page.addInitScript(installFakePictures);
  await page.addInitScript(installFakeCatalog);
  await page.addInitScript(installFakeWindow);
  await page.addInitScript(installLateReplies);
  await page.goto('/');
  // A person's first key comes once the window shows its settings, not while the page is still starting.
  await page.waitForFunction(() => window.thermiFake?.caughtUp() === true, undefined, { timeout: 10_000 });
  await page.evaluate(settled);
}

/** Lets the answers a late fake holds go, the newest first. */
export async function releaseReplies(page: Page): Promise<void> {
  await page.evaluate(() => window.thermiFake?.release?.());
}

/** A page of its own on the given state, for a table whose rows each start apart. */
export async function windowWith(context: BrowserContext, state: FakeState): Promise<Page> {
  const page = await context.newPage();
  await openWindow(page, state);
  return page;
}

/**
 * What the fake holds now: the settings the page's edits left, and the rest of the C# side, once the page has read
 * them and drawn what it read, so the test's next key lands on the page as it shows them, as a person's does.
 */
export async function fakeOf(page: Page): Promise<FakeState> {
  await page.waitForFunction(() => window.thermiFake?.caughtUp() !== false, undefined, { timeout: 5000 });
  await page.evaluate(settled);
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

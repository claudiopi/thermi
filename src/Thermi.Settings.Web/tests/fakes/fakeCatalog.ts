import type { CatalogFound, CatalogGroup, CatalogRow, CatalogView, DeviceKind, Mark, NoValue, Sensor, SensorKind } from '../bridgeTypes';

// The C# side's catalog and CPU fan finder, as the fake gives them. Core decides both for real and tests them; the
// fake follows the same rules in rough form, only so the page has something true to show. What a search finds, in what
// order and with what marked, is Core's answer, and a sensor's rename the real bridge's, which the fake replays. It runs
// in the page after the fake and its views, so it may use nothing but types.
export function installFakeCatalog(): void {
  const fake = window.thermiFake;
  const bridge = window.thermi;
  if (fake === undefined || bridge === undefined) {
    throw new Error('The fake bridge is not installed.');
  }
  const state = fake.state;
  const say = (key: string): string => state.texts[key] ?? key;
  const changed = (): void => fake.set({});

  const kinds: SensorKind[] = [
    'temperature',
    'load',
    'clock',
    'power',
    'fan',
    'control',
    'data',
    'throughput',
    'duration',
    'frame-rate',
    'frame-time',
  ];
  const libraryTypes: Record<string, SensorKind> = { Level: 'load', TimeSpan: 'duration' };
  const devices: Record<string, DeviceKind> = {
    Cpu: 'cpu',
    GpuNvidia: 'gpu',
    GpuAmd: 'gpu',
    GpuIntel: 'gpu',
    Memory: 'ram',
    Motherboard: 'board',
    SuperIO: 'board',
    Storage: 'ssd',
    Network: 'network',
    Battery: 'battery',
    Game: 'game',
  };
  // The library's type as the bridge's kind, FrameRate as frame-rate.
  const capital = (kind: string): string => kind.replace(/(^|-)(\w)/g, (_, _dash: string, letter: string) => letter.toUpperCase());
  const kindOf = (sensor: Sensor): SensorKind | undefined => libraryTypes[sensor.type] ?? kinds.find((kind) => capital(kind) === sensor.type);
  const deviceOf = (sensor: Sensor): string => sensor.id.split('/').slice(0, -2).join('/');
  const why = (sensor: Sensor, kind: SensorKind): NoValue | null => {
    if (sensor.hardwareType === 'Cpu' && sensor.value === 0 && ['temperature', 'power', 'clock'].includes(kind)) {
      return 'driver-may-be-missing';
    }
    if (sensor.value === null) {
      return sensor.hardwareType === 'Game' ? 'no-frames' : 'reads-nothing';
    }
    if (kind !== 'fan' || sensor.value !== 0) {
      return null;
    }
    return devices[sensor.hardwareType] === 'gpu' ? 'resting' : 'stopped';
  };
  // A minute around the value now, the same on every run so a screenshot compares.
  const minute = (value: number | null): (number | null)[] =>
    Array.from({ length: 60 }, (_, index) => (value === null || value === 0 ? value : value * (1 + 0.06 * Math.sin(index / 4) + 0.03 * Math.cos(index / 1.7))));

  // Gigabyte's ITE wiring, the user's names, and the rows nobody would add, as Core has them.
  const likely: Record<string, string> = {
    'Temperature #1': 'System',
    'Temperature #2': 'Chipset',
    'Temperature #3': 'Cpu',
    'Temperature #4': 'PcieSlot',
    'Temperature #5': 'Vrm',
    'Fan #1': 'CpuFan',
    'Fan #2': 'SystemFan1',
    'Fan #3': 'SystemFan2',
  };
  const nameOf = (sensor: Sensor, id: string): Pick<CatalogRow, 'name' | 'from' | 'note' | 'tip'> => {
    const own = state.settings.sensorNames?.[id];
    const probable = sensor.hardwareType === 'SuperIO' && /IT86(86|88|89|96)E$/.test(sensor.hardware) ? likely[sensor.name] : undefined;
    if (own !== undefined) {
      return { name: own, from: 'own', note: sensor.name, tip: null };
    }
    const input = sensor.hardwareType === 'SuperIO' ? /#(\d+)$/.exec(sensor.name)?.[1] : undefined;
    return probable === undefined
      ? {
          name: sensor.name,
          from: 'library',
          note: input === undefined ? null : say('CatalogText.InputNote').replace('{0}', sensor.hardware).replace('{1}', input),
          tip: null,
        }
      : {
          name: say(`LikelyName.${probable}`),
          from: 'likely',
          note: say('CatalogText.LikelyNote').replace('{0}', sensor.name),
          tip: say('CatalogText.LikelyTip')
            .replace('{0}', sensor.hardware)
            .replace('{1}', sensor.name)
            .replace('{2}', say(`LikelyName.${probable}`)),
        };
  };
  const hides = (sensor: Sensor, row: CatalogRow): boolean =>
    row.why === 'reads-nothing' || row.why === 'stopped' || (devices[sensor.hardwareType] === 'gpu' && sensor.name.startsWith('D3D '));

  // Every row of the types chosen, by device as the service lists them and by type within one, as Core builds them.
  type Listed = { sensor: Sensor; row: CatalogRow };
  const rowsOf = (values: Record<string, string>, chosen: SensorKind[]): Listed[] => {
    const rows: Listed[] = [];
    // The library repeats a few ids; Core numbers the repeats, as "#2", so each row has its own.
    const seen = new Map<string, number>();
    for (const sensor of state.service === 'stopped' ? [] : state.sensors) {
      const repeat = (seen.get(sensor.id) ?? 0) + 1;
      seen.set(sensor.id, repeat);
      const kind = kindOf(sensor);
      if (kind === undefined || (chosen.length > 0 && !chosen.includes(kind))) {
        continue;
      }
      const id = repeat === 1 ? sensor.id : `${sensor.id}#${repeat}`;
      const row: CatalogRow = {
        id,
        ...nameOf(sensor, id),
        kind,
        value: values[sensor.id] ?? null,
        why: why(sensor, kind),
        minute: minute(sensor.value),
        marks: [],
        lineMarks: [],
        alias: null,
        aliasMarks: [],
      };
      rows.push({ sensor, row });
    }
    const devicesInOrder = [...new Set(rows.map(({ sensor }) => deviceOf(sensor)))];
    const place = (one: Listed): number => devicesInOrder.indexOf(deviceOf(one.sensor)) * 100 + kinds.indexOf(one.row.kind);
    return rows.map((one, index) => ({ one, index })).sort((a, b) => place(a.one) - place(b.one) || a.index - b.index).map(({ one }) => one);
  };

  // Which rows a search finds, in what order and with what marked, is Core's answer from the same sensors, language and
  // names, which the oracle keeps.
  const searched = async (search: string, chosen: SensorKind[], rows: Listed[]): Promise<{ listed: Listed[]; marks: Map<string, Mark[]> }> => {
    const answer = await window.thermiFake?.answered(
      window.thermiPure?.({ method: 'catalogSearch', args: [search, chosen, state.sensors, state.texts, state.settings.sensorNames ?? null] }),
    );
    if (answer === undefined || 'missing' in answer) {
      throw new Error(`The oracle has no answer: ${answer?.missing ?? 'it is not installed'}.`);
    }
    const found = answer.result as CatalogFound[];
    const listed = found.flatMap((device) =>
      device.sensors.flatMap((one) => {
        const at = rows.find((each) => each.row.id === one.id);
        return at === undefined
          ? []
          : [{ ...at, row: { ...at.row, marks: one.marks, lineMarks: one.lineMarks, alias: one.alias ?? null, aliasMarks: one.aliasMarks } }];
      }),
    );
    return { listed, marks: new Map(found.map((device) => [device.id, device.marks])) };
  };

  bridge.catalog = async (search: string, chosen: SensorKind[], all: boolean): Promise<CatalogView> => {
    const rows = rowsOf((await bridge.view()).values, chosen);
    const { listed, marks } = search.trim() === '' ? { listed: rows, marks: new Map<string, Mark[]>() } : await searched(search, chosen, rows);
    const groups: CatalogGroup[] = [];
    let hidden = 0;
    for (const { sensor, row } of listed) {
      if (!all && hides(sensor, row)) {
        hidden++;
        continue;
      }
      const device = deviceOf(sensor);
      const group = groups.find((one) => one.id === device) ?? {
        id: device,
        name: sensor.hardware,
        kind: devices[sensor.hardwareType] ?? null,
        sensors: [],
        marks: marks.get(device) ?? [],
      };
      if (!groups.includes(group)) {
        groups.push(group);
      }
      group.sensors.push(row);
    }
    const count = groups.reduce((sum, group) => sum + group.sensors.length, 0);
    return { groups, count, hidden, suggestion: count + hidden === 0 && search.trim() !== '' ? 'gpu' : null };
  };

  // The finder runs until a test moves it on, as the C# side would after up to 30 s of load.
  bridge.findCpuFan = async () => {
    state.finder = { phase: 'running', pick: null, progress: 0.2, text: say('CatalogText.FanFinderWaiting') };
    changed();
  };
  bridge.cancelFind = async () => {
    state.finder = { phase: state.finder.phase === 'running' ? 'cancelled' : 'idle', pick: null };
    changed();
  };
}

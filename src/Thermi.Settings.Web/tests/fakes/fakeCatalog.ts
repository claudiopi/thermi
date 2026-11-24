import type { CatalogGroup, CatalogRow, CatalogView, DeviceKind, NoValue, Sensor, SensorKind, SensorNameEdit, Settings } from '../bridgeTypes';

// The C# side's catalog and CPU fan finder, as the fake gives them. Core decides both for real and tests them; the
// fake follows the same rules in rough form, only so the page has something true to show. It runs in the page after the
// fake and its views, so it may use nothing but types.
export function installFakeCatalog(): void {
  const fake = window.thermiFake;
  const bridge = window.thermi;
  if (fake === undefined || bridge === undefined) {
    throw new Error('The fake bridge is not installed.');
  }
  const state = fake.state;
  const say = (key: string): string => state.texts[key] ?? key;
  const changed = (): void => fake.set({});

  const kinds: SensorKind[] = ['temperature', 'load', 'clock', 'power', 'fan', 'control', 'data', 'throughput', 'duration'];
  const libraryTypes: Record<string, SensorKind> = { Level: 'load', TimeSpan: 'duration' };
  const devices: Record<string, DeviceKind> = { Cpu: 'cpu', GpuNvidia: 'gpu', GpuAmd: 'gpu', GpuIntel: 'gpu', Memory: 'ram', Motherboard: 'board', SuperIO: 'board', Storage: 'ssd', Network: 'network', Battery: 'battery' };
  const capital = (word: string): string => word.charAt(0).toUpperCase() + word.slice(1);
  const kindOf = (sensor: Sensor): SensorKind | undefined => libraryTypes[sensor.type] ?? kinds.find((kind) => capital(kind) === sensor.type);
  const deviceOf = (sensor: Sensor): string => sensor.id.split('/').slice(0, -2).join('/');
  const why = (sensor: Sensor, kind: SensorKind): NoValue | null => {
    if (sensor.hardwareType === 'Cpu' && sensor.value === 0 && ['temperature', 'power', 'clock'].includes(kind)) {
      return 'driver-may-be-missing';
    }
    return sensor.value === null ? 'reads-nothing' : kind === 'fan' && sensor.value === 0 ? 'stopped' : null;
  };
  // A minute around the value now, the same on every run so a screenshot compares.
  const minute = (value: number | null): (number | null)[] =>
    Array.from({ length: 60 }, (_, index) => (value === null || value === 0 ? value : value * (1 + 0.06 * Math.sin(index / 4) + 0.03 * Math.cos(index / 1.7))));

  const matches = (sensor: Sensor, kind: SensorKind, word: string): boolean => {
    const device = devices[sensor.hardwareType];
    const synonyms = [say(`Synonyms.${capital(kind)}`), device === undefined ? '' : say(`Synonyms.${capital(device)}`)].join(' ').split(' ');
    const said = [sensor.name, sensor.hardware, sensor.type, say(`SensorKind.${capital(kind)}`)];
    return said.some((one) => one.toLowerCase().includes(word)) || synonyms.some((synonym) => synonym !== '' && synonym.startsWith(word));
  };

  // Gigabyte's ITE wiring, the user's names, and the rows nobody would add, as Core has them.
  const likely: Record<string, string> = { 'Temperature #1': 'System', 'Temperature #2': 'Chipset', 'Temperature #3': 'Cpu', 'Temperature #4': 'PcieSlot', 'Temperature #5': 'Vrm', 'Fan #1': 'CpuFan', 'Fan #2': 'SystemFan1', 'Fan #3': 'SystemFan2' };
  const nameOf = (sensor: Sensor, id: string): Pick<CatalogRow, 'name' | 'from' | 'note' | 'tip'> => {
    const own = state.settings.sensorNames?.[id];
    const probable = sensor.hardwareType === 'SuperIO' && /IT86(86|88|89|96)E$/.test(sensor.hardware) ? likely[sensor.name] : undefined;
    if (own !== undefined) {
      return { name: own, from: 'own', note: sensor.name, tip: null };
    }
    const input = sensor.hardwareType === 'SuperIO' ? /#(\d+)$/.exec(sensor.name)?.[1] : undefined;
    return probable === undefined
      ? { name: sensor.name, from: 'library', note: input === undefined ? null : say('CatalogText.InputNote').replace('{0}', sensor.hardware).replace('{1}', input), tip: null }
      : { name: say(`LikelyName.${probable}`), from: 'likely', note: say('CatalogText.LikelyNote').replace('{0}', sensor.name), tip: say('CatalogText.LikelyTip').replace('{0}', sensor.hardware).replace('{1}', sensor.name).replace('{2}', say(`LikelyName.${probable}`)) };
  };
  const hides = (sensor: Sensor, row: CatalogRow): boolean => row.why === 'reads-nothing' || row.why === 'stopped' || (devices[sensor.hardwareType] === 'gpu' && sensor.name.startsWith('D3D '));

  bridge.catalog = async (search: string, chosen: SensorKind[], all: boolean): Promise<CatalogView> => {
    const values = (await bridge.view()).values;
    const words = search.toLowerCase().split(' ').filter((word) => word !== '');
    const groups: CatalogGroup[] = [];
    const sensors = state.service === 'stopped' ? [] : state.sensors;
    let hidden = 0;
    // The library repeats a few ids; Core numbers the repeats, as "#2", so each row has its own.
    const seen = new Map<string, number>();
    for (const sensor of sensors) {
      const repeat = (seen.get(sensor.id) ?? 0) + 1;
      seen.set(sensor.id, repeat);
      const kind = kindOf(sensor);
      const id = repeat === 1 ? sensor.id : `${sensor.id}#${repeat}`;
      const named = nameOf(sensor, id);
      const fits = kind !== undefined && (chosen.length === 0 || chosen.includes(kind));
      if (kind === undefined || !fits || !words.every((word) => matches(sensor, kind, word) || named.name.toLowerCase().includes(word))) {
        continue;
      }
      const row: CatalogRow = { id, ...named, kind, value: values[sensor.id] ?? null, why: why(sensor, kind), minute: minute(sensor.value) };
      if (!all && hides(sensor, row)) {
        hidden++;
        continue;
      }
      const device = deviceOf(sensor);
      let group = groups.find((one) => one.id === device);
      if (group === undefined) {
        group = { id: device, name: sensor.hardware, kind: devices[sensor.hardwareType] ?? null, sensors: [] };
        groups.push(group);
      }
      group.sensors.push(row);
    }
    groups.forEach((group) => group.sensors.sort((one, other) => kinds.indexOf(one.kind) - kinds.indexOf(other.kind)));
    const count = groups.reduce((sum, group) => sum + group.sensors.length, 0);
    return { groups, count, hidden, suggestion: count + hidden === 0 && words.length > 0 ? 'gpu' : null };
  };

  // Core's rule in rough form: the items still named after the sensor follow it, and empty gives the library's back.
  window.thermiFakeCatalog = {
    rename(settings: Settings, edit: SensorNameEdit): void {
      const library = state.sensors.find((candidate) => candidate.id === edit.sensor)?.name ?? edit.sensor;
      const [was, now] = [settings.sensorNames?.[edit.sensor] ?? library, edit.name.trim() === '' ? library : edit.name.trim()];
      const others = Object.fromEntries(Object.entries(settings.sensorNames ?? {}).filter(([id]) => id !== edit.sensor));
      settings.sensorNames = now === library ? others : { ...others, [edit.sensor]: now };
      const named = settings.overlays.flatMap((overlay) => overlay.items).filter((item) => item.sensor === edit.sensor && item.metric === undefined && item.auto === undefined && item.name === was);
      named.forEach((item) => (item.name = now));
    },
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

import type { ChartColor, ChartView, Item, ItemView, MarkState, Meter, MeterChoice, PillView, Sensor, Threshold, WindowView } from '../bridgeTypes';

// The C# side's views of the settings and the readings, as the fake gives them: the pills, each item's value and
// level, and its chart. Core decides these for real and tests them; the fake follows the same rules in rough form,
// only so the page has something true to show. It runs in the page after the fake, so it may use nothing but types.
export function installFakeViews(): void {
  const fake = window.thermiFake;
  const bridge = window.thermi;
  if (fake === undefined || bridge === undefined) {
    throw new Error('The fake bridge is not installed.');
  }
  const state = fake.state;
  const say = (key: string, ...blanks: string[]): string =>
    (state.texts[key] ?? key).replace(/\{(\d+)\}/g, (blank, index: string) => blanks[Number(index)] ?? blank);

  // The sensors each default metric picks on the recorded PC.
  const metricSensors: Record<string, string> = {
    'cpu.temperature': '/amdcpu/0/temperature/2',
    'cpu.load': '/amdcpu/0/load/0',
    'cpu.power': '/amdcpu/0/power/0',
    'cpu.clock': '/amdcpu/0/clock/1',
    'gpu.temperature': '/gpu-nvidia/0/temperature/0',
    'gpu.load': '/gpu-nvidia/0/load/0',
    'ram.used': '/ram/data/0',
    'ram.load': '/ram/load/0',
    'fan.cpu': '/lpc/it8689e/0/fan/0',
  };
  const units: Record<string, [string, number]> = {
    Temperature: ['°C', 0],
    Load: ['%', 0],
    Level: ['%', 0],
    Control: ['%', 0],
    Fan: ['RPM', 0],
    Power: ['W', 0],
    Clock: ['MHz', 0],
    Data: ['GB', 1],
    Throughput: ['MB/s', 1],
  };
  const sensor = (id: string | undefined): Sensor | undefined => state.sensors.find((candidate) => candidate.id === id);
  const sensorOf = (item: Item): Sensor | undefined => sensor(item.sensor ?? item.sensors?.[0] ?? metricSensors[item.metric ?? '']);
  const meterOf = (item: Item): Sensor | undefined =>
    item.meter === undefined ? undefined : sensor(item.meter.sensor ?? metricSensors[item.meter.metric ?? '']);
  const live = (one: Sensor | undefined): number | null => (state.service === 'stopped' || one === undefined ? null : one.value);
  const format = (type: string, value: number): string => {
    const [unit, decimals] = units[type] ?? ['', 1];
    if (type === 'TimeSpan') {
      return `${Math.floor(value / 3600)} h ${Math.floor((value % 3600) / 60)} min`;
    }
    const shown = type === 'Throughput' ? value / 1e6 : value;
    return `${shown.toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })} ${unit}`;
  };

  const labelOf = (item: Item): string => {
    if (item.label !== undefined) {
      return item.label;
    }
    const kind = (item.metric ?? '').split('.')[0];
    const byMetric: Record<string, string> = { cpu: 'MetricLabel.Cpu', fan: 'MetricLabel.Cpu', gpu: 'MetricLabel.Gpu', ram: 'MetricLabel.Ram' };
    const byDevice: Record<string, string> = {
      Cpu: 'MetricLabel.Cpu',
      GpuNvidia: 'MetricLabel.Gpu',
      GpuAmd: 'MetricLabel.Gpu',
      Memory: 'MetricLabel.Ram',
      Storage: 'MetricLabel.Ssd',
    };
    return say(byMetric[kind ?? ''] ?? byDevice[sensorOf(item)?.hardwareType ?? ''] ?? 'DeviceKind.Board');
  };
  const pillsOf = (items: Item[]): PillView[] => {
    const pills: PillView[] = [];
    for (const item of items) {
      const label = labelOf(item);
      const current = pills.at(-1);
      if (current !== undefined && current.label === label && item.own !== true) {
        current.items.push(item.id);
      } else {
        pills.push({ label, items: [item.id] });
      }
    }
    return pills;
  };
  const levelOf = (item: Item): ItemView['level'] => {
    const value = live(meterOf(item));
    if (value === null || item.meter === undefined) {
      return null;
    }
    const { warning, critical } = item.threshold ?? {};
    return critical !== undefined && value >= critical ? 'critical' : warning !== undefined && value >= warning ? 'warning' : 'normal';
  };
  const meterable = ['Temperature', 'Load', 'Control', 'Fan', 'Throughput'];
  // A metric's name as Core's MetricName says it: "ram.load" is RamLoad, and the fan's metrics name the CPU first.
  const metricName = (metric: string): string => {
    const words = metric.startsWith('fan.cpu') ? ['cpu', 'fan', ...metric.split('.').slice(2)] : metric.split('.');
    return say(`MetricName.${words.map((word) => word.charAt(0).toUpperCase() + word.slice(1)).join('')}`);
  };
  const meterNameOf = (item: Item): string | null =>
    item.meter === undefined
      ? null
      : item.meter.sensor !== undefined
        ? (sensor(item.meter.sensor)?.name ?? item.meter.sensor)
        : metricName(item.meter.metric ?? '');
  // As Core's Threshold.MetersOf: the item itself, then a fan's own duty or a duty's own fan where it is read.
  const otherForm = (item: Item): Meter | undefined => {
    if (item.metric === 'fan.cpu' || item.metric === 'fan.cpu.duty') {
      return { metric: item.metric === 'fan.cpu' ? 'fan.cpu.duty' : 'fan.cpu' };
    }
    const swapped = item.sensor?.includes('/fan/') === true ? item.sensor.replace('/fan/', '/control/') : item.sensor?.replace('/control/', '/fan/');
    return swapped !== undefined && swapped !== item.sensor && sensor(swapped) !== undefined ? { sensor: swapped } : undefined;
  };
  const metersOf = (item: Item, type: string): MeterChoice[] => {
    const self: Meter | undefined = item.metric !== undefined ? { metric: item.metric } : item.sensor !== undefined ? { sensor: item.sensor } : undefined;
    // Each named by what it reads, as the C# side names it: a fan's speed or its duty, else its type.
    const kindName = (of: string): string => say(of === 'Fan' ? 'UiText.MeterSpeed' : `SensorKind.${of}`);
    // The RAM in use is judged by the RAM's load.
    if (item.sensors === undefined && (item.metric ?? item.auto) === 'ram.used') {
      return [{ meter: { metric: 'ram.load' }, name: kindName('Load') }];
    }
    if (!meterable.includes(type) || self === undefined) {
      return [];
    }
    const other = item.sensors === undefined ? otherForm(item) : undefined;
    return [{ meter: self, name: kindName(type) }, ...(other === undefined ? [] : [{ meter: other, name: kindName(type === 'Fan' ? 'Control' : 'Fan') }])];
  };
  const itemView = (item: Item): ItemView => {
    const read = sensorOf(item);
    const value = live(read);
    const missing = value === null || (value === 0 && read?.hardwareType === 'Cpu' && read.type !== 'Load');
    const type = read?.type ?? 'Temperature';
    return {
      value: missing ? null : format(type, value),
      level: levelOf(item),
      type,
      ...(item.metric !== undefined && metricSensors[item.metric] !== undefined ? { auto: metricSensors[item.metric] } : {}),
      meters: metersOf(item, type),
      meterName: meterNameOf(item),
      // A network's speed, never a drive's, shows in bits.
      bits: type === 'Throughput' && (read?.hardwareType === 'Network' || item.metric === 'net.download' || item.metric === 'net.upload'),
    };
  };

  // thermi's mark, as the tray's: gray without fresh readings, else the worst level of every item.
  const markOf = (items: Item[]): MarkState => {
    if (state.service !== 'running') {
      return 'stopped';
    }
    const levels = items.map(levelOf);
    return levels.includes('critical') ? 'hot' : levels.includes('warning') ? 'warm' : 'calm';
  };

  bridge.view = async (): Promise<WindowView> => {
    const items = state.settings.overlays.flatMap((overlay) => overlay.items);
    return {
      pills: state.settings.overlays.map((overlay) => pillsOf(overlay.items)),
      items: Object.fromEntries(items.map((item) => [item.id, itemView(item)])),
      values: Object.fromEntries(state.sensors.flatMap((one) => (live(one) === null ? [] : [[one.id, format(one.type, one.value ?? 0)]]))),
      mark: markOf(items),
    };
  };

  // A line's edits, and the push of the other line past it, are the real bridge's, which the fake replays.
  const scaleOf = (type: string): { max: number; step: number } => (type === 'Fan' ? { max: 10000, step: 50 } : { max: 100, step: 1 });

  // Ten minutes of readings around the value now, the same on every run so a screenshot compares.
  const history = (value: number | null, max: number): (number | null)[] =>
    Array.from({ length: 600 }, (_, index) =>
      value === null
        ? null
        : Math.max(0, Math.min(max, value + 4 * Math.sin(index / 17) + 2.5 * Math.sin(index / 5.3) - 3 * Math.cos(index / 61))),
    );
  const colorsOf = (from: number, to: number, warning?: number, critical?: number): { at: number; color: ChartColor }[] => {
    const warm = warning ?? critical;
    if (warm === undefined) {
      return [{ at: from, color: 'cool' }, { at: to, color: 'middle' }];
    }
    const hot = warning !== undefined && critical !== undefined ? critical : warm + 10;
    return [{ at: warm - 20, color: 'cool' }, { at: warm - 8, color: 'middle' }, { at: warm, color: 'warm' }, { at: hot, color: 'hot' }];
  };

  // The smallest 1, 2 or 5 times a power of ten at or over the value, as Core's chart tops.
  const oneTwoFive = (value: number): number => {
    const power = 10 ** Math.floor(Math.log10(value));
    return ([1, 2, 5, 10].find((each) => each * power >= value) ?? 10) * power;
  };

  // A line reached for more than half of the ten minutes' seconds, unless the lines are the item's defaults.
  const firesMostly = (readings: readonly (number | null)[], line: number | undefined): boolean =>
    line !== undefined && readings.filter((reading) => reading !== null && reading >= line).length * 2 > 600;
  const madeLines = (item: Item): unknown => state.defaultOverlay.items.find((made) => made.metric !== undefined && made.metric === item.metric)?.threshold;

  // As Core's WindowCharts: the button that puts back the defaults says them.
  const defaultsText = (made: Threshold | undefined, said: (reading: number, units: boolean) => string): string =>
    made?.warning !== undefined && made.critical !== undefined
      ? say('ChartText.UseLines', said(made.warning, false), said(made.critical, true))
      : (made?.warning ?? made?.critical) !== undefined
        ? say('ChartText.UseLine', said((made?.warning ?? made?.critical)!, true))
        : say('ChartText.UseNoLines');

  bridge.chart = async (id: string): Promise<ChartView | null> => {
    const item = state.settings.overlays.flatMap((overlay) => overlay.items).find((candidate) => candidate.id === id);
    // As Core's WindowCharts: the meter, or without one the item's own reading, with no lines, unless it is no number.
    const meter = item === undefined ? undefined : item.meter === undefined ? sensorOf(item) : meterOf(item);
    const charted = item?.meter === undefined ? !['Time', 'Address', 'Duration'].includes(meter?.type ?? 'Time') : meterable.includes(meter?.type ?? '');
    if (item === undefined || meter === undefined || !charted) {
      return null;
    }
    const { max, step } = scaleOf(meter.type);
    const value = live(meter);
    const driver = meter.hardwareType === 'Cpu' && value === 0;
    const readings = history(driver ? null : value, meterable.includes(meter.type) ? max : Number.POSITIVE_INFINITY);
    const shown = readings.filter((reading): reading is number => reading !== null);
    const { warning, critical } = item.meter === undefined ? {} : (item.threshold ?? {});
    const lines = [warning, critical].filter((line): line is number => line !== undefined);
    const all = [...shown, ...lines];
    const top = all.length === 0 ? 0 : Math.max(...all);
    const [from, to] = meter.type === 'Temperature' && all.length > 0
      ? [Math.max(0, Math.floor((Math.min(...all) - 6) / 10) * 10), Math.min(100, Math.ceil((top + 3) / 5) * 5)]
      : meter.type === 'Fan' ? [0, Math.ceil(Math.max(top, 500) * 1.15 / 500) * 500]
        : meterable.includes(meter.type) ? [0, 100] : [0, oneTwoFive(Math.max(top * 1.15, 1))];
    const text = (line: number | undefined): string | null => (line === undefined ? null : format(meter.type, line));
    const average = shown.reduce((sum, reading) => sum + reading, 0) / Math.max(1, shown.length);
    const round = (reading: number): string => format(meter.type, reading).split(' ')[0] ?? '';
    return {
      max,
      step,
      from,
      to,
      readings,
      warning: warning ?? null,
      critical: critical ?? null,
      warningText: text(warning),
      criticalText: text(critical),
      colors: colorsOf(from, to, warning, critical),
      stats: shown.length === 0 ? null : `Min ${round(Math.min(...shown))} · Avg ${round(average)} · Max ${format(meter.type, Math.max(...shown))}`,
      summary:
        shown.length === 0
          ? ''
          : `Last 10 minutes: between ${round(Math.min(...shown))} and ${format(meter.type, Math.max(...shown))}, ` +
            `now ${format(meter.type, shown.at(-1) ?? 0)}.`,
      empty: state.service === 'stopped' ? 'stopped' : driver ? 'driver' : shown.length === 0 ? 'none' : null,
      pushed: null,
      title: meter.type === 'Control' ? say('ChartText.DutyTitle') : null,
      fromText: format(meter.type, from),
      toText: format(meter.type, to),
      fires:
        firesMostly(readings, warning ?? critical) && JSON.stringify(madeLines(item)) !== JSON.stringify(item.threshold)
          ? defaultsText(madeLines(item) as Threshold | undefined, (reading, units) => (units ? format(meter.type, reading) : round(reading)))
          : null,
      startText: say('ChartText.MinutesAgo', '10'),
    };
  };
}

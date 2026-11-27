import type {
  ChartView,
  ColorStop,
  Item,
  ItemView,
  LookColors,
  MarkState,
  Overlay,
  PillView,
  Sensor,
  Threshold,
  WarnBy,
  WarnByChoice,
  WindowView,
} from '../bridgeTypes';

// The C# side's views of the settings and the readings, as the fake gives them: the pills, each item's value and
// level, and its chart. Core decides these for real and tests them; the fake follows the same rules in rough form,
// only so the page has something true to show, and only the fields the page reads: what it never reads, as an item's
// bars, it leaves out. It runs in the page after the fake, so it may use nothing but types.
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
    'game.fps': '/game/0/framerate/0',
    'game.frametime': '/game/0/frametime/0',
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
    FrameRate: ['', 0],
    FrameTime: ['ms', 1],
  };
  const sensor = (id: string | undefined): Sensor | undefined => state.sensors.find((candidate) => candidate.id === id);
  const sensorOf = (item: Item): Sensor | undefined => sensor(item.sensor ?? item.sensors?.[0] ?? metricSensors[item.metric ?? '']);
  // What the item warns by: another reading of itself, or its own.
  const meterOf = (item: Item): Sensor | undefined =>
    item.warnBy === undefined ? sensorOf(item) : sensor(item.warnBy.sensor ?? metricSensors[item.warnBy.metric ?? '']);
  const live = (one: Sensor | undefined): number | null => (state.service === 'stopped' || one === undefined ? null : one.value);
  const format = (type: string, value: number): string => {
    const [unit, decimals] = units[type] ?? ['', 1];
    if (type === 'TimeSpan') {
      return `${Math.floor(value / 3600)} h ${Math.floor((value % 3600) / 60)} min`;
    }
    const shown = type === 'Throughput' ? value / 1e6 : value;
    return `${shown.toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })} ${unit}`.trimEnd();
  };

  const labelOf = (item: Item): string => {
    if (item.label !== undefined) {
      return item.label;
    }
    const kind = (item.metric ?? '').split('.')[0];
    const byMetric: Record<string, string> = {
      cpu: 'MetricLabel.Cpu',
      fan: 'MetricLabel.Cpu',
      gpu: 'MetricLabel.Gpu',
      ram: 'MetricLabel.Ram',
      game: 'MetricLabel.Fps',
    };
    const byDevice: Record<string, string> = {
      Cpu: 'MetricLabel.Cpu',
      GpuNvidia: 'MetricLabel.Gpu',
      GpuAmd: 'MetricLabel.Gpu',
      Memory: 'MetricLabel.Ram',
      Storage: 'MetricLabel.Ssd',
      Game: 'MetricLabel.Fps',
    };
    return say(byMetric[kind ?? ''] ?? byDevice[sensorOf(item)?.hardwareType ?? ''] ?? 'DeviceKind.Board');
  };
  // Mirrors Core's OverlayLayout.Runs; a tag only says what its first value is.
  const pillsOf = (overlay: Overlay): PillView[] => {
    const pills: (PillView & { run: string })[] = [];
    const layout = overlay.style?.layout ?? 'pills';
    for (const item of overlay.items) {
      const run = labelOf(item);
      const current = pills.at(-1);
      if (current !== undefined && current.run === run && layout !== 'each' && (layout === 'one' || item.own !== true)) {
        current.items.push(item.id);
      } else {
        pills.push({ run, label: itemView(item).label ?? run, items: [item.id] });
      }
    }
    return pills.map(({ label, items: ids }) => ({ label, items: ids }));
  };
  const meterable = ['Temperature', 'Load', 'Control', 'Fan', 'Throughput', 'FrameRate', 'FrameTime'];
  // As Core's Threshold.WarnsLow: a frame rate is in trouble when low, its critical under its warning.
  const low = (type: string | undefined): boolean => type === 'FrameRate';
  // As Core's Threshold.Judges: thresholds judge what it warns by when that reads a type they can judge.
  const judged = (item: Item): boolean => meterable.includes(meterOf(item)?.type ?? '');
  const levelOf = (item: Item): ItemView['level'] => {
    const value = live(meterOf(item));
    if (value === null || !judged(item)) {
      return null;
    }
    const { warning, critical } = item.threshold ?? {};
    const past = (line: number | undefined): boolean => line !== undefined && (low(meterOf(item)?.type) ? value <= line : value >= line);
    return past(critical) ? 'critical' : past(warning) ? 'warning' : 'normal';
  };
  // A metric's name as Core's MetricName says it: "ram.load" is RamLoad, and the fan's metrics name the CPU first.
  const metricName = (metric: string): string => {
    const words = metric.startsWith('fan.cpu') ? ['cpu', 'fan', ...metric.split('.').slice(2)] : metric.split('.');
    return say(`MetricName.${words.map((word) => word.charAt(0).toUpperCase() + word.slice(1)).join('')}`);
  };
  const warnByNameOf = (item: Item): string | null =>
    item.warnBy === undefined
      ? null
      : item.warnBy.sensor !== undefined
        ? (sensor(item.warnBy.sensor)?.name ?? item.warnBy.sensor)
        : metricName(item.warnBy.metric ?? '');
  // As Core's Threshold.WarnByOf: a fan's own duty or a duty's own fan where it is read.
  const otherForm = (item: Item): WarnBy | undefined => {
    if (item.metric === 'fan.cpu' || item.metric === 'fan.cpu.duty') {
      return { metric: item.metric === 'fan.cpu' ? 'fan.cpu.duty' : 'fan.cpu' };
    }
    const swapped = item.sensor?.includes('/fan/') === true ? item.sensor.replace('/fan/', '/control/') : item.sensor?.replace('/control/', '/fan/');
    return swapped !== undefined && swapped !== item.sensor && sensor(swapped) !== undefined ? { sensor: swapped } : undefined;
  };
  // As Core's WindowViews: its own reading first, Off where thresholds cannot judge it, then its other readings, each named
  // by what it reads: a fan's speed or its duty, else its type; the RAM in use is judged by the RAM's load.
  const warnByOf = (item: Item, type: string): WarnByChoice[] => {
    const kindName = (of: string): string => say(of === 'Fan' ? 'UiText.WarnBySpeed' : `SensorKind.${of}`);
    const own: WarnByChoice = { name: say(meterable.includes(type) ? 'UiText.WarnByOwn' : 'UiText.WarnByOff') };
    if (item.sensors === undefined && (item.metric ?? item.auto) === 'ram.used') {
      return [own, { warnBy: { metric: 'ram.load' }, name: kindName('Load') }];
    }
    const other = item.sensors === undefined ? otherForm(item) : undefined;
    return [own, ...(other === undefined ? [] : [{ warnBy: other, name: kindName(type === 'Fan' ? 'Control' : 'Fan') }])];
  };
  // As Core's SensorOffer: the sensors of its type on its own device first, then, of data, only its own kind's.
  const deviceOf = (id: string): string => id.slice(0, id.lastIndexOf('/', id.lastIndexOf('/') - 1));
  const offerOf = (read: Sensor | undefined, type: string): NonNullable<ItemView['offer']> => {
    const ofType = state.sensors.filter((one) => one.type === type);
    const own = (one: Sensor): boolean => read === undefined || deviceOf(one.id) === deviceOf(read.id);
    return {
      first: ofType.filter(own).map((one) => one.id),
      more: ofType.filter((one) => !own(one) && (type !== 'Data' || one.hardwareType === read?.hardwareType)).map((one) => one.id),
    };
  };
  // As Core's OverlayLayout.TagOf: what the value is when not the value now, said after it, a low of the game's saying
  // which first, by its metric or its sensor.
  const lows: Record<string, string> = {
    'game.low1': 'OverlayText.OnePercentLowTag',
    '/game/0/framerate/1': 'OverlayText.OnePercentLowTag',
    'game.low01': 'OverlayText.PointOnePercentLowTag',
    '/game/0/framerate/2': 'OverlayText.PointOnePercentLowTag',
  };
  const tagOf = (item: Item): string | undefined => {
    const stats: Record<string, string> = { min: 'OverlayText.MinTag', max: 'OverlayText.MaxTag', average: 'OverlayText.AverageTag' };
    const stat = stats[item.stat ?? ''];
    const said = stat !== undefined ? say(stat) : (item.smooth ?? 0) > 0 ? say('OverlayText.SmoothTag', String(item.smooth)) : undefined;
    const low = lows[item.sensor ?? item.metric ?? item.auto ?? ''];
    return low === undefined ? said : said === undefined ? say(low) : `${say(low)} ${said}`;
  };
  const itemView = (item: Item): ItemView => {
    const read = sensorOf(item);
    const value = live(read);
    const missing = value === null || (value === 0 && read?.hardwareType === 'Cpu' && read.type !== 'Load');
    const type = read?.type ?? 'Temperature';
    const tag = tagOf(item);
    return {
      ...(tag === undefined ? {} : { tag }),
      label: labelOf(item),
      offer: offerOf(read, type),
      value: missing ? null : format(type, value),
      level: levelOf(item),
      type,
      ...(item.metric !== undefined && metricSensors[item.metric] !== undefined ? { auto: metricSensors[item.metric] } : {}),
      warnBy: warnByOf(item, type),
      warnByName: warnByNameOf(item),
      judged: judged(item),
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

  // As Core's look draws it on the fake's dark taskbar, roughly: a color the style holds, else its theme's default.
  const themes = {
    dark: { background: '#20201E', normal: '#6CCB5F', warning: '#FEC84B', critical: '#F05A50', label: '#A9A9A7', value: '#EFEFEE' },
    light: { background: '#F3F3F1', normal: '#0F7B0F', warning: '#9D5D00', critical: '#C42B1C', label: '#5C5C5A', value: '#1A1A1A' },
  } as const;
  const colorsShown = (overlay: Overlay): LookColors => ({ ...themes[overlay.style?.pills === 'light' ? 'light' : 'dark'], ...overlay.style?.colors });

  bridge.view = async (): Promise<WindowView> => {
    const items = state.settings.overlays.flatMap((overlay) => overlay.items);
    return {
      pills: state.settings.overlays.map(pillsOf),
      items: Object.fromEntries(state.settings.overlays.flatMap((overlay) => overlay.items.map((item) => [item.id, itemView(item)]))),
      values: Object.fromEntries(state.sensors.flatMap((one) => (live(one) === null ? [] : [[one.id, format(one.type, one.value ?? 0)]]))),
      mark: markOf(items),
      colors: state.settings.overlays.map(colorsShown),
      // Whether an overlay fits its display is measured by C#, which the fake leaves to the window's own tests.
      fitted: [],
    };
  };

  // A line's edits, and the push of the other line past it, are the real bridge's, which the fake replays.
  const scaleOf = (type: string): { max: number; step: number } =>
    type === 'Fan' ? { max: 10000, step: 50 } : type.startsWith('Frame') ? { max: 1000, step: 1 } : { max: 100, step: 1 };

  // Ten minutes of readings around the value now, the same on every run so a screenshot compares, with a gap 195 to 204 s
  // ago, as the PC asleep or the service stopped leaves.
  const history = (value: number | null, max: number): (number | null)[] =>
    Array.from({ length: 600 }, (_, index) =>
      value === null || (index >= 395 && index < 405)
        ? null
        : Math.max(0, Math.min(max, value + 4 * Math.sin(index / 17) + 2.5 * Math.sin(index / 5.3) - 3 * Math.cos(index / 61))),
    );
  // As Core's MeterChart.Stops: the level from the bottom, then each line within the span where the level changes; a
  // low meter's level is that of the values just above it, critical up to the critical and normal over the warning.
  const colorsOf = (from: number, to: number, warning?: number, critical?: number, under = false): ColorStop[] => {
    const past = (line: number | undefined, at: number): boolean => line !== undefined && (under ? at < line : at >= line);
    const levelAt = (at: number): ColorStop['level'] => (past(critical, at) ? 'critical' : past(warning, at) ? 'warning' : 'normal');
    const stops: ColorStop[] = [{ at: from, level: levelAt(from) }];
    const lines = [warning, critical].filter((line): line is number => line !== undefined && line > from && line <= to).sort((a, b) => a - b);
    for (const line of lines) {
      if (levelAt(line) !== stops.at(-1)?.level) {
        stops.push({ at: line, level: levelAt(line) });
      }
    }
    return stops;
  };

  // The smallest 1, 2 or 5 times a power of ten at or over the value, as Core's chart tops.
  const oneTwoFive = (value: number): number => {
    const power = 10 ** Math.floor(Math.log10(value));
    return ([1, 2, 5, 10].find((each) => each * power >= value) ?? 10) * power;
  };

  // A line reached for more than half of the ten minutes' seconds, unless the lines are the item's defaults.
  const firesMostly = (readings: readonly (number | null)[], line: number | undefined, under: boolean): boolean =>
    line !== undefined && readings.filter((reading) => reading !== null && (under ? reading <= line : reading >= line)).length * 2 > 600;
  const madeLines = (item: Item): unknown => state.defaultOverlay.items.find((made) => made.metric !== undefined && made.metric === item.metric)?.threshold;

  // As Core's WindowCharts: the button that puts back the defaults says them.
  const defaultsText = (made: Threshold | undefined, said: (reading: number, units: boolean) => string): string =>
    made?.warning !== undefined && made.critical !== undefined
      ? say('ChartText.UseLines', said(made.warning, false), said(made.critical, true))
      : (made?.warning ?? made?.critical) !== undefined
        ? say('ChartText.UseLine', said((made?.warning ?? made?.critical)!, true))
        : say('ChartText.UseNoLines');

  // What each second's point says is Core's MeterChart.Points' to decide: the fake's are tokens, one of its own a second.
  const pointsOf = (readings: (number | null)[]): string[] => readings.map((_, index) => `#${index}`);

  bridge.chart = async (id: string): Promise<ChartView | null> => {
    const item = state.settings.overlays.flatMap((overlay) => overlay.items).find((candidate) => candidate.id === id);
    // As Core's WindowCharts: what it warns by, or for an item thresholds cannot judge its own reading, with no lines,
    // unless it is no number.
    const meter = item === undefined ? undefined : meterOf(item);
    const charted = meter !== undefined && !['Time', 'Address', 'Duration'].includes(meter.type);
    if (item === undefined || meter === undefined || !charted) {
      return null;
    }
    const { max, step } = scaleOf(meter.type);
    const value = live(meter);
    const driver = meter.hardwareType === 'Cpu' && value === 0;
    const readings = history(driver ? null : value, meterable.includes(meter.type) ? max : Number.POSITIVE_INFINITY);
    const shown = readings.filter((reading): reading is number => reading !== null);
    const { warning, critical } = judged(item) ? (item.threshold ?? {}) : {};
    const lines = [warning, critical].filter((line): line is number => line !== undefined);
    const all = [...shown, ...lines];
    const top = all.length === 0 ? 0 : Math.max(...all);
    const [from, to] = meter.type === 'Temperature' && all.length > 0
      ? [Math.max(0, Math.floor((Math.min(...all) - 6) / 10) * 10), Math.min(100, Math.ceil((top + 3) / 5) * 5)]
      : meter.type === 'Fan' ? [0, Math.ceil(Math.max(top, 500) * 1.15 / 500) * 500]
        : meterable.includes(meter.type) && !meter.type.startsWith('Frame') ? [0, 100] : [0, oneTwoFive(Math.max(top * 1.15, 1))];
    const text = (line: number | undefined): string | null => (line === undefined ? null : format(meter.type, line));
    const average = shown.reduce((sum, reading) => sum + reading, 0) / Math.max(1, shown.length);
    const round = (reading: number): string => format(meter.type, reading).split(' ')[0] ?? '';
    return {
      max,
      step,
      from,
      to,
      readings,
      points: pointsOf(readings),
      warning: warning ?? null,
      critical: critical ?? null,
      warningText: text(warning),
      criticalText: text(critical),
      colors: colorsOf(from, to, warning, critical, low(meter.type)),
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
        firesMostly(readings, warning ?? critical, low(meter.type)) && JSON.stringify(madeLines(item)) !== JSON.stringify(item.threshold)
          ? defaultsText(madeLines(item) as Threshold | undefined, (reading, units) => (units ? format(meter.type, reading) : round(reading)))
          : null,
      startText: say('ChartText.MinutesAgo', '10'),
      low: meter.type === 'FrameRate',
    };
  };
}

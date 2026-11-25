import type { OverlayPicture, Style } from '../bridgeTypes';

// The C# side's pictures of an overlay and of each look's sample, as the fake gives them. For real, Core's scene is
// painted by the overlay's own painter, which Thermi.Windows.Tests holds to the overlay's "Copy as image"; the fake
// draws rough pills in SVG from its views and the settings, only so the page shows something that changes as they do.
// It runs in the page after the fake and its views, so it may use nothing but types.
export function installFakePictures(): void {
  const fake = window.thermiFake;
  const bridge = window.thermi;
  if (fake === undefined || bridge === undefined) {
    throw new Error('The fake bridge is not installed.');
  }
  const state = fake.state;
  // Core's PillColors, fill, border, label and value; Match taskbar follows the fake's dark taskbar.
  const themes = { dark: ['#20201E', '#31312F', '#A9A9A7', '#EFEFEE'], light: ['#F3F3F1', '#DADAD8', '#5C5C5A', '#1A1A1A'] } as const;
  const fonts = { segoe: 'Segoe UI', bahnschrift: 'Bahnschrift', consolas: 'Consolas' } as const;
  const height = 38;
  const escape = (said: string): string => said.replace(/[<>&]/g, (one) => `&#${one.charCodeAt(0)};`);

  interface Run {
    label: string;
    values: string[];
  }
  // As Core's layout roughly: the view's pills, one per value, or all in one bar.
  const runsOf = (runs: Run[], style: Style): Run[] =>
    style.layout === 'one'
      ? [{ label: runs[0]?.label ?? '', values: runs.flatMap((run) => run.values) }]
      : style.layout === 'each'
        ? runs.flatMap((run) => run.values.map((value) => ({ label: run.label, values: [value] })))
        : runs;

  const paint = (runs: Run[], style: Style, size: number, taskbar: string | null): OverlayPicture => {
    const [fill, border, label, value] = themes[style.pills === 'light' ? 'light' : 'dark'];
    const values = (run: Run): string => run.values.map((one) => (style.units === false ? (one.split(' ')[0] ?? one) : one)).join('  ');
    const widths = runs.map((run) => 24 + (style.noLabels === true ? 0 : run.label.length + 1) * 7 + values(run).length * 7);
    const vertical = style.orientation === 'vertical';
    const widest = Math.max(0, ...widths);
    const gap = style.gap ?? 8;
    const radius = style.corners === 'square' ? 0 : style.corners === 'soft' ? height / 4 : height / 2;
    let at = 0;
    const shapes = runs.map((run, index) => {
      const width = vertical ? widest : (widths[index] ?? 0);
      const [x, y] = vertical ? [0, at] : [at, 0];
      at += (vertical ? height : width) + gap;
      const named = style.noLabels === true ? '' : `<tspan fill="${label}">${escape(run.label)} </tspan>`;
      return (
        `<rect x="${x + 0.5}" y="${y + 0.5}" width="${width - 1}" height="${height - 1}" rx="${radius}" fill="${fill}" ` +
        `fill-opacity="${style.opacity?.background ?? 1}" stroke="${border}"/>` +
        `<text x="${x + 12}" y="${y + 24}" fill="${value}">${named}${escape(values(run))}</text>`
      );
    });
    const [w, h] = vertical ? [widest, Math.max(0, at - gap)] : [Math.max(0, at - gap), height];
    const svg =
      `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" opacity="${style.opacity?.overlay ?? 1}" ` +
      `font-family="${fonts[style.font ?? 'segoe']}" font-size="13">${shapes.join('')}</svg>`;
    return { png: `data:image/svg+xml,${encodeURIComponent(svg)}`, width: w * size, height: h * size, taskbar };
  };

  bridge.preview = async (overlay) => {
    const one = state.settings.overlays[overlay];
    if (one === undefined) {
      return null;
    }
    const view = await bridge.view();
    const runs = (view.pills[overlay] ?? []).map((pill) => ({ label: pill.label, values: pill.items.map((id) => view.items[id]?.value ?? '—') }));
    const onTaskbar = (one.snap?.spot ?? (one.position === undefined ? 'taskbar' : null)) === 'taskbar' && one.style?.orientation !== 'vertical';
    return paint(runsOf(runs, one.style ?? {}), one.style ?? {}, one.size ?? 1, onTaskbar ? '#1C1C1C' : null);
  };

  // As Core's LookSample: the CPU's temperature and load, the GPU's load and the RAM in use, side by side in the look.
  bridge.lookSamples = async () => {
    const view = await bridge.view();
    const value = (id: string): string => view.items[id]?.value ?? '—';
    const runs: Run[] = [
      { label: state.texts['MetricLabel.Cpu'] ?? '', values: [value('cpu.temperature'), value('cpu.load')] },
      { label: state.texts['MetricLabel.Gpu'] ?? '', values: [value('gpu.load')] },
      { label: state.texts['MetricLabel.Ram'] ?? '', values: [value('ram.used')] },
    ];
    const sample = (look: Style): OverlayPicture => paint(runs, look, 1, null);
    return { default: sample({}), saved: Object.fromEntries(Object.entries(state.looks).map(([name, look]) => [name, sample(look)])) };
  };
}

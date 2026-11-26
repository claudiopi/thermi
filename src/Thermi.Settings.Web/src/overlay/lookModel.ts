import type { CornerStyle, Orientation, PillLayout, PillTheme, Style, StyleColors, StyleFont, TextEffect, TextStyle, Value } from '../bridge/bridge.g';
import type { TextKey } from '../texts/Texts';

// What the Appearance column reads from a look: each field as the overlay draws it, a field the file leaves out taking
// Core's default, so every control shows a value. The rules behind them stay Core's.

export interface ShownStyle {
  pills: PillTheme;
  layout: PillLayout;
  orientation: Orientation;
  background: number;
  overlay: number;
  gap: number;
  units: boolean;
  font: StyleFont;
  corners: CornerStyle;
  effect: TextEffect;
  colors: StyleColors;
  label: TextStyle;
  value: TextStyle;
}

const presetText: Record<string, [TextStyle, TextStyle]> = {
  compact: [{ size: 11, weight: 'semibold' }, { size: 12, weight: 'bold' }],
  large: [{ size: 16, weight: 'semibold' }, { size: 17, weight: 'bold' }],
  standard: [{ size: 13, weight: 'semibold' }, { size: 13.5, weight: 'bold' }],
};

/** A look as it shows, its text from its preset, or its own for a custom one. */
export function shownStyle(style: Style | undefined): ShownStyle {
  const [label, value] = presetText[style?.preset ?? 'standard'] ?? presetText['standard'] ?? [];
  const custom = style?.preset === 'custom';
  return {
    pills: style?.pills ?? 'system',
    layout: style?.layout ?? 'pills',
    orientation: style?.orientation ?? 'horizontal',
    background: style?.opacity?.background ?? 1,
    overlay: style?.opacity?.overlay ?? 1,
    gap: style?.gap ?? 8,
    units: style?.units ?? true,
    font: style?.font ?? 'segoe',
    corners: style?.corners ?? 'round',
    effect: style?.effect ?? 'auto',
    colors: style?.colors ?? {},
    label: (custom ? style.label : undefined) ?? label ?? { size: 13, weight: 'semibold' },
    value: (custom ? style.value : undefined) ?? value ?? { size: 13.5, weight: 'bold' },
  };
}

/** The meter's colors that "Colorblind-safe" sets: Okabe-Ito's blue, orange and vermilion. */
export const colorblindSafe = { normal: '#0072B2', warning: '#E69F00', critical: '#D55E00' } as const;

export function isColorblindSafe(colors: StyleColors): boolean {
  return (['normal', 'warning', 'critical'] as const).every((level) => colors[level]?.toLowerCase() === colorblindSafe[level].toLowerCase());
}

export type ColorKey = keyof StyleColors;

/** The colors as the file holds them: only the ones set, and none at all once all are back to their defaults. */
export function colorsValue(colors: { [key in ColorKey]?: string | undefined }): Value {
  const set = Object.entries(colors).filter((entry): entry is [string, string] => typeof entry[1] === 'string');
  return set.length === 0 ? null : Object.fromEntries(set);
}

/** The prototype's choices for each color, a null one being its default: green for normal, or Auto for text. */
export const colorChoices: Record<ColorKey, { text: TextKey; color: string | null }[]> = {
  normal: [
    { text: 'OverlayPageText.ColorGreen', color: null },
    { text: 'OverlayPageText.ColorSky', color: '#4CC2FF' },
    { text: 'OverlayPageText.ColorBlue', color: '#0072B2' },
  ],
  warning: [
    { text: 'OverlayPageText.ColorYellow', color: '#FEC84B' },
    { text: 'OverlayPageText.ColorAmber', color: '#FFB020' },
    { text: 'OverlayPageText.ColorOrange', color: '#E69F00' },
  ],
  critical: [
    { text: 'OverlayPageText.ColorRed', color: '#F05A50' },
    { text: 'OverlayPageText.ColorRose', color: '#FF4D6D' },
    { text: 'OverlayPageText.ColorVermilion', color: '#D55E00' },
  ],
  label: [
    { text: 'UiText.Auto', color: null },
    { text: 'OverlayPageText.ColorWhite', color: '#EFEFEE' },
    { text: 'OverlayPageText.ColorGray', color: '#A9A9A7' },
    { text: 'OverlayPageText.ColorSky', color: '#60CDFF' },
    { text: 'OverlayPageText.ColorBlack', color: '#1A1A1A' },
  ],
  value: [
    { text: 'UiText.Auto', color: null },
    { text: 'OverlayPageText.ColorWhite', color: '#EFEFEE' },
    { text: 'OverlayPageText.ColorGray', color: '#A9A9A7' },
    { text: 'OverlayPageText.ColorSky', color: '#60CDFF' },
    { text: 'OverlayPageText.ColorBlack', color: '#1A1A1A' },
  ],
  background: [
    { text: 'UiText.Auto', color: null },
    { text: 'OverlayPageText.ColorBlack', color: '#000000' },
    { text: 'OverlayPageText.ColorBlue', color: '#0B2E4F' },
    { text: 'OverlayPageText.ColorGray', color: '#5A5A58' },
    { text: 'OverlayPageText.ColorWhite', color: '#FFFFFF' },
  ],
};

/** A color as the hex box takes it: #RRGGBB, upper case. */
export function hexColor(typed: string): string | null {
  const hex = typed.trim().replace(/^#?/, '#');
  return /^#[0-9a-f]{6}$/i.test(hex) ? hex.toUpperCase() : null;
}

/** A setting's choices, each worded by its enum's text: `dark` of `PillTheme` reads `PillTheme.Dark`. */
export function enumChoices(say: (key: TextKey) => string, group: string, values: readonly string[]): { value: string; text: string }[] {
  return values.map((value) => ({ value, text: say(`${group}.${value.charAt(0).toUpperCase()}${value.slice(1)}`) }));
}

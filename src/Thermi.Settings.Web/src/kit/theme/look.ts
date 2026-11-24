import { tokens, webDarkTheme, webLightTheme, type Theme } from '@fluentui/react-components';

// The window's look, from the design prototype: Windows 11's spacing, radii and surfaces over Fluent's own tokens.
// Fluent's solid neutrals stay, so menus and dialogs never turn see-through; the surfaces below are thermi's.

const font = '"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif';

/** Windows' default accent, as it shows in light and dark mode, until the host gives the user's. */
const windowsAccent = { light: '#005fb8', dark: '#60cdff' };

/** The surfaces under the controls, as CSS custom properties that the kit's provider sets per mode. */
export const surface = {
  mica: 'var(--thermi-mica)',
  card: 'var(--thermi-card)',
  cardStroke: 'var(--thermi-card-stroke)',
  divider: 'var(--thermi-divider)',
  subtleHover: 'var(--thermi-subtle-hover)',
  panel: 'var(--thermi-panel)',
  flyout: 'var(--thermi-flyout)',
  flyoutStroke: 'var(--thermi-flyout-stroke)',
  dialog: 'var(--thermi-dialog)',
  dialogFoot: 'var(--thermi-dialog-foot)',
  /** Behind a dialog, over the list and the pane. */
  smoke: 'var(--thermi-smoke)',
} as const;

/** The text colors of the prototype: primary, secondary and tertiary, as Windows 11's. */
export const text = {
  primary: 'var(--thermi-text)',
  secondary: 'var(--thermi-text2)',
  tertiary: 'var(--thermi-text3)',
} as const;

// A flyout, as the narrow list and a toast, is opaque: over WebView2's see-through page a translucent one let the pane
// show through it.
// Windows draws Mica under the title row and the list; outside the host, a tint close to it, so contrast holds. Where
// Windows refused Mica, as Windows 10 does, page.css gives `--thermi-solid`, Windows' own solid window background.
// The chart's colors are the mark's, shaded per mode so the line keeps 3:1 against the window, and its lines take
// the meter colors, darker on light.
const surfaces = {
  light: {
    '--thermi-mica': 'var(--thermi-solid, linear-gradient(160deg, #eef1f8 0%, #f2f2f4 45%, #f4f0f4 100%), #f3f3f3)',
    '--thermi-layer': 'rgba(255, 255, 255, 0.5)',
    '--thermi-layer-stroke': 'rgba(0, 0, 0, 0.05)',
    '--thermi-card': 'rgba(255, 255, 255, 0.7)',
    '--thermi-card-stroke': 'rgba(0, 0, 0, 0.0578)',
    '--thermi-divider': 'rgba(0, 0, 0, 0.0803)',
    '--thermi-subtle-hover': 'rgba(0, 0, 0, 0.04)',
    '--thermi-panel': '#f3f3f3',
    '--thermi-ok-bg': '#dff6dd',
    '--thermi-ok': '#0f7b0f',
    '--thermi-text': 'rgba(0, 0, 0, 0.894)',
    '--thermi-text2': 'rgba(0, 0, 0, 0.608)',
    '--thermi-text3': 'rgba(0, 0, 0, 0.62)',
    '--thermi-cool': '#3D5BFF',
    '--thermi-middle': '#7A3DF0',
    '--thermi-warm': '#C25200',
    '--thermi-hot': '#D92D20',
    '--thermi-warning': '#9D5D00',
    '--thermi-critical': '#C42B1C',
    '--thermi-on-level': '#ffffff',
    '--thermi-bar-info': 'rgba(246, 246, 246, 0.5)',
    '--thermi-bar-warning': '#fff4ce',
    '--thermi-bar-error': '#fde7e9',
    '--thermi-bar-success': '#dff6dd',
    '--thermi-badge-warning': '#9d5d00',
    '--thermi-badge-error': '#c42b1c',
    '--thermi-badge-success': '#0f7b0f',
    '--thermi-on-badge': '#ffffff',
    '--thermi-flyout': '#f9f9f9',
    '--thermi-control': 'rgba(255, 255, 255, 0.7)',
    '--thermi-control-hover': 'rgba(249, 249, 249, 0.5)',
    '--thermi-control-stroke': 'rgba(0, 0, 0, 0.0578)',
    '--thermi-dialog': '#f3f3f3',
    '--thermi-dialog-foot': '#eeeeee',
    '--thermi-smoke': 'rgba(0, 0, 0, 0.3)',
    '--thermi-flyout-stroke': 'rgba(0, 0, 0, 0.0578)',
  },
  dark: {
    '--thermi-mica': 'var(--thermi-solid, linear-gradient(160deg, #1f2330 0%, #1d1d21 45%, #221e26 100%), #202020)',
    '--thermi-layer': 'rgba(255, 255, 255, 0.03)',
    '--thermi-layer-stroke': 'rgba(255, 255, 255, 0.05)',
    '--thermi-card': 'rgba(255, 255, 255, 0.0512)',
    '--thermi-card-stroke': 'rgba(0, 0, 0, 0.18)',
    '--thermi-divider': 'rgba(255, 255, 255, 0.0837)',
    '--thermi-subtle-hover': 'rgba(255, 255, 255, 0.06)',
    '--thermi-panel': '#272727',
    '--thermi-ok-bg': '#393d1b',
    '--thermi-ok': '#6ccb5f',
    '--thermi-text': '#ffffff',
    '--thermi-text2': 'rgba(255, 255, 255, 0.784)',
    '--thermi-text3': 'rgba(255, 255, 255, 0.64)',
    '--thermi-cool': '#7088FF',
    '--thermi-middle': '#A57BFF',
    '--thermi-warm': '#FF8A3D',
    '--thermi-hot': '#FF5A4F',
    '--thermi-warning': '#FEC84B',
    '--thermi-critical': '#F05A50',
    '--thermi-on-level': '#000000',
    '--thermi-bar-info': 'rgba(255, 255, 255, 0.0326)',
    '--thermi-bar-warning': '#433519',
    '--thermi-bar-error': '#442726',
    '--thermi-bar-success': '#393d1b',
    '--thermi-badge-warning': '#fce100',
    '--thermi-badge-error': '#ff99a4',
    '--thermi-badge-success': '#6ccb5f',
    '--thermi-on-badge': '#000000',
    '--thermi-flyout': '#2c2c2c',
    '--thermi-control': 'rgba(255, 255, 255, 0.0605)',
    '--thermi-control-hover': 'rgba(255, 255, 255, 0.0837)',
    '--thermi-control-stroke': 'rgba(255, 255, 255, 0.0698)',
    '--thermi-dialog': '#2b2b2b',
    '--thermi-dialog-foot': '#202020',
    // Windows' 30 % smoke barely shows over a dark window, so a dialog seemed to float over a live page.
    '--thermi-smoke': 'rgba(0, 0, 0, 0.55)',
    '--thermi-flyout-stroke': 'rgba(255, 255, 255, 0.08)',
  },
} as const;

export type Mode = keyof typeof surfaces;

/** The custom properties of a mode's surfaces, for the provider's root. */
export function surfacesOf(mode: Mode): Record<string, string> {
  return surfaces[mode];
}

/** Spacing and sizes, in the prototype's steps. */
export const space = {
  between: '4px',
  inside: '12px',
  row: '16px',
  page: '24px',
  control: '32px',
  /**
   * The window's title row, around Windows' caption buttons, which the page cannot dim: what covers the page starts
   * under it, so the row and the buttons stay one band.
   */
  titleRow: '48px',
} as const;

export const radius = {
  control: tokens.borderRadiusMedium,
  card: tokens.borderRadiusMedium,
  surface: tokens.borderRadiusXLarge,
  round: tokens.borderRadiusCircular,
} as const;

// Hover and pressed shades come from the accent itself, so any accent Windows gives keeps them in step.
// Text in the accent, as links, is the accent itself on light and a lighter shade of it on dark, as Windows' accent
// text, so it keeps 4.5:1 on the warning bar's brown too.
function accentTokens(accent: string, onAccent: string, mode: Mode): Partial<Theme> {
  const hover = `color-mix(in srgb, ${accent} 90%, ${onAccent})`;
  const pressed = `color-mix(in srgb, ${accent} 80%, ${onAccent})`;
  const accentText = mode === 'dark' ? `color-mix(in srgb, ${accent} 60%, #ffffff)` : accent;
  return {
    colorBrandForeground1: accentText,
    colorBrandForegroundLink: accentText,
    colorBrandForegroundLinkHover: accentText,
    colorBrandForegroundLinkPressed: pressed,
    colorBrandForegroundLinkSelected: accentText,
    colorBrandBackground: accent,
    colorBrandBackgroundHover: hover,
    colorBrandBackgroundPressed: pressed,
    colorBrandBackgroundSelected: accent,
    colorCompoundBrandBackground: accent,
    colorCompoundBrandBackgroundHover: hover,
    colorCompoundBrandBackgroundPressed: pressed,
    colorCompoundBrandStroke: accent,
    colorCompoundBrandStrokeHover: hover,
    colorCompoundBrandStrokePressed: pressed,
    colorBrandStroke1: accent,
    colorNeutralForegroundOnBrand: onAccent,
  };
}

/** Fluent's theme for a mode, with Windows' font and the accent, the user's when the host gives one. */
export function themeOf(mode: Mode, accent?: string): Theme {
  const base = mode === 'dark' ? webDarkTheme : webLightTheme;
  const onAccent = mode === 'dark' ? '#000000' : '#ffffff';
  return {
    ...base,
    ...accentTokens(accent ?? windowsAccent[mode], onAccent, mode),
    // Fluent tints an icon with the accent under the pointer or the focus; Windows' and the prototype's keep it neutral.
    colorNeutralForeground2BrandHover: base.colorNeutralForeground2Hover,
    colorNeutralForeground2BrandPressed: base.colorNeutralForeground2Pressed,
    colorNeutralForeground2BrandSelected: base.colorNeutralForeground2Selected,
    colorNeutralForeground3BrandHover: base.colorNeutralForeground3Hover,
    colorNeutralForeground3BrandPressed: base.colorNeutralForeground3Pressed,
    colorNeutralForeground3BrandSelected: base.colorNeutralForeground3Selected,
    fontFamilyBase: font,
  };
}

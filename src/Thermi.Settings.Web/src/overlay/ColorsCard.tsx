import { useRef, useState, type KeyboardEvent } from 'react';
import type { PillTheme, Style, Value } from '../bridge/bridge.g';
import { useEdit } from '../bridge/useWindow';
import { Expander } from '../app/Expander';
import { SwitchLine } from '../app/SwitchLine';
import { Button, CardRow, Popover, TextField } from '../kit';
import { useText } from '../texts/Texts';
import { colorChoices, colorblindSafe, hexColor, isColorblindSafe, levelDefaults, type ColorKey } from './lookModel';

export interface ColorsCardProps {
  overlay: number;
  style: Style | undefined;
  open: boolean;
  onOpen: (open: boolean) => void;
}

const levelKeys = ['normal', 'warning', 'critical'] as const;
const textKeys = ['label', 'value'] as const;
const names = {
  normal: 'StyleColor.Normal',
  warning: 'StyleColor.Warning',
  critical: 'StyleColor.Critical',
  label: 'StyleColor.Label',
  value: 'StyleColor.Value',
} as const;

/** The color a swatch shows while the look leaves it out: neutral, the level's default, or the pills' text. */
function fallbackOf(key: ColorKey, pills: PillTheme): string | null {
  const light = pills === 'light';
  switch (key) {
    case 'normal':
      return null;
    case 'warning':
      return light ? '#9D5D00' : levelDefaults.warning;
    case 'critical':
      return light ? '#C42B1C' : levelDefaults.critical;
    case 'label':
      return light ? '#5C5C5A' : '#A9A9A7';
    case 'value':
      return light ? '#1A1A1A' : '#EFEFEE';
  }
}

/** The colors as the file holds them: only the ones set, and none at all once all are back to their defaults. */
function colorsValue(colors: { [key in ColorKey]?: string | undefined }): Value {
  const set = Object.entries(colors).filter((entry): entry is [string, string] => typeof entry[1] === 'string');
  return set.length === 0 ? null : Object.fromEntries(set);
}

interface SwatchProps {
  name: string;
  color: string | undefined;
  fallback: string | null;
  choices: (typeof colorChoices)[ColorKey];
  onColor: (color: string | null) => void;
}

/** A color's swatch, which opens a hex box over its named choices; a hex that is not #RRGGBB stays, saying so. */
function Swatch({ name, color, fallback, choices, onColor }: SwatchProps) {
  const t = useText();
  const [open, setOpen] = useState(false);
  const [typed, setTyped] = useState<string | null>(null);
  const [refused, setRefused] = useState(false);
  const current = useRef<HTMLButtonElement>(null);
  const close = (): void => {
    setOpen(false);
    setTyped(null);
    setRefused(false);
  };
  const choose = (next: string | null): void => {
    close();
    if (next?.toUpperCase() !== color?.toUpperCase()) {
      onColor(next);
    }
  };
  const commit = (value: string): void => {
    const hex = hexColor(value);
    if (hex === null) {
      setRefused(true);
    } else {
      choose(hex);
    }
  };
  const shown = color ?? fallback;
  const trigger = (
    <button type="button" className="t-color-swatch" aria-label={name} aria-description={shown ?? t('OverlayPageText.ColorNeutral')}>
      <i className={shown === null ? 't-neutral' : undefined} style={shown === null ? undefined : { background: shown }} />
    </button>
  );
  const at = Math.max(0, choices.findIndex((choice) => (choice.color ?? '').toUpperCase() === (color ?? '').toUpperCase()));
  // The arrows move among the choices, as in a menu; Tab goes on to the hex box, which shows above them.
  const arrows = (event: KeyboardEvent<HTMLDivElement>): void => {
    const step = { ArrowDown: 1, ArrowUp: -1 }[event.key];
    const all = [...event.currentTarget.querySelectorAll<HTMLElement>('[role="radio"]')];
    const from = all.indexOf(document.activeElement as HTMLElement);
    if (step !== undefined && from >= 0) {
      event.preventDefault();
      all[(from + step + all.length) % all.length]?.focus();
    }
  };
  return (
    <Popover open={open} onOpenChange={(next) => (next ? setOpen(true) : close())} label={name} trigger={trigger} initialFocus={current}>
      <div className="t-color-choices" role="radiogroup" aria-label={name} onKeyDown={arrows}>
        {choices.map((choice, index) => (
          <button
            key={choice.text}
            ref={index === at ? current : undefined}
            type="button"
            role="radio"
            tabIndex={index === at ? 0 : -1}
            aria-checked={(choice.color ?? '').toUpperCase() === (color ?? '').toUpperCase()}
            className="t-color-choice"
            onClick={() => choose(choice.color)}
          >
            <i className={choice.color === null ? 't-neutral' : undefined} style={choice.color === null ? undefined : { background: choice.color }} />
            {t(choice.text)}
          </button>
        ))}
      </div>
      {/* Enter submits, which comes after the key's own handlers, so the key never lands on the swatch the focus goes back to. */}
      <form
        className="t-hex"
        onSubmit={(event) => {
          event.preventDefault();
          commit(typed ?? color ?? '');
        }}
      >
        <TextField
          label={t('UiText.HexColor')}
          labelShown={false}
          placeholder={t('OverlayPageText.HexHint')}
          value={typed ?? color ?? ''}
          onChange={(value) => {
            setTyped(value);
            setRefused(false);
          }}
          {...(refused ? { error: t('OverlayPageText.HexFormat') } : {})}
        />
      </form>
    </Popover>
  );
}

export function ColorsCard({ overlay, style, open, onOpen }: ColorsCardProps) {
  const t = useText();
  const edit = useEdit();
  const colors = style?.colors ?? {};
  const pills = style?.pills ?? 'system';
  const setColors = (next: { [key in ColorKey]?: string | undefined }): void =>
    void edit({ kind: 'style', overlay, field: 'colors', value: colorsValue(next) });
  const swatch = (key: ColorKey) => (
    <Swatch
      key={key}
      name={t(names[key])}
      color={colors[key]}
      fallback={fallbackOf(key, pills)}
      choices={colorChoices[key]}
      onColor={(color) => setColors({ ...colors, [key]: color ?? undefined })}
    />
  );
  const safe = isColorblindSafe(colors);
  const levels = (
    <span className="t-color-row" aria-hidden="true">
      {levelKeys.map((key) => {
        const shown = colors[key] ?? fallbackOf(key, pills);
        return <i key={key} className={shown === null ? 't-neutral' : undefined} style={shown === null ? undefined : { background: shown }} />;
      })}
    </span>
  );
  return (
    <Expander label={t('OverlayPageText.ColorsField')} aside={levels} open={open} onOpen={onOpen}>
      <CardRow label={t('UiText.MeterColorsField')}>{levelKeys.map(swatch)}</CardRow>
      <SwitchLine
        label={t('OverlayPageText.ColorblindSafe')}
        description={t('OverlayPageText.ColorblindHint')}
        checked={safe}
        onChange={(on) => setColors(on ? { ...colors, ...colorblindSafe } : { label: colors.label, value: colors.value })}
      />
      <CardRow
        label={t('UiText.TextColorsField')}
        description={t(
          colors.label === undefined && colors.value === undefined ? 'OverlayPageText.TextColorsAuto' : 'OverlayPageText.TextColorsOwn',
        )}
      >
        {textKeys.map(swatch)}
      </CardRow>
      <div className="t-reset-row">
        <Button kind="subtle" onClick={() => setColors({})}>
          {t('UiText.ResetColors')}
        </Button>
      </div>
    </Expander>
  );
}

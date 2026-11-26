import { useRef, useState, type KeyboardEvent } from 'react';
import type { LookColors, Style } from '../bridge/bridge.g';
import { useEdit } from '../bridge/useWindow';
import { Expander } from '../app/Expander';
import { SwitchLine } from '../app/SwitchLine';
import { Button, CardRow, Popover, TextField } from '../kit';
import { useText, type TextKey } from '../texts/Texts';
import { colorChoices, colorblindSafe, colorsValue, hexColor, isColorblindSafe, type ColorKey } from './lookModel';

export interface ColorsCardProps {
  overlay: number;
  style: Style | undefined;
  /** The colors the overlay draws now, as C# says, which a swatch the look leaves at its default shows. */
  drawn: LookColors | undefined;
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
  background: 'StyleColor.Background',
} as const;
// The pills' color stands alone in its row, which already names it.
const words: Partial<Record<ColorKey, TextKey>> = {
  normal: 'OverlayPageText.SwatchNormal',
  warning: 'OverlayPageText.SwatchWarning',
  critical: 'OverlayPageText.SwatchCritical',
  label: 'OverlayPageText.SwatchLabels',
  value: 'OverlayPageText.SwatchValues',
};

interface SwatchProps {
  name: string;
  color: string | undefined;
  fallback: string;
  /** Whether the default choice shows its color, as a level's does, or an empty ring, as Auto text does. */
  filledDefault: boolean;
  choices: (typeof colorChoices)[ColorKey];
  onColor: (color: string | null) => void;
}

/** A color's swatch, which opens a hex box over its named choices; a hex that is not #RRGGBB stays, saying so. */
function Swatch({ name, color, fallback, filledDefault, choices, onColor }: SwatchProps) {
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
    <button type="button" className="t-color-swatch" aria-label={name} aria-description={shown}>
      <span className="t-chip" style={{ background: shown }} />
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
        {choices.map((choice, index) => {
          const swatch = choice.color ?? (filledDefault ? fallback : null);
          return (
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
              <span className={swatch === null ? 't-chip t-neutral' : 't-chip'} style={swatch === null ? undefined : { background: swatch }} />
              {t(choice.text)}
            </button>
          );
        })}
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

export function ColorsCard({ overlay, style, drawn, open, onOpen }: ColorsCardProps) {
  const t = useText();
  const edit = useEdit();
  const colors = style?.colors ?? {};
  const setColors = (next: { [key in ColorKey]?: string | undefined }): void =>
    void edit({ kind: 'style', overlay, field: 'colors', value: colorsValue(next) });
  // Its word under it is what the swatch's name already tells Narrator.
  const swatch = (key: ColorKey) => {
    const word = words[key];
    return (
      <span key={key} className="t-swatch-named">
        <Swatch
          name={t(names[key])}
          color={colors[key]}
          fallback={drawn?.[key] ?? ''}
          filledDefault={key === 'normal'}
          choices={colorChoices[key]}
          onColor={(color) => setColors({ ...colors, [key]: color ?? undefined })}
        />
        {word !== undefined && <span aria-hidden="true">{t(word)}</span>}
      </span>
    );
  };
  const safe = isColorblindSafe(colors);
  const levels = (
    <span className="t-color-row" aria-hidden="true">
      {levelKeys.map((key) => {
        return <span key={key} className="t-chip" style={{ background: colors[key] ?? drawn?.[key] }} />;
      })}
    </span>
  );
  return (
    <Expander label={t('OverlayPageText.ColorsField')} aside={levels} open={open} onOpen={onOpen}>
      <CardRow label={t('StyleColor.Background')}>{swatch('background')}</CardRow>
      <CardRow label={t('UiText.MeterColorsField')}>{levelKeys.map(swatch)}</CardRow>
      <SwitchLine
        label={t('OverlayPageText.ColorblindSafe')}
        description={t('OverlayPageText.ColorblindHint')}
        checked={safe}
        onChange={(on) =>
          setColors(on ? { ...colors, ...colorblindSafe } : { label: colors.label, value: colors.value, background: colors.background })
        }
      />
      <CardRow
        label={t('UiText.TextColorsField')}
        description={t(
          colors.label === undefined && colors.value === undefined ? 'OverlayPageText.TextColorsAuto' : 'OverlayPageText.TextColorsOwn',
        )}
      >
        {textKeys.map(swatch)}
      </CardRow>
      {/* At the defaults already, a reset would do nothing, so it is not offered. */}
      {Object.values(colors).some((color) => color !== undefined) && (
        <div className="t-reset-row">
          <Button kind="subtle" onClick={() => setColors({})}>
            {t('UiText.ResetColors')}
          </Button>
        </div>
      )}
    </Expander>
  );
}

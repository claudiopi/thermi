import { steady } from '../steady';
import { Radio, RadioGroup, ToggleButton, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import type { Choice } from './Dropdown';
import { radius } from '../theme/look';
import type { KeyboardEvent } from 'react';

export interface SegmentedProps {
  label: string;
  choices: readonly Choice[];
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
}

// Radios underneath, so the arrows choose and only the chosen one takes Tab, drawn as the prototype's segments.
const useStyles = makeStyles({
  group: {
    display: 'inline-flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: '2px',
    height: '32px',
    padding: '1px',
    boxSizing: 'border-box',
    borderRadius: radius.card,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorSubtleBackground,
  },
  segment: {
    position: 'relative',
    height: '28px',
    alignItems: 'center',
    borderRadius: radius.control,
    ':hover': { backgroundColor: tokens.colorSubtleBackgroundHover },
    // Fluent colors a radio's label from the input beside it: the others in the secondary text, the chosen in the first.
    '& input:enabled:not(:checked) ~ .fui-Radio__label': { color: tokens.colorNeutralForeground2 },
    '& input:disabled ~ .fui-Radio__label': { color: tokens.colorNeutralForegroundDisabled },
  },
  chosen: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    boxShadow: `0 0 0 1px ${tokens.colorNeutralStroke2}`,
    '::after': {
      content: '""',
      position: 'absolute',
      left: '50%',
      bottom: '1px',
      width: '12px',
      height: '2px',
      marginLeft: '-6px',
      borderRadius: '1px',
      backgroundColor: tokens.colorCompoundBrandStroke,
    },
    '@media (forced-colors: active)': { outline: '1px solid Highlight', boxShadow: 'none', '::after': { display: 'none' } },
  },
  indicator: { display: 'none' },
  text: { padding: `0 ${tokens.spacingHorizontalM}`, color: tokens.colorNeutralForeground1 },
});

const useChipStyles = makeStyles({
  // Rows that wrap where the prototype's one row scrolled sideways: a scroll without a scrollbar hid chips with no hint.
  group: { display: 'flex', flexDirection: 'row', flexWrap: 'wrap', gap: '6px', flexShrink: 0 },
  chip: {
    flexShrink: 0,
    height: '28px',
    alignItems: 'center',
    borderRadius: radius.round,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorSubtleBackground,
    ':hover': { backgroundColor: tokens.colorSubtleBackgroundHover },
  },
  chosen: {
    backgroundColor: tokens.colorBrandBackground,
    borderTopColor: 'transparent',
    borderRightColor: 'transparent',
    borderBottomColor: 'transparent',
    borderLeftColor: 'transparent',
    ':hover': { backgroundColor: tokens.colorBrandBackgroundHover },
    // Fluent colors a checked radio's label from the input beside it, so the chip's text answers the same way.
    '& input:enabled:checked ~ .fui-Radio__label': { color: tokens.colorNeutralForegroundOnBrand },
    '@media (forced-colors: active)': {
      forcedColorAdjust: 'none',
      backgroundColor: 'Highlight',
      '& input:enabled:checked ~ .fui-Radio__label': { color: 'HighlightText' },
    },
  },
  indicator: { display: 'none' },
  text: { padding: '0 12px', lineHeight: '20px', whiteSpace: 'nowrap', color: tokens.colorNeutralForeground1 },
});

/**
 * The arrows move to the next choice or the one before and choose it, as Edge does for radios by itself and WebKit does
 * not: the page moves them alike in both.
 */
function arrowsChoose(choices: readonly Choice[], value: string, onChange: (value: string) => void) {
  return (event: KeyboardEvent<HTMLDivElement>): void => {
    const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[event.key];
    const from = choices.findIndex((choice) => choice.value === value);
    if (step === undefined || from < 0 || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }

    event.preventDefault();
    const next = choices[(from + step + choices.length) % choices.length];
    if (next !== undefined) {
      onChange(next.value);
      event.currentTarget.querySelector<HTMLInputElement>(`input[value="${CSS.escape(next.value)}"]`)?.focus();
    }
  };
}

/** Filters as a row of chips, one chosen: the arrows move and choose, and the row is one Tab stop. */
function ChipsDrawn({ label, choices, value, onChange }: Omit<SegmentedProps, 'disabled'>) {
  const styles = useChipStyles();
  return (
    <RadioGroup
      className={styles.group}
      aria-label={label}
      layout="horizontal"
      value={value}
      onChange={(_, data) => onChange(data.value)}
      onKeyDown={arrowsChoose(choices, value, onChange)}
    >
      {choices.map((choice) => (
        <Radio
          key={choice.value}
          className={mergeClasses(styles.chip, choice.value === value && styles.chosen)}
          indicator={{ className: styles.indicator }}
          label={{ className: styles.text, children: choice.text }}
          value={choice.value}
        />
      ))}
    </RadioGroup>
  );
}

const useToggleStyles = makeStyles({
  chip: {
    flexShrink: 0,
    minWidth: 0,
    height: '24px',
    padding: '0 10px',
    borderRadius: radius.round,
    fontWeight: tokens.fontWeightRegular,
    whiteSpace: 'nowrap',
  },
});

export interface ToggleChipProps {
  text: string;
  pressed: boolean;
  onChange: (pressed: boolean) => void;
}

function ToggleChipDrawn({ text, pressed, onChange }: ToggleChipProps) {
  const styles = useToggleStyles();
  return (
    <ToggleButton
      className={styles.chip}
      size="small"
      shape="circular"
      appearance={pressed ? 'primary' : 'outline'}
      checked={pressed}
      onClick={() => onChange(!pressed)}
    >
      {text}
    </ToggleButton>
  );
}

/** A few exclusive choices side by side: the arrows move and choose, as in Windows' settings. */
function SegmentedDrawn({ label, choices, value, onChange, disabled = false }: SegmentedProps) {
  const styles = useStyles();
  return (
    <RadioGroup
      className={styles.group}
      aria-label={label}
      layout="horizontal"
      value={value}
      disabled={disabled}
      onChange={(_, data) => onChange(data.value)}
      onKeyDown={disabled ? undefined : arrowsChoose(choices, value, onChange)}
    >
      {choices.map((choice) => (
        <Radio
          key={choice.value}
          className={mergeClasses(styles.segment, choice.value === value && styles.chosen)}
          indicator={{ className: styles.indicator }}
          label={{ className: styles.text, children: choice.text }}
          value={choice.value}
        />
      ))}
    </RadioGroup>
  );
}

export const Chips = steady(ChipsDrawn);

export const ToggleChip = steady(ToggleChipDrawn);

export const Segmented = steady(SegmentedDrawn);

import { steady } from '../steady';
import { Slider as FluentSlider, Input, makeStyles, tokens } from '@fluentui/react-components';
import { fluentRef } from '../fluentRef';
import { useRef, useState, type KeyboardEvent, type Ref } from 'react';
import { space } from '../theme/look';

export interface SliderProps {
  label: string;
  min: number;
  max: number;
  step: number;
  value: number;
  /** The value's number as a person reads it, like "125", which the box beside the slider shows and takes typed. */
  shown: string;
  unit: string;
  onChange: (value: number) => void;
  /**
   * Text typed in the box, kept with Enter or as the focus leaves it: C# reads it, and answers null once it kept it as
   * typed, else what Narrator hears.
   */
  onTyped: (typed: string) => Promise<string | null>;
  disabled?: boolean;
  /** Sets the slider's own width, as the prototype's rows do. */
  className?: string;
  ref?: Ref<HTMLInputElement>;
}

const useStyles = makeStyles({
  root: { display: 'inline-flex', alignItems: 'center', gap: space.inside },
  box: { width: '76px', minWidth: '0' },
  number: { textAlign: 'end', fontVariantNumeric: 'tabular-nums', color: tokens.colorNeutralForeground1 },
  unit: { color: tokens.colorNeutralForeground2 },
  // Fluent marks each step on the rail; Windows' sliders, and the prototype's, draw a plain rail.
  rail: { '::before': { display: 'none' } },
  said: { position: 'absolute', width: '1px', height: '1px', overflow: 'hidden', clipPath: 'inset(50%)', whiteSpace: 'nowrap' },
});

/** A slider with its value beside it in a box, which takes a typed number too. */
function SliderDrawn({ label, min, max, step, value, shown, unit, onChange, onTyped, disabled = false, className, ref }: SliderProps) {
  const styles = useStyles();
  const [typed, setTyped] = useState<string | null>(null);
  const [said, setSaid] = useState('');
  // Enter and the focus leaving may both come for one number; the second keeps nothing.
  const sent = useRef(false);
  const keep = (): void => {
    if (typed === null || sent.current) {
      return;
    }
    sent.current = true;
    void onTyped(typed).then((answer) => {
      sent.current = false;
      setTyped(null);
      setSaid(answer ?? '');
    });
  };
  const keyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    if (event.key === 'Enter') {
      keep();
    } else if (event.key === 'Escape' && typed !== null) {
      event.preventDefault();
      event.stopPropagation();
      setTyped(null);
    }
  };
  return (
    <span className={styles.root}>
      <FluentSlider
        ref={fluentRef(ref)}
        className={className}
        rail={{ className: styles.rail }}
        aria-label={label}
        aria-valuetext={`${shown} ${unit}`}
        min={min}
        max={max}
        step={step}
        value={value}
        disabled={disabled}
        onChange={(_, data) => onChange(data.value)}
      />
      <Input
        className={styles.box}
        input={{ className: styles.number }}
        size="small"
        aria-label={label}
        inputMode="decimal"
        spellCheck={false}
        value={typed ?? shown}
        disabled={disabled}
        contentAfter={<span className={styles.unit}>{unit}</span>}
        onChange={(_, data) => {
          setSaid('');
          setTyped(data.value);
        }}
        onKeyDown={keyDown}
        onBlur={keep}
      />
      <span className={styles.said} aria-live="polite">
        {said}
      </span>
    </span>
  );
}

export const Slider = steady(SliderDrawn);

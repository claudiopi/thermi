import { useEffect, useId, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';
import type { Line } from '../bridge/bridge.g';

export interface KnobProps {
  line: Line;
  /** Its name, "Warning line", which its box takes too. */
  label: string;
  value: number;
  text: string;
  max: number;
  step: number;
  /** Its center's height in the knobs' column. */
  top: number;
  /** A move by the keys, or null to turn the line off. */
  onMove: (value: number | null) => void;
  /** Typed text, which comes back refused, worded, or null once kept. */
  onTyped: (typed: string) => Promise<string | null>;
  /** A press on it, which drags it or, without a move, opens its box. */
  onPress: (event: PointerEvent<HTMLDivElement>, open: () => void) => void;
  onFocus: () => void;
}

/** What a key does to a knob's value: a step, ten with Page Up and Down, five times as far with Shift, or an end. */
function moved(event: KeyboardEvent, value: number, step: number, max: number): number | null {
  const times = event.shiftKey ? 5 : 1;
  const by: Record<string, number> = { ArrowUp: step, ArrowRight: step, ArrowDown: -step, ArrowLeft: -step, PageUp: 10 * step, PageDown: -10 * step };
  const delta = by[event.key];
  if (delta !== undefined) {
    return Math.min(max, Math.max(0, value + delta * times));
  }
  return event.key === 'Home' ? 0 : event.key === 'End' ? max : null;
}

function Box({ label, first, typedFirst, onDone, onTyped }: { label: string; first: string; typedFirst: boolean; onDone: (back: 'knob' | 'none') => void; onTyped: (typed: string) => Promise<string | null> }) {
  const [typed, setTyped] = useState(first);
  const [refused, setRefused] = useState<string | null>(null);
  const reason = useId();
  const closed = useRef(false);
  const input = useRef<HTMLInputElement>(null);
  useEffect(() => {
    input.current?.focus();
  }, []);
  const keep = (back: 'knob' | 'none'): void => {
    void onTyped(typed).then((refusal) => {
      if (refusal === null) {
        closed.current = true;
        onDone(back);
      } else if (!closed.current) {
        setRefused(refusal);
      }
    });
  };
  const keyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    if (event.key === 'Enter') {
      event.preventDefault();
      keep('knob');
    } else if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      closed.current = true;
      onDone('knob');
    }
  };
  return (
    <span className="t-box-wrap">
      <input
        className="t-box"
        role="spinbutton"
        aria-label={label}
        aria-invalid={refused !== null}
        aria-describedby={refused === null ? undefined : reason}
        ref={input}
        value={typed}
        onFocus={(event) => (typedFirst ? event.currentTarget.setSelectionRange(typed.length, typed.length) : event.currentTarget.select())}
        onChange={(event) => setTyped(event.target.value)}
        onKeyDown={keyDown}
        onBlur={() => {
          if (!closed.current) {
            keep('none');
          }
        }}
      />
      {refused !== null && (
        <span id={reason} className="t-box-refused" role="alert">
          {refused}
        </span>
      )}
    </span>
  );
}

/** A threshold's knob at its line: a slider that the arrows, a drag and a typed value move. */
export function Knob({ line, label, value, text, max, step, top, onMove, onTyped, onPress, onFocus }: KnobProps) {
  const [box, setBox] = useState<{ first: string; typed: boolean } | null>(null);
  const knob = useRef<HTMLDivElement>(null);
  const keyDown = (event: KeyboardEvent<HTMLDivElement>): void => {
    const next = moved(event, value, step, max);
    if (next !== null) {
      event.preventDefault();
      onMove(next);
    } else if (/^\d$/.test(event.key) && !event.ctrlKey && !event.altKey) {
      event.preventDefault();
      setBox({ first: event.key, typed: true });
    } else if (event.key === 'Enter' || event.key === 'F2') {
      event.preventDefault();
      setBox({ first: String(value), typed: false });
    } else if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault();
      onMove(null);
    }
  };
  const done = (back: 'knob' | 'none'): void => {
    setBox(null);
    if (back === 'knob') {
      knob.current?.focus();
    }
  };
  return (
    <div className={`t-knob-place t-${line}`} style={{ top: `${top}px` }}>
      <div
        ref={knob}
        className="t-knob"
        role="slider"
        tabIndex={0}
        aria-label={label}
        aria-orientation="vertical"
        aria-valuemin={0}
        aria-valuemax={max}
        aria-valuenow={value}
        aria-valuetext={text}
        data-focus={`knob-${line}`}
        onKeyDown={keyDown}
        onFocus={() => {
          setBox(null);
          onFocus();
        }}
        onPointerDown={(event) => onPress(event, () => setBox({ first: String(value), typed: false }))}
      >
        {text}
      </div>
      {box !== null && <Box label={label} first={box.first} typedFirst={box.typed} onDone={done} onTyped={onTyped} />}
    </div>
  );
}

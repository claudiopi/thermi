import { Combobox, Option, makeStyles, tokens } from '@fluentui/react-components';
import { steady } from '../steady';
import type { KeyboardEvent } from 'react';
import type { Choice } from './Dropdown';
import { useTypedBox } from './typedBox';

export interface SizeBoxProps {
  label: string;
  choices: readonly Choice[];
  value: string;
  onPick: (value: string) => void;
  /** Text typed in it, kept with Enter or as the focus leaves it: C# reads it, and answers null or what Narrator hears. */
  onTyped: (typed: string) => Promise<string | null>;
  className?: string;
}

const useStyles = makeStyles({
  // Fluent's box keeps the width of twenty letters, which would push the chevron out of a narrow box.
  input: { minWidth: '0', width: '100%', boxSizing: 'border-box' },
  listbox: { maxHeight: '320px', backgroundColor: tokens.colorNeutralBackground1 },
  said: { position: 'absolute', width: '1px', height: '1px', overflow: 'hidden', clipPath: 'inset(50%)', whiteSpace: 'nowrap' },
});

const listKeys = new Set(['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End']);

/** A list of sizes whose box also takes one typed, as a word processor's font size does. */
function SizeBoxDrawn({ label, choices, value, onPick, onTyped, className }: SizeBoxProps) {
  const styles = useStyles();
  const box = useTypedBox(onTyped);
  const current = choices.find((choice) => choice.value === value);
  // Typed text is what Enter keeps; moving through the list with the keys puts the box back, so Enter picks the size there.
  const keyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    if (event.key === 'Enter') {
      box.keep();
    } else if (event.key === 'Escape') {
      box.escape(event);
    } else if (listKeys.has(event.key)) {
      box.clear();
    }
  };
  return (
    <>
      <Combobox
        className={className}
        // Focused, its size is selected, so what is typed replaces it, as a word processor's box does.
        input={{ className: styles.input, onFocus: (event) => event.currentTarget.select() }}
        listbox={{ className: styles.listbox }}
        freeform
        aria-label={label}
        value={box.typed ?? current?.text ?? value}
        selectedOptions={[value]}
        onChange={(event) => box.type(event.target.value)}
        onOptionSelect={(event, data) => {
          // Fluent picks the active size with Enter too, which typed text overrules, and clears its pick as text matches
          // none; a click always picks.
          if (data.optionValue === undefined || (box.typed !== null && event.type !== 'click')) {
            return;
          }
          box.clear();
          if (data.optionValue !== value) {
            onPick(data.optionValue);
          }
        }}
        onKeyDown={keyDown}
        onBlur={box.keep}
      >
        {choices.map((choice) => (
          <Option key={choice.value} value={choice.value} text={choice.text}>
            {choice.text}
          </Option>
        ))}
      </Combobox>
      <span className={styles.said} aria-live="polite">
        {box.said}
      </span>
    </>
  );
}

export const SizeBox = steady(SizeBoxDrawn);

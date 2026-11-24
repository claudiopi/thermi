import { Combobox, Option, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { useState, type Ref } from 'react';
import { fluentRef } from '../fluentRef';
import { text } from '../theme/look';

export interface Choice {
  value: string;
  text: string;
  /** What shows at the choice's end, like a sensor's value now. */
  aside?: string;
  /** What the closed dropdown shows once it is chosen, when shorter than its text in the list. */
  shown?: string;
}

export interface DropdownProps {
  label: string;
  choices: readonly Choice[];
  value: string;
  onChange: (value: string) => void;
  /** The hint in the box while it is open, for a list long enough to filter, as "Filter 10 temperature sensors". */
  filter?: string;
  disabled?: boolean;
  /** The id of what explains it, which Narrator reads after its name. */
  describedBy?: string;
  /** Drawn as text in a sentence, like the overlay in the catalog's title: no box, the text sized to the choice. */
  inline?: boolean;
  className?: string;
  ref?: Ref<HTMLInputElement>;
}

const useStyles = makeStyles({
  dropdown: { minWidth: '160px' },
  option: { display: 'flex', gap: '12px', width: '100%' },
  name: { flex: '1', minWidth: '0' },
  aside: { color: text.secondary, fontVariantNumeric: 'tabular-nums' },
  listbox: { maxHeight: '320px', backgroundColor: tokens.colorNeutralBackground1 },
  // Fluent's box keeps the width of twenty letters, which would push the chevron out of a narrow dropdown.
  input: { minWidth: '0', width: '100%', boxSizing: 'border-box' },
  inline: { minWidth: '0', font: 'inherit', color: tokens.colorBrandForegroundLink, borderBottomColor: 'transparent', backgroundColor: 'transparent', ':hover': { backgroundColor: tokens.colorSubtleBackgroundHover, borderBottomColor: 'transparent' } },
  inlineInput: { fieldSizing: 'content', font: 'inherit', color: 'inherit', padding: '0' },
});

/**
 * A dropdown of choices. Alt+Down opens it on the current choice, typed text goes to the choice it starts, spaces
 * included, Enter takes it, and Esc closes it, the focus back on the dropdown. With `filter`, the typed text also
 * leaves out the choices that do not hold it.
 */
export function Dropdown({ label, choices, value, onChange, filter, disabled = false, describedBy, inline = false, className, ref }: DropdownProps) {
  const styles = useStyles();
  const [typed, setTyped] = useState<string | null>(null);
  const current = choices.find((choice) => choice.value === value);
  const shown = filter === undefined || typed === null ? choices : choices.filter((choice) => choice.text.toLowerCase().includes(typed.trim().toLowerCase()));
  return (
    <Combobox
      ref={fluentRef(ref)}
      className={mergeClasses(styles.dropdown, inline && styles.inline, className)}
      appearance={inline ? 'underline' : 'outline'}
      inlinePopup
      listbox={{ className: styles.listbox }}
      input={{ className: mergeClasses(styles.input, inline && styles.inlineInput) }}
      aria-label={label}
      aria-describedby={describedBy}
      value={typed ?? current?.shown ?? current?.text ?? ''}
      placeholder={typed === null ? '' : (filter ?? current?.text ?? '')}
      selectedOptions={[value]}
      disabled={disabled}
      onOpenChange={(_, data) => setTyped(data.open ? '' : null)}
      onChange={(event) => setTyped(event.target.value)}
      onOptionSelect={(_, data) => {
        setTyped(null);
        if (data.optionValue !== undefined && data.optionValue !== value) {
          onChange(data.optionValue);
        }
      }}
    >
      {shown.map((choice) => (
        <Option key={choice.value} value={choice.value} text={choice.text}>
          <span className={styles.option}>
            <span className={styles.name}>{choice.text}</span>
            {choice.aside !== undefined && <span className={styles.aside}>{choice.aside}</span>}
          </span>
        </Option>
      ))}
    </Combobox>
  );
}

import { Combobox, Option, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { steady } from '../steady';
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
  /** The end of its text that is only context, like a sensor's device, drawn quieter in the list. */
  detail?: string;
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
  /** A list wider than the box, its end on the box's end, for choices that read as a name and its context. */
  wide?: boolean;
  className?: string;
  ref?: Ref<HTMLInputElement>;
}

const useStyles = makeStyles({
  dropdown: { minWidth: '160px' },
  option: { display: 'flex', gap: '12px', width: '100%' },
  name: { flex: '1', minWidth: '0' },
  aside: { color: text.secondary, fontVariantNumeric: 'tabular-nums' },
  listbox: { maxHeight: '320px', backgroundColor: tokens.colorNeutralBackground1 },
  wide: { minWidth: '356px' },
  detail: { color: text.secondary },
  // Fluent's box keeps the width of twenty letters, which would push the chevron out of a narrow dropdown.
  input: { minWidth: '0', width: '100%', boxSizing: 'border-box' },
  inline: {
    minWidth: '0',
    font: 'inherit',
    color: tokens.colorBrandForegroundLink,
    borderBottomColor: 'transparent',
    backgroundColor: 'transparent',
    ':hover': { backgroundColor: tokens.colorSubtleBackgroundHover, borderBottomColor: 'transparent' },
  },
  inlineInput: { fieldSizing: 'content', font: 'inherit', color: 'inherit', padding: '0' },
});

/**
 * A dropdown of choices. Alt+Down opens it on the current choice, typed text goes to the choice it starts, spaces
 * included, Enter takes it, and Esc closes it, the focus back on the dropdown. With `filter`, the typed text also
 * leaves out the choices that do not hold it.
 */
export function Dropdown(props: DropdownProps) {
  const [typed, setTyped] = useState<string | null>(null);
  // Closed, its list does not show, so the choices' asides, values that change every second, draw nothing until it opens.
  const listed = typed === null ? props.choices.map(withoutAside) : props.choices;
  return <DropdownBox {...props} choices={listed} typed={typed} onTyped={setTyped} />;
}

const withoutAside = ({ value, text, shown, detail }: Choice): Choice => ({
  value,
  text,
  ...(shown === undefined ? {} : { shown }),
  ...(detail === undefined ? {} : { detail }),
});

interface DropdownBoxProps extends DropdownProps {
  /** What is typed in it while it is open; null while it is closed. */
  typed: string | null;
  onTyped: (typed: string | null) => void;
}

function DropdownBoxDrawn({
  label,
  choices,
  value,
  onChange,
  filter,
  disabled = false,
  describedBy,
  inline = false,
  wide = false,
  className,
  ref,
  typed,
  onTyped: setTyped,
}: DropdownBoxProps) {
  const styles = useStyles();
  const current = choices.find((choice) => choice.value === value);
  const shown = filter === undefined || typed === null ? choices : choices.filter((choice) => choice.text.toLowerCase().includes(typed.trim().toLowerCase()));
  return (
    <Combobox
      ref={fluentRef(ref)}
      className={mergeClasses(styles.dropdown, inline && styles.inline, className)}
      appearance={inline ? 'underline' : 'outline'}
      inlinePopup
      listbox={{ className: mergeClasses(styles.listbox, wide && styles.wide) }}
      {...(wide ? { positioning: 'below-end' as const } : {})}
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
            <span className={styles.name}>
              {choice.detail !== undefined && choice.text.endsWith(choice.detail) ? (
                <>
                  {choice.text.slice(0, choice.text.length - choice.detail.length)}
                  <span className={styles.detail}>{choice.detail}</span>
                </>
              ) : (
                choice.text
              )}
            </span>
            {choice.aside !== undefined && <span className={styles.aside}>{choice.aside}</span>}
          </span>
        </Option>
      ))}
    </Combobox>
  );
}

const DropdownBox = steady(DropdownBoxDrawn);

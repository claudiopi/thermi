import { Combobox, Option, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { steady } from '../steady';
import { useState, type KeyboardEvent, type Ref } from 'react';
import { fluentRef } from '../fluentRef';
import { text } from '../theme/look';
import { partsOf, useFound, type Find, type Found } from './dropdownSearch';
import { HighlightedText } from './HighlightedText';

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
  /** With `filter`, the C# side's search of what is typed among the choices, which decides what shows and what matched. */
  find?: Find;
  /** With `filter`, what the status line says while typed text filters: how many match, or that none matches it. */
  counted?: (count: number) => string;
  none?: (typed: string) => string;
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
  dropdown: {
    minWidth: '160px',
    // Overflowing past the box makes it the parent Fluent's findScrollableParent scrolls an option into view in, instead of
    // the pane, which jumped on open; check it again with each Fluent update. The list stays inline, not in a portal, which
    // would leave its options outside every landmark.
    '::before': { content: '""', position: 'absolute', top: '0', bottom: '-4px', width: '1px', visibility: 'hidden', pointerEvents: 'none' },
  },
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
  // It quotes what is typed, which can be longer than the list is wide.
  none: { color: text.secondary, whiteSpace: 'normal', overflowWrap: 'anywhere' },
  status: { position: 'absolute', width: '1px', height: '1px', overflow: 'hidden', clipPath: 'inset(50%)', whiteSpace: 'nowrap' },
});

/** Stands for no choice in the row that says none matches; no choice has it. */
const noMatch = '\u0000none';

/**
 * A dropdown of choices. Alt+Down opens it on the current choice, typed text goes to the choice it starts, spaces
 * included, Enter takes it, and Esc closes it, the focus back on the dropdown. With `filter`, the typed text also
 * leaves out the choices the C# side's search does not find, the best first, with what matched marked; a row says when
 * none does, a status line says how many do, and Enter takes the first.
 */
export function Dropdown(props: DropdownProps) {
  const [typed, setTyped] = useState<string | null>(null);
  const query = props.filter === undefined ? '' : (typed ?? '').trim();
  const found = useFound(
    query,
    props.choices.map(({ text: shown, detail }) => ({ title: partsOf(shown, detail).head, detail: partsOf(shown, detail).detail, aliases: [], words: [] })),
    props.find,
  );
  const count = found === null ? 0 : found.length;
  const said = found === null ? '' : count === 0 ? (props.none?.(query) ?? '') : (props.counted?.(count) ?? '');
  // Closed, its list does not show, so the choices' asides, values that change every second, draw nothing until it opens.
  const listed = typed === null ? props.choices.map(withoutAside) : props.choices;
  return <DropdownBox {...props} choices={listed} typed={typed} onTyped={setTyped} found={found} said={said} />;
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
  /** What the C# side's search found for what is typed, best first; null while nothing is typed or before it answers. */
  found: readonly Found[] | null;
  /** What the status line says of it. */
  said: string;
}

interface Shown {
  choice: Choice;
  found: Found | null;
}

/** The choices that show: with a search's answer, those it found, best first, else all of them. */
const shownOf = (choices: readonly Choice[], found: readonly Found[] | null): Shown[] =>
  found === null
    ? choices.map((choice) => ({ choice, found: null }))
    : found.flatMap((one) => {
        const choice = choices[one.index];
        return choice === undefined ? [] : [{ choice, found: one }];
      });

/** A choice's row: its text with what the search matched marked, its detail quieter, and its aside at its end. */
function ChoiceRow({ choice, found }: Shown) {
  const styles = useStyles();
  const { head, detail } = partsOf(choice.text, choice.detail);
  return (
    <span className={styles.option}>
      <span className={styles.name}>
        <HighlightedText text={head} marks={found?.title ?? []} />
        {detail !== '' && (
          <span className={styles.detail}>
            <HighlightedText text={detail} marks={found?.detail ?? []} />
          </span>
        )}
      </span>
      {choice.aside !== undefined && <span className={styles.aside}>{choice.aside}</span>}
    </span>
  );
}

/** In place of the choices when the search finds none, a row that says so, which nothing can pick. */
function NoMatchRow({ said }: { said: string }) {
  const styles = useStyles();
  return said === '' ? null : (
    <Option key={noMatch} value={noMatch} text={said} disabled>
      <span className={styles.none}>{said}</span>
    </Option>
  );
}

/**
 * Whether Enter takes the first choice the search found: Fluent makes active only a choice that starts with what is
 * typed, and the search finds by any word, so with none active.
 */
const takesFirst = (event: KeyboardEvent<HTMLInputElement>, first: string | undefined): first is string =>
  event.key === 'Enter' && first !== undefined && !event.currentTarget.getAttribute('aria-activedescendant');

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
  found,
  said,
}: DropdownBoxProps) {
  const styles = useStyles();
  const current = choices.find((choice) => choice.value === value);
  const shown = shownOf(choices, found);
  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    const first = found === null ? undefined : shown[0]?.choice.value;
    if (takesFirst(event, first)) {
      setTyped(null);
      if (first !== value) {
        onChange(first);
      }
    }
  };
  // Fluent clears the choice as typed text stops starting one; the choice stays the settings', so only a pick counts.
  const handlePick = (picked: string | undefined): void => {
    if (picked !== undefined) {
      setTyped(null);
      if (picked !== value && picked !== noMatch) {
        onChange(picked);
      }
    }
  };
  return (
    <>
    <Combobox
      ref={fluentRef(ref)}
      className={mergeClasses(styles.dropdown, inline && styles.inline, className)}
      appearance={inline ? 'underline' : 'outline'}
      inlinePopup
      listbox={{ className: mergeClasses(styles.listbox, wide && styles.wide) }}
      {...(wide ? { positioning: 'below-end' as const } : {})}
      // A sensor's name, as "Core (Tctl/Tdie)", is no word to check.
      input={{ className: mergeClasses(styles.input, inline && styles.inlineInput), spellCheck: false }}
      aria-label={label}
      aria-describedby={describedBy}
      value={typed ?? current?.shown ?? current?.text ?? ''}
      placeholder={typed === null ? '' : (filter ?? current?.text ?? '')}
      selectedOptions={[value]}
      disabled={disabled}
      onOpenChange={(_, data) => setTyped(data.open ? '' : null)}
      onChange={(event) => setTyped(event.target.value)}
      onKeyDown={handleKeyDown}
      onOptionSelect={(_, data) => handlePick(data.optionValue)}
    >
      {shown.map((one) => (
        <Option key={one.choice.value} value={one.choice.value} text={one.choice.text}>
          <ChoiceRow {...one} />
        </Option>
      ))}
      <NoMatchRow said={found?.length === 0 ? said : ''} />
    </Combobox>
    {filter !== undefined && typed !== null && (
      <span aria-live="polite" className={styles.status}>
        {said}
      </span>
    )}
    </>
  );
}

const DropdownBox = steady(DropdownBoxDrawn);

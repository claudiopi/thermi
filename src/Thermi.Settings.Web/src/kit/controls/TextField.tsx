import { steady } from '../steady';
import { Field, Input, makeStyles, tokens } from '@fluentui/react-components';
import { fluentRef } from '../fluentRef';
import type { KeyboardEvent, Ref } from 'react';
import { text } from '../theme/look';
import { Icon } from './Icon';

export interface TextFieldProps {
  label: string;
  /** Whether the label shows above the box, or only Narrator reads it, for a box whose row already names it. */
  labelShown?: boolean;
  value: string;
  onChange: (value: string) => void;
  /** Enter, or the focus leaving; either may come after the other, so committing twice must change nothing. */
  onCommit?: (value: string) => void;
  /** Esc, which puts the value back. */
  onCancel?: () => void;
  hint?: string;
  /** Why the value is refused; the box stays and says so. */
  error?: string;
  type?: 'text' | 'search';
  /** What shows in the empty box, like the label an item takes from its pill. */
  placeholder?: string;
  className?: string;
  ref?: Ref<HTMLInputElement>;
}

export interface SearchFieldProps {
  label: string;
  value: string;
  onChange: (value: string) => void;
  /** Keys typed in it, as Down into results or Enter on the first; one whose default is prevented goes no further. */
  onKeyDown?: (event: KeyboardEvent<HTMLInputElement>) => void;
  placeholder?: string;
  /** The id of what it filters, as a count, which Narrator reads after its name. */
  describedBy?: string;
  /** The name of the button that clears it. */
  clearLabel: string;
  ref?: Ref<HTMLInputElement>;
}

const useSearchStyles = makeStyles({
  box: { width: '100%' },
  // Edge's own clear button: Esc clears it, as the prototype's box does.
  input: { '::-webkit-search-cancel-button': { display: 'none' } },
  icon: { color: text.secondary, fontSize: '14px' },
  clear: {
    display: 'grid',
    placeItems: 'center',
    width: '24px',
    height: '24px',
    marginRight: '4px',
    padding: '0',
    border: 'none',
    borderRadius: '4px',
    background: 'transparent',
    color: tokens.colorBrandForeground1,
    fontSize: '12px',
    ':hover': { backgroundColor: tokens.colorSubtleBackgroundHover },
  },
});

/** A search box, its magnifier at its end, that filters what follows as each letter is typed; a button clears it. */
function SearchFieldDrawn({ label, value, onChange, onKeyDown, placeholder, describedBy, clearLabel, ref }: SearchFieldProps) {
  const styles = useSearchStyles();
  return (
    <Input
      ref={fluentRef(ref)}
      className={styles.box}
      input={{ className: styles.input }}
      type="search"
      aria-label={label}
      aria-describedby={describedBy}
      spellCheck={false}
      value={value}
      placeholder={placeholder}
      contentAfter={
        <>
          {value !== '' && (
            // Esc clears it from the keys, so the pointer's button stays out of Tab and leaves the focus in the box.
            <button type="button" className={styles.clear} tabIndex={-1} aria-label={clearLabel} title={clearLabel} onMouseDown={(event) => event.preventDefault()} onClick={() => onChange('')}>
              <Icon name="close" />
            </button>
          )}
          <Icon name="search" className={styles.icon} />
        </>
      }
      onChange={(_, data) => onChange(data.value)}
      onKeyDown={onKeyDown}
    />
  );
}

/** A text box in a field: its label, a hint, and the reason a value is refused. */
function TextFieldDrawn({ label, labelShown = true, value, onChange, onCommit, onCancel, hint, error, type = 'text', placeholder, className, ref }: TextFieldProps) {
  const keyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    if (event.key === 'Enter') {
      onCommit?.(value);
    } else if (event.key === 'Escape' && onCancel !== undefined) {
      event.stopPropagation();
      onCancel();
    }
  };
  return (
    <Field
      className={className}
      label={labelShown ? label : null}
      hint={hint ?? null}
      validationState={error === undefined ? 'none' : 'error'}
      validationMessage={error ?? null}
    >
      <Input
        ref={fluentRef(ref)}
        type={type}
        aria-label={labelShown ? undefined : label}
        value={value}
        placeholder={placeholder}
        onChange={(_, data) => onChange(data.value)}
        onKeyDown={keyDown}
        onBlur={() => onCommit?.(value)}
      />
    </Field>
  );
}

export const SearchField = steady(SearchFieldDrawn);

export const TextField = steady(TextFieldDrawn);

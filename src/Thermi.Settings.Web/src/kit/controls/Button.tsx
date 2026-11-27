import { steady } from '../steady';
import { Button as FluentButton, Link, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { fluentRef } from '../fluentRef';
import type { ButtonHTMLAttributes, ReactElement, ReactNode, Ref } from 'react';
import { radius } from '../theme/look';

export type ButtonKind = 'standard' | 'accent' | 'subtle';

const appearances = { standard: 'secondary', accent: 'primary', subtle: 'subtle' } as const;

/** What a menu or a tooltip around a button gives it: its handlers and the ARIA that ties them together. */
type Trigger = Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'type' | 'disabled' | 'children' | 'onClick' | 'className'>;

export interface ButtonProps extends Trigger {
  kind?: ButtonKind;
  children?: ReactNode;
  icon?: ReactElement;
  /** Windows' administrator shield before its text, for an action that asks Windows for permission. */
  shield?: boolean;
  /** The name Narrator reads, for a button that shows only its icon. */
  label?: string;
  /** Its shortcut, as `Control+Z`, which Narrator reads after its name. */
  keys?: string;
  disabled?: boolean;
  type?: 'button' | 'submit';
  onClick?: () => void;
  /** The id of what explains it, which Narrator reads after its name. */
  describedBy?: string;
  className?: string;
  ref?: Ref<HTMLButtonElement>;
}

// Windows' buttons, and the prototype's, write their text in the regular weight, where Fluent's are semibold.
const useStyles = makeStyles({
  button: { fontWeight: tokens.fontWeightRegular },
  // Beside a text the icon is 16 px, as in Windows' buttons; alone it keeps Fluent's 20 px.
  withText: { '& .fui-Button__icon': { fontSize: '16px', width: '16px', height: '16px' } },
  shield: { width: '16px', height: '16px', ':global(.t-contrast) &': { forcedColorAdjust: 'none' } },
});

/** Windows' administrator shield, in its own colors. */
function Shield({ className }: { className: string }) {
  return (
    <svg className={className} viewBox="0 0 16 16" aria-hidden="true">
      <path d="M8 1 2.5 3V8H8z" fill="#2b7cd3" />
      <path d="M8 1l5.5 2V8H8z" fill="#ffc83d" />
      <path d="M2.5 8c0 3.3 2.3 5.6 5.5 7V8z" fill="#ffc83d" />
      <path d="M13.5 8c0 3.3-2.3 5.6-5.5 7V8z" fill="#2b7cd3" />
    </svg>
  );
}

/**
 * A button. A disabled one keeps the focus it has, so a button that disables itself, like Undo when nothing is left
 * to undo, never drops the focus to the page.
 */
function ButtonDrawn({
  kind = 'standard',
  children,
  icon,
  shield = false,
  label,
  keys,
  disabled = false,
  type = 'button',
  onClick,
  describedBy,
  className,
  ref,
  ...trigger
}: ButtonProps) {
  const styles = useStyles();
  const shown = shield ? <Shield className={styles.shield} /> : icon;
  return (
    <FluentButton
      {...trigger}
      ref={fluentRef(ref)}
      type={type}
      appearance={appearances[kind]}
      icon={shown ?? null}
      aria-label={label ?? trigger['aria-label']}
      aria-keyshortcuts={keys}
      aria-describedby={describedBy ?? trigger['aria-describedby']}
      className={mergeClasses(styles.button, children !== undefined && shown !== undefined && styles.withText, className)}
      disabledFocusable={disabled}
      onClick={onClick}
    >
      {children}
    </FluentButton>
  );
}

const useLinkStyles = makeStyles({
  link: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '8px',
    minHeight: '32px',
    padding: '0 11px',
    marginInline: '-11px',
    borderRadius: radius.control,
    whiteSpace: 'nowrap',
    textDecorationLine: 'none',
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    ':hover': { backgroundColor: tokens.colorSubtleBackgroundHover, textDecorationLine: 'none' },
    ':hover:active': { textDecorationLine: 'none' },
  },
});

export interface LinkButtonProps {
  children: ReactNode;
  icon?: ReactElement;
  onClick: () => void;
  /** The name Narrator reads, when the text alone would not say enough. */
  label?: string;
  /** Its shortcut, as `Control+Z`, which Narrator reads after its name. */
  keys?: string;
  className?: string;
  ref?: Ref<HTMLButtonElement>;
}

/** An action written as a link in the accent's color, as "What is this?" in a bar or a toast's Undo. */
function LinkButtonDrawn({ children, icon, onClick, label, keys, className, ref }: LinkButtonProps) {
  const styles = useLinkStyles();
  return (
    <Link
      ref={fluentRef(ref)}
      as="button"
      type="button"
      className={mergeClasses(styles.link, className)}
      aria-label={label}
      aria-keyshortcuts={keys}
      onClick={onClick}
    >
      {icon}
      {children}
    </Link>
  );
}

export const Button = steady(ButtonDrawn);

export const LinkButton = steady(LinkButtonDrawn);

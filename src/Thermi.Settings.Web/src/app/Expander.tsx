import { useId, type ReactNode } from 'react';
import { Icon } from '../kit';

export interface ExpanderProps {
  label: string;
  /** What shows before its name, like About's mark. */
  icon?: ReactNode;
  /** What it holds, under its name. */
  hint?: string;
  /** Whether the hint stays once it is open, as "Text and shape" keeps it and "More options" does not. */
  hintWhenOpen?: boolean;
  /** What shows before the chevron, like the meter colors. */
  aside?: ReactNode;
  /** Rows of Windows' full height, for actions, where settings take compact ones. */
  roomy?: boolean;
  open: boolean;
  onOpen: (open: boolean) => void;
  children: ReactNode;
}

/** A card whose first row, clicked as a whole, opens the rest of its rows under it. */
export function Expander({ label, icon, hint, hintWhenOpen = false, aside, roomy = false, open, onOpen, children }: ExpanderProps) {
  const hintId = useId();
  const showHint = hint !== undefined && (hintWhenOpen || !open);
  return (
    <div className="t-card t-more">
      <button type="button" className="t-more-row" aria-expanded={open} aria-describedby={showHint ? hintId : undefined} onClick={() => onOpen(!open)}>
        {icon}
        <span className="t-more-text">
          <span className="t-more-label">{label}</span>
          {showHint && (
            <span id={hintId} className="t-more-hint">
              {hint}
            </span>
          )}
        </span>
        {aside}
        <span className="t-more-chevron">
          <Icon name={open ? 'chevronUp' : 'chevronDown'} />
        </span>
      </button>
      {open && <div className={roomy ? 't-more-rows t-roomy' : 't-more-rows'}>{children}</div>}
    </div>
  );
}

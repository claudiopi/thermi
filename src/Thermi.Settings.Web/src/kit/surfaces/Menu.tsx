import {
  Menu as FluentMenu,
  MenuDivider,
  MenuItem,
  MenuItemCheckbox,
  MenuItemRadio,
  MenuList,
  MenuPopover,
  MenuTrigger,
  makeStyles,
} from '@fluentui/react-components';
import { useEffect, useRef, useState, type ReactElement, type RefObject } from 'react';
import { useFocusOnOpen } from './focus';
import { fluentRef } from '../fluentRef';
import { Icon, type IconName } from '../controls/Icon';

export type MenuEntry =
  | { kind: 'action'; id: string; text: string; keys?: string; disabled?: boolean; icon?: IconName; onRun: () => void }
  | { kind: 'choice'; id: string; text: string; chosen: boolean; onRun: () => void }
  | { kind: 'check'; id: string; text: string; checked: boolean; disabled?: boolean; onRun: () => void }
  | { kind: 'submenu'; id: string; text: string; icon?: IconName; entries: readonly MenuEntry[] }
  | { kind: 'divider'; id: string };

export interface MenuProps {
  /** The control that opens it with a click; without one, `at` places it and the page opens it. */
  trigger?: ReactElement;
  entries: readonly MenuEntry[];
  /** Opened from the keyboard or a right click instead of a click on the trigger. */
  context?: boolean;
  /** The element a menu without a trigger opens beside, like a row on Shift+F10, which gets the focus back. */
  at?: HTMLElement | null;
  /** Whether a menu without a trigger is open, and its closing. */
  open?: boolean;
  onClose?: () => void;
}

const choices = 'choice';
const checks = 'check';

// Rendered beside its trigger, inside the page's landmarks where Narrator finds it, and fixed so no pane clips it.
const floating = { strategy: 'fixed' } as const;

const useStyles = makeStyles({
  icon: { fontSize: '16px' },
});

function EntryIcon({ name }: { name: IconName }) {
  return <Icon name={name} className={useStyles().icon} />;
}

// An entry's icon goes in Fluent's icon slot, which keeps the texts of a menu lined up, at Windows' 16 px.
const iconOf = (name: IconName | undefined): ReactElement | null => (name === undefined ? null : <EntryIcon name={name} />);

const chosenOf =(entries: readonly MenuEntry[]): string[] => entries.flatMap((entry) => (entry.kind === 'choice' && entry.chosen ? [entry.id] : []));
const checkedOf = (entries: readonly MenuEntry[]): string[] => entries.flatMap((entry) => (entry.kind === 'check' && entry.checked ? [entry.id] : []));

/** An entry that opens its own entries beside it: Right or Enter opens them, Left or Esc goes back. */
function Submenu({ text, icon, entries }: { text: string; icon: IconName | undefined; entries: readonly MenuEntry[] }) {
  const chosen = useRef<HTMLDivElement>(null);
  return (
    <FluentMenu
      checkedValues={{ [choices]: chosenOf(entries), [checks]: checkedOf(entries) }}
      inline
      positioning={{ ...floating, position: 'after', align: 'top' }}
    >
      <MenuTrigger disableButtonEnhancement>
        <MenuItem icon={iconOf(icon)}>{text}</MenuItem>
      </MenuTrigger>
      <MenuPopover>
        <MenuList>
          {entries.map((entry) => (
            <Entry key={entry.id} entry={entry} chosen={chosen} />
          ))}
        </MenuList>
      </MenuPopover>
    </FluentMenu>
  );
}

function Entry({ entry, chosen }: { entry: MenuEntry; chosen: RefObject<HTMLDivElement | null> }): ReactElement {
  switch (entry.kind) {
    case 'divider':
      return <MenuDivider />;
    case 'submenu':
      return <Submenu text={entry.text} icon={entry.icon} entries={entry.entries} />;
    case 'choice':
      return (
        <MenuItemRadio ref={fluentRef(entry.chosen ? chosen : undefined)} name={choices} value={entry.id} onClick={entry.onRun}>
          {entry.text}
        </MenuItemRadio>
      );
    case 'check':
      return (
        <MenuItemCheckbox name={checks} value={entry.id} disabled={entry.disabled ?? false} onClick={entry.onRun}>
          {entry.text}
        </MenuItemCheckbox>
      );
    case 'action':
      return (
        // Fluent's menu items keep the focus when disabled, so the arrows still pass over them, as Windows' do.
        <MenuItem icon={iconOf(entry.icon)} secondaryContent={entry.keys ?? null} disabled={entry.disabled ?? false} onClick={entry.onRun}>
          {entry.text}
        </MenuItem>
      );
  }
}

// A menu without a trigger opens on its first entry, and Fluent gives the focus back to nobody as it closes, so it
// goes back to the element the menu opened beside.
function usePlacedFocus(open: boolean, popover: RefObject<HTMLDivElement | null>): void {
  useEffect(() => {
    if (open) {
      queueMicrotask(() => popover.current?.querySelector<HTMLElement>('[role^="menuitem"]')?.focus());
    }
  }, [open, popover]);
}

function focusBackTo(at: HTMLElement | null | undefined, popover: HTMLElement | null): void {
  queueMicrotask(() => {
    const active = document.activeElement;
    const lost = active === null || active === document.body || popover?.contains(active) === true || popover?.isConnected === false;
    if (lost && at?.isConnected === true) {
      at.focus();
    }
  });
}

/**
 * A menu. It opens with the focus on the current choice, takes the arrows and typed letters, closes with Esc giving
 * the focus back to its trigger, and closes with Tab, the focus moving on.
 */
export function Menu({ trigger, entries, context = false, at, open: placed = false, onClose }: MenuProps) {
  const [clicked, setClicked] = useState(false);
  const open = trigger === undefined ? placed : clicked;
  const chosen = useRef<HTMLDivElement>(null);
  const popover = useRef<HTMLDivElement>(null);
  useFocusOnOpen(open, chosen);
  usePlacedFocus(open && trigger === undefined, popover);
  const chosenOnes = chosenOf(entries);
  const checkedOnes = checkedOf(entries);
  const change = (next: boolean): void => {
    if (trigger !== undefined) {
      setClicked(next);
    } else if (!next) {
      onClose?.();
      focusBackTo(at, popover.current);
    }
  };
  const list = (
    <MenuPopover ref={fluentRef(popover)}>
      <MenuList>
        {entries.map((entry) => (
          <Entry key={entry.id} entry={entry} chosen={chosen} />
        ))}
      </MenuList>
    </MenuPopover>
  );
  const props = {
    open,
    onOpenChange: (_: unknown, data: { open: boolean }) => change(data.open),
    checkedValues: { [choices]: chosenOnes, [checks]: checkedOnes },
    inline: true,
  };
  return trigger === undefined ? (
    // As wide as the row it opens under, as the prototype's row menus are.
    <FluentMenu {...props} positioning={{ ...floating, target: at ?? null, position: 'below', align: 'start', matchTargetSize: 'width' }}>
      {list}
    </FluentMenu>
  ) : (
    <FluentMenu {...props} openOnContext={context} positioning={floating}>
      <MenuTrigger disableButtonEnhancement={context}>{trigger}</MenuTrigger>
      {list}
    </FluentMenu>
  );
}

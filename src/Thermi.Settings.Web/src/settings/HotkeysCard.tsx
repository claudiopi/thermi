import { useId, useRef, useState, type KeyboardEvent } from 'react';
import { bridge, useEdit, type WindowData } from '../bridge/useWindow';
import type { HotkeyRole } from '../bridge/bridge.g';
import { Expander } from '../app/Expander';
import { Button, CardRow, Icon } from '../kit';
import { useText, type TextKey } from '../texts/Texts';
import { pressed } from './combination';
import { hotkeyNames } from './hotkeyNames';


interface HotkeyRowProps {
  role: HotkeyRole;
  combination: string | undefined;
  /** Why the last combination was refused, or why the overlay could not take the one set. */
  refusal: string | null;
  capturing: boolean;
  onCapture: (capturing: boolean) => void;
  onSet: (combination: string | null) => void;
}

/** What a hotkey's button shows: "Listening…" while it records, else its keys or Off. */
function HotkeyKeys({ id, combination, capturing }: { id: string; combination: string | undefined; capturing: boolean }) {
  const t = useText();
  if (capturing || combination === undefined) {
    return (
      <span id={id} className="t-hk-text">
        {capturing ? t('HotkeyText.HotkeyListening') : t('HotkeyText.HotkeyOff')}
      </span>
    );
  }
  return (
    <span id={id} className="t-kbd">
      {combination.split('+').map((key) => (
        <kbd key={key}>{key}</kbd>
      ))}
    </span>
  );
}

/** A hotkey's row: its keys or Off, which a click records anew, and × to clear it. */
function HotkeyRow({ role, combination, refusal, capturing, onCapture, onSet }: HotkeyRowProps) {
  const t = useText();
  const name = t(hotkeyNames[role]);
  const valueId = useId();
  const main = useRef<HTMLButtonElement>(null);
  const hintId = useId();
  const hint = capturing ? t('WindowText.HotkeyCapturing') : role === 'clickThrough' ? t('WindowText.ClickThroughHotkeyHint') : undefined;
  const keyDown = (event: KeyboardEvent<HTMLButtonElement>): void => {
    if (!capturing) {
      return;
    }
    const press = pressed(event);
    if (press?.kind === 'away') {
      onCapture(false);
      return;
    }
    // While it records, every key is the combination's, so none reaches the window's own shortcuts.
    event.preventDefault();
    event.stopPropagation();
    if (press?.kind === 'cancel') {
      onCapture(false);
    } else if (press?.kind === 'clear') {
      onSet(null);
    } else if (press?.kind === 'combination') {
      onSet(press.combination);
    }
  };
  const description = refusal ?? hint;
  // × goes with the keys it clears, so the focus stays on the row's keys.
  const clear = (): void => {
    main.current?.focus();
    onSet(null);
  };
  // A set hotkey's × sits in its field, where the pen was, so every field ends where the other rows' controls do.
  const clearable = combination !== undefined && !capturing;
  return (
    <CardRow
      label={name}
      {...(description === undefined
        ? {}
        : {
            description: <span className={refusal === null ? undefined : 't-hk-error'}>{description}</span>,
            descriptionId: hintId,
          })}
    >
      <span className="t-hk-field">
        <button
          ref={main}
          type="button"
          className={`t-hk${capturing ? ' t-hk-capturing' : ''}${clearable ? ' t-hk-set' : ''}`}
          aria-label={name}
          aria-describedby={description === undefined ? valueId : `${valueId} ${hintId}`}
          onClick={() => !capturing && onCapture(true)}
          onKeyDown={keyDown}
          onBlur={() => capturing && onCapture(false)}
        >
          <HotkeyKeys id={valueId} combination={combination} capturing={capturing} />
          {!capturing && !clearable && <Icon name="edit" className="t-hk-pen" />}
        </button>
        {clearable && <Button kind="subtle" className="t-hk-x" icon={<Icon name="close" />} label={t('WindowText.ClearHotkey', name)} onClick={clear} />}
      </span>
    </CardRow>
  );
}

export interface HotkeysCardProps {
  data: WindowData;
  open: boolean;
  onOpen: (open: boolean) => void;
}

/** Hotkeys: the ones Core offers, all off at first, each recorded in its row while thermi's own rest. */
export function HotkeysCard({ data, open, onOpen }: HotkeysCardProps) {
  const t = useText();
  const edit = useEdit();
  const [capturing, setCapturing] = useState<HotkeyRole | null>(null);
  const [refused, setRefused] = useState<{ role: HotkeyRole; why: string } | null>(null);
  const hotkeys = data.settings.hotkeys ?? {};
  const set = data.status.hotkeys.filter((role) => hotkeys[role] !== undefined).length;

  const capture = (role: HotkeyRole, on: boolean): void => {
    setCapturing(on ? role : null);
    if (on) {
      setRefused(null);
    }
    void bridge()?.restHotkeys(on);
  };
  // A combination another app holds is refused before Core judges the rest. A check that fails leaves it to Core, and
  // to the overlay, whose row says so if it cannot register it; thermi's own hotkeys wake whatever happens.
  const save = async (role: HotkeyRole, combination: string | null): Promise<void> => {
    setCapturing(null);
    try {
      const held = combination !== null && (await bridge()?.hotkeyHeld(combination).catch(() => false)) === true;
      const refusal = held ? t('HotkeyText.HotkeyHeld', combination) : await edit({ kind: 'thermi', field: `hotkeys.${role}`, value: combination });
      setRefused(refusal === null ? null : { role, why: t(refusal as TextKey) });
    } finally {
      await bridge()?.restHotkeys(false);
    }
  };
  const refusalOf = (role: HotkeyRole): string | null => {
    if (refused?.role === role) {
      return refused.why;
    }
    const failed = data.status.failed.includes(role) ? hotkeys[role] : undefined;
    return failed === undefined ? null : t('HotkeyText.HotkeyHeld', failed);
  };
  const hint = !data.status.overlay ? t('HotkeyText.HotkeysNeedOverlay') : set === 0 ? t('WindowText.HotkeysOff') : t('WindowText.HotkeysSet', set);
  return (
    <Expander label={t('WindowText.HotkeysField')} hint={hint} hintWhenOpen open={open} onOpen={onOpen}>
      {data.status.hotkeys.map((role) => (
        <HotkeyRow
          key={role}
          role={role}
          combination={hotkeys[role]}
          refusal={refusalOf(role)}
          capturing={capturing === role}
          onCapture={(on) => capture(role, on)}
          onSet={(combination) => void save(role, combination)}
        />
      ))}
    </Expander>
  );
}

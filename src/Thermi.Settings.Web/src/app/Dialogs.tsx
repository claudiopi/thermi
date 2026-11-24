import type { ReactNode } from 'react';
import type { WindowData } from '../bridge/useWindow';
import { Sheet } from '../kit';
import { hotkeyNames } from '../settings/hotkeyNames';
import { useText, type TextKey } from '../texts/Texts';

/** A shortcut's keys, as Windows writes them, which no language translates: each combination its keys. */
type Keys = string[][];

/** The window's own shortcuts, in the prototype's order. */
const windowKeys: { keys: Keys; text: TextKey }[] = [
  { keys: [['Ctrl', 'F']], text: 'UiText.ShortcutFind' },
  { keys: [['Ctrl', 'N']], text: 'UiText.ShortcutAdd' },
  { keys: [['F2']], text: 'UiText.ShortcutRename' },
  { keys: [['Delete']], text: 'UiText.ShortcutRemove' },
  { keys: [['Alt', '↑ ↓']], text: 'UiText.ShortcutMove' },
  { keys: [['Ctrl', 'Z'], ['Ctrl', 'Y']], text: 'UiText.ShortcutUndo' },
  { keys: [['Ctrl', 'V']], text: 'WindowText.ShortcutPaste' },
  { keys: [['Ctrl', ',']], text: 'UiText.ShortcutSettings' },
  { keys: [['Shift', 'F10']], text: 'UiText.ShortcutMenu' },
  { keys: [['F1'], ['?']], text: 'UiText.ShortcutSheet' },
];

/** What the pointer does on the overlay, a gesture where a key would be. */
const desktopGestures: { text: TextKey; gesture: TextKey }[] = [
  { text: 'SheetText.SelectOnDesktop', gesture: 'SheetText.Click' },
  { text: 'SheetText.OpenOnDesktop', gesture: 'SheetText.DoubleClick' },
  { text: 'SheetText.PlacePill', gesture: 'SheetText.DragOff' },
  { text: 'SheetText.ResizeOverlay', gesture: 'SheetText.ItsCorner' },
];

function KeyCaps({ keys }: { keys: Keys }) {
  return (
    <span className="t-sheet-keys">
      {keys.map((combination) => (
        <span key={combination.join('+')} className="t-kbd">
          {combination.map((key) => (
            <kbd key={key}>{key}</kbd>
          ))}
        </span>
      ))}
    </span>
  );
}

function SheetRow({ name, children }: { name: string; children: ReactNode }) {
  return (
    <div className="t-sheet-row">
      <span>{name}</span>
      {children}
    </div>
  );
}

/**
 * Every shortcut, in a sheet that fits in the window: the hotkeys as they are set, the window's keys, and what
 * the pointer does on the overlay.
 */
export function ShortcutSheet({ data, open, onClose }: { data: WindowData; open: boolean; onClose: () => void }) {
  const t = useText();
  const hotkeys = data.settings.hotkeys ?? {};
  return (
    <Sheet open={open} title={t('UiText.KeyboardShortcuts')} closeLabel={t('UiText.Close')} onClose={onClose}>
      <section className="t-sheet-section">
        <h3 className="t-sheet-head">{t('SheetText.AnywhereInWindows')}</h3>
        {data.status.hotkeys.map((role) => {
          const combination = hotkeys[role];
          return (
            <SheetRow key={role} name={t(hotkeyNames[role])}>
              {combination === undefined ? <span className="t-sheet-word">{t('HotkeyText.HotkeyOff')}</span> : <KeyCaps keys={[combination.split('+')]} />}
            </SheetRow>
          );
        })}
      </section>
      <section className="t-sheet-section">
        <h3 className="t-sheet-head">{t('SheetText.InThisWindow')}</h3>
        {windowKeys.map((shortcut) => (
          <SheetRow key={shortcut.text} name={t(shortcut.text)}>
            <KeyCaps keys={shortcut.keys} />
          </SheetRow>
        ))}
      </section>
      <section className="t-sheet-section">
        <h3 className="t-sheet-head">{t('SheetText.OnYourDesktop')}</h3>
        {desktopGestures.map((row) => (
          <SheetRow key={row.text} name={t(row.text)}>
            <span className="t-sheet-word">{t(row.gesture)}</span>
          </SheetRow>
        ))}
      </section>
    </Sheet>
  );
}

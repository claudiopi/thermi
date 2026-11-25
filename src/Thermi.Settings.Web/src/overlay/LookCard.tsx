import { useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import type { Value } from '../bridge/bridge.g';
import { asOneStep, bridge, useEdit, type WindowData } from '../bridge/useWindow';
import { Button, Icon, Menu, type MenuEntry } from '../kit';
import { NameDialog } from '../status/NameDialog';
import { useNews } from '../status/useNews';
import { useText, type TextKey } from '../texts/Texts';
import { useDevicePixels } from './pixels';
import { Picture } from './Preview';
import { useLooks, useLookSamples, useWorn } from './useOverlayReads';

export interface LookCardProps {
  data: WindowData;
  overlay: number;
}

/** The fields a look holds, which Copy to all copies. */
const lookFields = ['name', 'preset', 'label', 'value', 'opacity', 'pills', 'colors', 'font', 'corners', 'gap', 'effect', 'units'];

type Asking = { kind: 'save' } | { kind: 'rename'; name: string } | null;

export function LookCard({ data, overlay }: LookCardProps) {
  const t = useText();
  const edit = useEdit();
  const looks = useLooks(data.changes);
  const worn = useWorn(data.changes)[overlay];
  const samples = useLookSamples(useDevicePixels(), data.changes);
  const [asking, setAsking] = useState<Asking>(null);
  const news = useNews();
  const tiles = useRef<HTMLDivElement>(null);
  const one = data.settings.overlays[overlay];
  const many = data.settings.overlays.length > 1;
  const names = Object.keys(looks);
  // Default is null, none is undefined.
  const chosen = worn === undefined || worn === null ? undefined : (worn.saved ?? null);

  const choose = (name: string | null): void => void edit({ kind: 'look', overlay, name });
  const tileKeys = (event: KeyboardEvent<HTMLDivElement>): void => {
    const all = [null, ...names];
    const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[event.key];
    if (step === undefined) {
      return;
    }
    event.preventDefault();
    const next = all[((chosen === undefined ? -1 : all.indexOf(chosen)) + step + all.length) % all.length] ?? null;
    choose(next);
    tiles.current?.querySelectorAll<HTMLElement>('[role="radio"]')[all.indexOf(next)]?.focus();
  };
  // A look another program deleted or holds open is refused, and the toast says why.
  const deleteLook = async (name: string): Promise<void> => {
    const refusal = (await bridge()?.deleteLook(name)) ?? null;
    if (refusal !== null) {
      news.show({ message: t(refusal as TextKey) });
    }
  };
  const lookMenu = (name: string): MenuEntry[] => [
    ...(many
      ? [
          {
            kind: 'action' as const,
            id: 'all',
            text: t('OverlayPageText.UseOnAllOverlays'),
            onRun: () => void edit({ kind: 'look', overlay: null, name }),
          },
        ]
      : []),
    { kind: 'action', id: 'rename', text: t('UiText.RenameStyle'), onRun: () => setAsking({ kind: 'rename', name }) },
    { kind: 'action', id: 'export', text: t('UiText.ExportStyle'), onRun: () => void bridge()?.exportSavedLook(name) },
    { kind: 'divider', id: 'end' },
    { kind: 'action', id: 'delete', text: t('UiText.DeleteStyle'), onRun: () => void deleteLook(name) },
  ];
  const copyToAll = (): Promise<void> =>
    asOneStep(async () => {
      const fields = (one?.style ?? {}) as Record<string, Value | undefined>;
      for (const index of data.settings.overlays.keys()) {
        for (const field of index === overlay ? [] : lookFields) {
          await edit({ kind: 'style', overlay: index, field, value: fields[field] ?? null });
        }
      }
    });
  const fileMenu: MenuEntry[] = [
    ...(many
      ? [
          { kind: 'action' as const, id: 'all', text: t('WindowText.CopyLookToAll'), onRun: () => void copyToAll() },
          { kind: 'divider' as const, id: 'files' },
        ]
      : []),
    { kind: 'action', id: 'import', text: t('UiText.ImportStyle'), onRun: () => void news.importLook(overlay, null) },
    { kind: 'action', id: 'export', text: t('UiText.ExportStyle'), onRun: () => void bridge()?.exportLook(overlay) },
  ];
  const tile = (name: string | null): ReactNode => {
    const on = name === chosen;
    const shown = name === null ? t('OverlayPageText.DefaultLook') : name;
    return (
      <button
        type="button"
        role="radio"
        aria-checked={on}
        aria-label={t('OverlayPageText.LookTile', shown)}
        tabIndex={on || (chosen === undefined && name === null) ? 0 : -1}
        className={`t-look${on ? ' t-on' : ''}`}
        onClick={() => choose(name)}
      >
        <span className="t-look-name">{shown}</span>
        <span className="t-look-sample">
          <Picture picture={(name === null ? samples?.default : samples?.saved[name]) ?? null} />
        </span>
      </button>
    );
  };
  return (
    <div className="t-card t-look-card">
      <div className="t-look-head">
        <span className="t-look-title">{t('UiText.LookField')}</span>
        <Button onClick={() => setAsking({ kind: 'save' })}>{t('OverlayPageText.SaveAsMyLook')}</Button>
        <Menu trigger={<Button kind="subtle" label={t('OverlayPageText.LookFileActions')} icon={<Icon name="more" />} />} entries={fileMenu} />
      </div>
      <div
        ref={tiles}
        className="t-looks"
        role="radiogroup"
        aria-label={t('UiText.LookField')}
        onKeyDown={tileKeys}
      >
        {tile(null)}
        {names.map((name) => (
          <span key={name} className="t-look-wrap">
            {tile(name)}
            <Menu
              trigger={<Button kind="subtle" className="t-look-more" label={t('OverlayPageText.LookActions', name)} icon={<Icon name="more" />} />}
              entries={lookMenu(name)}
            />
          </span>
        ))}
      </div>
      {asking?.kind === 'save' && (
        <NameDialog
          title={t('UiText.SaveAsMyStyle')}
          main={t('UiText.Save')}
          start={t('OverlayPageText.MyLook')}
          hint={t('OverlayPageText.LookKeeps')}
          onName={async (name) => (await bridge()?.saveLook(overlay, name)) ?? null}
          onClose={() => setAsking(null)}
        />
      )}
      {asking?.kind === 'rename' && (
        <NameDialog
          title={t('OverlayPageText.RenameLook')}
          main={t('UiText.Rename')}
          start={asking.name}
          onName={async (name) => (await bridge()?.renameLook(asking.name, name)) ?? null}
          onClose={() => setAsking(null)}
        />
      )}
    </div>
  );
}

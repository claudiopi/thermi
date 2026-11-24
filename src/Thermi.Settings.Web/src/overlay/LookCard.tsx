import { useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react';
import type { Style, Value } from '../bridge/bridge.g';
import { asOneStep, bridge, useEdit, type WindowData } from '../bridge/useWindow';
import { Button, Icon, Menu, type MenuEntry } from '../kit';
import { NameDialog } from '../status/NameDialog';
import { useNews } from '../status/useNews';
import { useText } from '../texts/Texts';
import { fontFamilies, levelDefaults, pillSizes, shownStyle, weights } from './lookModel';
import { useLooks } from './useOverlayReads';

export interface LookCardProps {
  data: WindowData;
  overlay: number;
}

/** The fields a look holds, which Default takes back to Core's defaults. */
const lookFields = ['name', 'preset', 'label', 'value', 'opacity', 'pills', 'colors', 'font', 'corners', 'gap', 'effect', 'units'];

/** A look's sample pill: the overlay's first label and value, in the look's theme, colors, sizes, font and corners. */
function Sample({ look, label, value }: { look: Style; label: string; value: string }) {
  const shown = shownStyle(look);
  const sizes = pillSizes(shown);
  const warning = shown.colors.warning;
  const ownWarning = warning !== undefined && warning.toLowerCase() !== levelDefaults.warning.toLowerCase();
  const pill: CSSProperties = { height: sizes.height, borderRadius: sizes.radius, paddingInline: sizes.padding, gap: sizes.gap, fontFamily: fontFamilies[shown.font] };
  return (
    <span className={`t-pill t-pill-${shown.pills}`} style={pill} aria-hidden="true">
      <span className="t-pill-label" style={{ fontSize: shown.label.size, fontWeight: weights[shown.label.weight], color: shown.colors.label }}>
        {label}
      </span>
      <span className="t-pill-value" style={{ fontSize: shown.value.size, fontWeight: weights[shown.value.weight], color: ownWarning ? warning : shown.colors.value }}>
        {value}
      </span>
    </span>
  );
}

type Asking = { kind: 'save' } | { kind: 'rename'; name: string } | null;

export function LookCard({ data, overlay }: LookCardProps) {
  const t = useText();
  const edit = useEdit();
  const looks = useLooks(data.version);
  const [asking, setAsking] = useState<Asking>(null);
  const news = useNews();
  const tiles = useRef<HTMLDivElement>(null);
  const one = data.settings.overlays[overlay];
  const worn = one?.style?.name;
  const many = data.settings.overlays.length > 1;
  const sampleItem = one?.items.find((item) => item.id === 'cpu.temperature') ?? one?.items[0];
  const label = data.view.pills[overlay]?.find((pill) => pill.items.includes(sampleItem?.id ?? ''))?.label ?? '';
  const value = data.view.items[sampleItem?.id ?? '']?.value ?? '';
  const names = Object.keys(looks);
  const chosen = worn !== undefined && names.includes(worn) ? worn : null;

  const choose = (name: string | null): void => {
    if (name === null) {
      void asOneStep(() => lookFields.reduce<Promise<unknown>>((done, field) => done.then(() => edit({ kind: 'style', overlay, field, value: null })), Promise.resolve()));
    } else {
      void edit({ kind: 'look', overlay, name });
    }
  };
  const tileKeys = (event: KeyboardEvent<HTMLDivElement>): void => {
    const all = [null, ...names];
    const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[event.key];
    if (step === undefined) {
      return;
    }
    event.preventDefault();
    const next = all[(all.indexOf(chosen) + step + all.length) % all.length] ?? null;
    choose(next);
    tiles.current?.querySelectorAll<HTMLElement>('[role="radio"]')[all.indexOf(next)]?.focus();
  };
  const lookMenu = (name: string): MenuEntry[] => [
    ...(many ? [{ kind: 'action' as const, id: 'all', text: t('OverlayPageText.UseOnAllOverlays'), onRun: () => void edit({ kind: 'look', overlay: null, name }) }] : []),
    { kind: 'action', id: 'rename', text: t('UiText.RenameStyle'), onRun: () => setAsking({ kind: 'rename', name }) },
    { kind: 'action', id: 'export', text: t('UiText.ExportStyle'), onRun: () => void bridge()?.exportSavedLook(name) },
    { kind: 'divider', id: 'end' },
    { kind: 'action', id: 'delete', text: t('UiText.DeleteStyle'), onRun: () => void bridge()?.deleteLook(name) },
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
    ...(many ? [{ kind: 'action' as const, id: 'all', text: t('WindowText.CopyLookToAll'), onRun: () => void copyToAll() }, { kind: 'divider' as const, id: 'files' }] : []),
    { kind: 'action', id: 'import', text: t('UiText.ImportStyle'), onRun: () => void news.importLook(overlay, null) },
    { kind: 'action', id: 'export', text: t('UiText.ExportStyle'), onRun: () => void bridge()?.exportLook(overlay) },
  ];
  const tile = (name: string | null): ReactNode => {
    const on = name === chosen;
    const shown = name === null ? t('OverlayPageText.DefaultLook') : name;
    return (
      <button type="button" role="radio" aria-checked={on} aria-label={t('OverlayPageText.LookTile', shown)} tabIndex={on ? 0 : -1} className={`t-look${on ? ' t-on' : ''}`} onClick={() => choose(name)}>
        <Sample look={name === null ? {} : (looks[name] ?? {})} label={label} value={value} />
        <span className="t-look-name">{shown}</span>
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
      <div ref={tiles} className="t-looks" role="radiogroup" aria-label={t('UiText.LookField')} style={{ gridTemplateColumns: `repeat(${Math.max(4, names.length + 1)}, minmax(0, 1fr))` }} onKeyDown={tileKeys}>
        {tile(null)}
        {names.map((name) => (
          <span key={name} className="t-look-wrap">
            {tile(name)}
            <Menu trigger={<Button kind="subtle" className="t-look-more" label={t('OverlayPageText.LookActions', name)} icon={<Icon name="more" />} />} entries={lookMenu(name)} />
          </span>
        ))}
      </div>
      {asking?.kind === 'save' && (
        <NameDialog title={t('UiText.SaveAsMyStyle')} main={t('UiText.Save')} start={t('OverlayPageText.MyLook')} hint={t('OverlayPageText.LookKeeps')} onName={async (name) => (await bridge()?.saveLook(overlay, name)) ?? null} onClose={() => setAsking(null)} />
      )}
      {asking?.kind === 'rename' && (
        <NameDialog title={t('OverlayPageText.RenameLook')} main={t('UiText.Rename')} start={asking.name} onName={async (name) => (await bridge()?.renameLook(asking.name, name)) ?? null} onClose={() => setAsking(null)} />
      )}
    </div>
  );
}

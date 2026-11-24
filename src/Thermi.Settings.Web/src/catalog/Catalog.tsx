import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import type { CatalogView, SensorKind } from '../bridge/bridge.g';
import { bridge, type WindowData } from '../bridge/useWindow';
import { Button, Chips, Dropdown, Icon, Panel, SearchField, ToggleChip } from '../kit';
import { overlayName } from '../list/rows';
import { useText, type TextKey } from '../texts/Texts';
import { CatalogRows } from './CatalogRows';
import { useCatalog } from './useCatalog';
import './catalog.css';

/** What the catalog was opened for: to add to an overlay. */
export interface CatalogAsk {
  overlay: number;
}

export interface CatalogProps {
  data: WindowData;
  ask: CatalogAsk | null;
  onClose: () => void;
  /** Adds a sensor to an overlay; with keep, the catalog stays open. */
  onAdd: (sensor: string, overlay: number, keep: boolean) => Promise<void>;
  /** Says a change to Narrator. */
  announce: (text: string) => void;
}

// The prototype's chips, in its order.
const chipKinds: SensorKind[] = ['temperature', 'load', 'fan', 'control', 'power', 'clock', 'data', 'throughput'];
const kindKey = (kind: SensorKind): TextKey => `SensorKind.${kind.charAt(0).toUpperCase()}${kind.slice(1)}`;

/** How long the search waits for the typing to stop before Narrator says the count, as the prototype. */
const sayAfter = 500;

/** The last catalog the C# side gave, so the list stays while the next search is read. */
function useLastCatalog(search: string, kinds: SensorKind[], all: boolean, version: number): CatalogView | null {
  const view = useCatalog(search, kinds, all, version);
  const [last, setLast] = useState(view);
  if (view !== null && view !== last) {
    setLast(view);
  }
  return view ?? last;
}

interface TitleProps {
  data: WindowData;
  to: number;
  onTo: (overlay: number) => void;
}

/** "Add to Taskbar", its overlay a picker when there are several. */
function CatalogTitle({ data, to, onTo }: TitleProps) {
  const t = useText();
  const overlays = data.settings.overlays.map((overlay, index) => ({ value: String(index), text: overlayName(overlay, index, t) }));
  if (overlays.length < 2) {
    return <h2 className="t-cat-title">{t('CatalogText.AddTo', overlays[0]?.text ?? '')}</h2>;
  }
  // The picker stands where the translation puts the overlay's name.
  const [before, after] = t('CatalogText.AddTo', '\u0000').split('\u0000');
  return (
    <h2 className="t-cat-title">
      {before}
      <Dropdown inline label={t('CatalogText.CatalogOverlay')} choices={overlays} value={String(to)} onChange={(value) => onTo(Number(value))} />
      {after}
    </h2>
  );
}

/** In place of the rows: no sensor read yet, or none for the search, with a word to try. */
function CatalogNone({ typed, suggestion }: { typed: string; suggestion: string | null }) {
  const t = useText();
  const hint = typed === '' ? t('CatalogText.CatalogEmptyHint') : suggestion === null ? null : t('CatalogText.TryOneWord', suggestion);
  return (
    <div className="t-cat-empty">
      {typed === '' ? t('CatalogText.CatalogEmpty') : t('CatalogText.NoMatchFor', typed)}
      {hint === null ? null : ` ${hint}`}
    </div>
  );
}

/** Says the count once the typing stops, or that nothing matches and the word to try. */
function useSaidCount(typed: string, view: CatalogView | null, announce: (text: string) => void): string {
  const t = useText();
  const count = view?.count ?? 0;
  const counted = count === 1 ? t('CatalogText.OneSensor') : t('CatalogText.SensorCount', count);
  const suggestion = view?.suggestion ?? null;
  const said = count > 0 ? t('CatalogText.CountSaid', counted) : `${t('CatalogText.NoMatchFor', typed)} ${suggestion === null ? '' : t('CatalogText.TryOneWord', suggestion)}`;
  useEffect(() => {
    if (typed === '') {
      return;
    }
    const timer = setTimeout(() => announce(said), sayAfter);
    return () => clearTimeout(timer);
  }, [typed, said, announce]);
  return counted;
}

function CatalogBody({ data, ask, onClose, onAdd, announce }: CatalogProps & { ask: CatalogAsk }) {
  const t = useText();
  const countId = useId();
  const search = useRef<HTMLInputElement>(null);
  const results = useRef<HTMLDivElement>(null);
  const [typed, setTyped] = useState('');
  const [chip, setChip] = useState('all');
  const [showAll, setShowAll] = useState(false);
  const [to, setTo] = useState(ask.overlay);
  const items = data.settings.overlays.flatMap((overlay) => overlay.items);
  const view = useLastCatalog(typed, chip === 'all' ? [] : [chip as SensorKind], showAll, data.version);
  const counted = useSaidCount(typed, view, announce);
  const hidden = view?.hidden ?? 0;

  useEffect(() => {
    queueMicrotask(() => search.current?.focus());
  }, []);

  const shownBy = (sensor: string): string[] => items.filter((item) => item.sensor === sensor || data.view.items[item.id]?.auto === sensor).map((item) => item.name ?? item.id);
  const handlePick = (sensor: string, keep: boolean): void => {
    void onAdd(sensor, to, keep);
    if (keep) {
      search.current?.focus();
    }
  };
  const handleRename = (sensor: string, name: string): void => {
    void bridge()?.edit({ kind: 'sensorName', sensor, name });
  };
  const first = view?.groups[0]?.sensors[0];
  const handleSearchKey = (event: KeyboardEvent<HTMLInputElement>): void => {
    const run: Partial<Record<string, () => void>> = {
      Escape: typed === '' ? undefined : () => setTyped(''),
      ArrowDown: () => results.current?.querySelector<HTMLElement>('[role="option"]')?.focus(),
      Enter: first === undefined ? undefined : () => handlePick(first.id, event.ctrlKey),
    };
    const action = run[event.key];
    if (action !== undefined) {
      event.preventDefault();
      action();
    }
  };
  return (
    <>
      <div className="t-cat-h">
        <div>
          <CatalogTitle data={data} to={to} onTo={setTo} />
          <div className="t-cat-sub">{t('UiText.CatalogLive')}</div>
        </div>
        <Button kind="subtle" label={t('UiText.Close')} icon={<Icon name="close" className="t-cat-close" />} onClick={onClose} />
      </div>
      <SearchField ref={search} label={t('UiText.CatalogSearch')} placeholder={t('UiText.CatalogSearch')} clearLabel={t('CatalogText.ClearSearch')} describedBy={countId} value={typed} onChange={setTyped} onKeyDown={handleSearchKey} />
      <Chips label={t('CatalogText.KindField')} value={chip} onChange={setChip} choices={[{ value: 'all', text: t('CatalogText.AllKinds') }, ...chipKinds.map((kind) => ({ value: kind, text: t(kindKey(kind)) }))]} />
      <div className="t-cat-count-row">
        <div className="t-cat-count" id={countId}>
          {hidden > 0 ? t('CatalogText.HiddenCount', counted, hidden) : counted}
        </div>
        {(hidden > 0 || showAll) && <ToggleChip text={t('CatalogText.ShowAll')} pressed={showAll} onChange={setShowAll} />}
      </div>
      {view !== null && view.count === 0 ? (
        <CatalogNone typed={typed} suggestion={view.suggestion} />
      ) : (
        <CatalogRows ref={results} groups={view?.groups ?? []} shownBy={shownBy} onPick={handlePick} onRename={handleRename} onLeaveUp={() => search.current?.focus()} />
      )}
    </>
  );
}

/**
 * The catalog of every sensor the service reads, from the window's right edge: a search, the type chips, and the
 * sensors by device, live. Enter adds the first match, Down goes into the list, Ctrl+Enter adds and stays, and Esc
 * clears the search, then closes.
 */
export function Catalog(props: CatalogProps) {
  const t = useText();
  return (
    <Panel open={props.ask !== null} label={t('UiText.Catalog')} onClose={props.onClose}>
      {props.ask !== null && <CatalogBody {...props} ask={props.ask} />}
    </Panel>
  );
}

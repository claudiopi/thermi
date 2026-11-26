import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import type { CatalogView, SensorKind } from '../bridge/bridge.g';
import { bridge, type WindowData } from '../bridge/useWindow';
import { Button, Chips, Dropdown, Icon, Panel, SearchField, ToggleChip } from '../kit';
import { overlayName } from '../list/rows';
import { useText, type TextKey } from '../texts/Texts';
import { CatalogRows } from './CatalogRows';
import { shownBy } from './shownBy';
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

// The prototype's chips, in its order, with voltages beside power.
const chipKinds: SensorKind[] = ['temperature', 'load', 'fan', 'control', 'power', 'voltage', 'clock', 'data', 'throughput'];
const kindKey = (kind: SensorKind): TextKey => `SensorKind.${kind.charAt(0).toUpperCase()}${kind.slice(1)}`;

/** How long the search waits for the typing to stop before Narrator says the count, as the prototype. */
const sayAfter = 500;

/** The search's keys that act on the list: Down into it, Enter to add its first, Ctrl+Enter to add it and stay. */
type ListKey = 'ArrowDown' | 'Enter' | 'Control+Enter';

/**
 * The last catalog the C# side gave, so the list stays while the next search is read, and whether it answers this one:
 * a key that acts on the list waits for that.
 */
function useLastCatalog(search: string, kinds: SensorKind[], all: boolean, version: number): { view: CatalogView | null; fresh: boolean } {
  const view = useCatalog(search, kinds, all, version);
  const [last, setLast] = useState(view);
  if (view !== null && view !== last) {
    setLast(view);
  }
  return { view: view ?? last, fresh: view !== null };
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
  const said =
    count > 0
      ? t('CatalogText.CountSaid', counted)
      : `${t('CatalogText.NoMatchFor', typed)} ${suggestion === null ? '' : t('CatalogText.TryOneWord', suggestion)}`;
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
  const asked = chip === 'all' ? [] : [chip as SensorKind];
  const { view, fresh } = useLastCatalog(typed, asked, showAll, data.changes);
  // Down or Enter typed before the list answers what is typed, as fast typing does, act on that answer, not the last.
  const waiting = useRef<ListKey | null>(null);
  const counted = useSaidCount(typed, view, announce);
  const hidden = view?.hidden ?? 0;
  const kinds = [{ value: 'all', text: t('CatalogText.AllKinds') }, ...chipKinds.map((kind) => ({ value: kind, text: t(kindKey(kind)) }))];

  useEffect(() => {
    queueMicrotask(() => search.current?.focus());
  }, []);

  const shownByItems = (sensor: string): string[] => shownBy(data.settings, data.view.items, sensor, t);
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
  const goDown = (): void => results.current?.querySelector<HTMLElement>('[role="option"]')?.focus();
  const add = (answer: CatalogView | null, key: ListKey): void => {
    const one = answer?.groups[0]?.sensors[0];
    if (one !== undefined) {
      handlePick(one.id, key === 'Control+Enter');
    }
  };
  // Down waits for the answer's rows to show; Enter adds the answer's first as it comes. The last key waiting wins.
  useEffect(() => {
    if (fresh && waiting.current === 'ArrowDown') {
      waiting.current = null;
      goDown();
    }
  }, [fresh]);
  const wait = (key: ListKey): void => {
    waiting.current = key;
    if (key !== 'ArrowDown') {
      void bridge()
        ?.catalog(typed, asked, showAll)
        .then((answer) => {
          if (waiting.current === key) {
            waiting.current = null;
            add(answer, key);
          }
        });
    }
  };
  const handleSearchKey = (event: KeyboardEvent<HTMLInputElement>): void => {
    const key = event.key === 'Enter' && event.ctrlKey ? 'Control+Enter' : event.key;
    const onList = key === 'ArrowDown' || ((key === 'Enter' || key === 'Control+Enter') && (!fresh || first !== undefined));
    if (key === 'Escape' && typed !== '') {
      event.preventDefault();
      setTyped('');
    } else if (onList) {
      event.preventDefault();
      if (!fresh) {
        wait(key);
      } else if (key === 'ArrowDown') {
        goDown();
      } else {
        add(view, key);
      }
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
      <SearchField
        ref={search}
        label={t('UiText.CatalogSearch')}
        placeholder={t('UiText.CatalogSearch')}
        clearLabel={t('CatalogText.ClearSearch')}
        describedBy={countId}
        value={typed}
        onChange={setTyped}
        onKeyDown={handleSearchKey}
      />
      {/* The chips, or in a panel too short for them and some rows, a dropdown of the same choices; the sheet picks one. */}
      <div className="t-cat-chips">
        <Chips label={t('CatalogText.KindField')} value={chip} onChange={setChip} choices={kinds} />
      </div>
      <div className="t-cat-kinds">
        <Dropdown label={t('CatalogText.KindField')} value={chip} onChange={setChip} choices={kinds} />
      </div>
      <div className="t-cat-count-row">
        <div className="t-cat-count" id={countId}>
          {hidden > 0 ? t('CatalogText.HiddenCount', counted, hidden) : counted}
        </div>
        {(hidden > 0 || showAll) && <ToggleChip text={t('CatalogText.ShowAll')} pressed={showAll} onChange={setShowAll} />}
      </div>
      {view !== null && view.count === 0 ? (
        <CatalogNone typed={typed} suggestion={view.suggestion} />
      ) : (
        <CatalogRows
          ref={results}
          groups={view?.groups ?? []}
          shownBy={shownByItems}
          onPick={handlePick}
          onRename={handleRename}
          onLeaveUp={() => search.current?.focus()}
        />
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

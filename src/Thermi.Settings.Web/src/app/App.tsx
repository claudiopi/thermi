import { useEffect, useRef, useState, useSyncExternalStore, type DragEvent } from 'react';
import type { Edit } from '../bridge/bridge.g';
import { bridge, useEdit, useWindowData, type WindowData } from '../bridge/useWindow';
import { Catalog, type CatalogAsk } from '../catalog/Catalog';
import { ItemList } from '../list/ItemList';
import { itemKey, overlayKey, overlayName, settingsKey } from '../list/rows';
import { overlayOutcome } from '../overlay/overlayEdits';
import { Bars } from '../status/Bars';
import { BrokenPage } from '../status/BrokenPage';
import { NewsProvider, useWindowNews } from '../status/useNews';
import { TextsProvider, useText, type TextKey } from '../texts/Texts';
import { ErrorBoundary } from './ErrorBoundary';
import { ShortcutSheet } from './Dialogs';
import { Pane } from './Pane';
import { focusPane } from './paneFocus';
import { AppName, TitleRow } from './TitleRow';
import { useDesktop } from './useDesktop';
import { useShortcuts } from './useShortcuts';
import './app.css';

const narrowQuery = matchMedia('(max-width: 859.98px)');
const subscribeNarrow = (changed: () => void): (() => void) => {
  narrowQuery.addEventListener('change', changed);
  return () => narrowQuery.removeEventListener('change', changed);
};

const rowOf = (key: string): HTMLElement | null => document.querySelector<HTMLElement>(`[data-row="${CSS.escape(key)}"]`);

/** The selected row: what was chosen while it still exists, else the first item, else the first overlay. */
function useSelection(data: WindowData | null): [string, (key: string) => void] {
  const [chosen, setChosen] = useState<string | null>(null);
  if (data === null) {
    return [chosen ?? '', setChosen];
  }
  const { overlays } = data.settings;
  const exists = (key: string): boolean =>
    key === settingsKey || overlays.some((overlay, index) => key === overlayKey(index) || overlay.items.some((item) => key === itemKey(item.id) || key === `p:${index}:${item.id}`));
  const first = overlays[0]?.items[0];
  const fallback = first === undefined ? overlayKey(0) : itemKey(first.id);
  return [chosen !== null && exists(chosen) ? chosen : fallback, setChosen];
}

/** The overlay of the selected row, which the catalog adds to unless its picker says otherwise. */
function overlayOfRow(data: WindowData, key: string): number {
  const own = /^[op]:(\d+)/.exec(key);
  if (own !== null) {
    return Number(own[1]);
  }
  return Math.max(0, data.settings.overlays.findIndex((overlay) => overlay.items.some((item) => key === itemKey(item.id))));
}

function Window({ data }: { data: WindowData }) {
  const t = useText();
  const edit = useEdit();
  const narrow = useSyncExternalStore(subscribeNarrow, () => narrowQuery.matches);
  const [selected, select] = useSelection(data);
  const [folded, setFolded] = useState<ReadonlySet<string>>(new Set());
  const [listOpen, setListOpen] = useState(false);
  const [catalog, setCatalog] = useState<CatalogAsk | null>(null);
  const [sheet, setSheet] = useState(false);
  const [announced, announce] = useState('');
  const [labelAsked, askLabel] = useState(0);
  const [pointedRow, pointRow] = useState<string | null>(null);
  const nav = useRef<HTMLButtonElement>(null);
  const folds = narrow && !listOpen;
  // Where the focus goes once React has drawn what an action changed: the pane, the list's button, or a row.
  const [focusAsk, askFocus] = useState<{ to: string; count: number } | null>(null);
  const focusTo = (to: string): void => askFocus((before) => ({ to, count: (before?.count ?? 0) + 1 }));
  const focusDone = useRef(0);
  // A row an edit makes shows only once the data comes back, so the ask waits for it across refreshes.
  useEffect(() => {
    if (focusAsk === null || focusDone.current === focusAsk.count) {
      return;
    }
    const target = focusAsk.to === 'pane' ? null : focusAsk.to === 'nav' ? nav.current : rowOf(focusAsk.to);
    if (focusAsk.to === 'pane') {
      focusPane();
    } else if (target === null) {
      return;
    } else {
      target.focus();
    }
    focusDone.current = focusAsk.count;
  }, [focusAsk, data.version]);

  // Widened while the list's button had the focus: the button goes, so the focus goes to the list's row.
  useEffect(() => {
    if (!narrow && document.activeElement === document.body) {
      rowOf(selected)?.focus();
    }
  }, [narrow, selected]);

  const closeList = (): void => {
    setListOpen(false);
  };
  const openList = (): void => {
    setListOpen(true);
    focusTo(selected);
  };
  const onSelect = (key: string, how: 'keys' | 'pointer'): void => {
    select(key);
    if (how === 'pointer' && narrow) {
      closeList();
      focusTo('nav');
    }
  };
  const onEnter = (key: string): void => {
    select(key);
    closeList();
    focusTo('pane');
  };
  // An overlay made, merged or deleted takes the selection, on its row, or on the page in a narrow window.
  const goToOverlay = (index: number, said: string): void => {
    announce(said);
    select(overlayKey(index));
    focusTo(narrow ? 'pane' : overlayKey(index));
  };
  const runOverlayEdit = (one: Edit): void => {
    const outcome = overlayOutcome(one, data.settings.overlays, t);
    void edit(one).then((refusal) => {
      if (refusal !== null) {
        announce(t(refusal as TextKey));
      } else if (outcome.select === null) {
        announce(outcome.say);
      } else {
        goToOverlay(outcome.select, outcome.say);
      }
    });
  };
  const toSettings = (): void => {
    select(settingsKey);
    focusTo(narrow ? 'pane' : settingsKey);
  };
  const openCatalog = (): void => setCatalog({ overlay: overlayOfRow(data, selected) });
  // The new item is the overlay's one the settings did not hold before; it is selected, shown and, once the catalog
  // closes, focused in the list.
  const addToOverlay = async (sensor: string, overlay: number, keep: boolean): Promise<void> => {
    const before = new Set(data.settings.overlays[overlay]?.items.map((item) => item.id));
    const after = await edit({ kind: 'addItem', overlay, sensor }).then(async (refused) => (refused === null ? bridge()?.settings() : undefined));
    const target = after?.overlays[overlay];
    const added = target?.items.find((item) => !before.has(item.id));
    if (target === undefined || added === undefined) {
      return;
    }
    const key = itemKey(added.id);
    setFolded((folds) => new Set([...folds].filter((one) => one !== overlayKey(overlay))));
    select(key);
    announce(t('CatalogText.Added', added.name ?? added.id, overlayName(target, overlay, t)));
    if (!keep) {
      // A narrow window folds the list away, so the new item's page takes the focus instead of its row.
      setCatalog(null);
      closeList();
      focusTo(narrow ? 'pane' : key);
    }
  };
  // The toast's Undo is the window's, which needs the toast in turn.
  const undoFromToast = useRef<() => void>(() => undefined);
  const news = useWindowNews(() => undoFromToast.current());
  // An undo or redo says what it changed back and shows it: its item, or its overlay.
  const stepBack = async (how: 'undo' | 'redo', fromToast = false): Promise<void> => {
    const undone = await bridge()?.[how]();
    if (undone === null || undone === undefined) {
      return;
    }
    news.api.show({ message: undone.said });
    const key = undone.item === null ? (undone.overlay === null ? null : overlayKey(undone.overlay)) : itemKey(undone.item);
    if (key !== null) {
      select(key);
    }
    // The toast's Undo goes with the toast, so the focus goes to what came back.
    if (fromToast) {
      focusTo(key ?? selected);
    }
  };
  useEffect(() => {
    undoFromToast.current = () => void stepBack('undo', true);
  });
  // A look file dropped on the window is worn by the selected overlay, as one pasted.
  const dragOver = (event: DragEvent): void => {
    if (event.dataTransfer.types.includes('Files')) {
      event.preventDefault();
    }
  };
  const drop = (event: DragEvent): void => {
    const file = event.dataTransfer.files[0];
    if (file !== undefined) {
      event.preventDefault();
      void file.text().then((text) => news.api.importLook(overlayOfRow(data, selected), text));
    }
  };
  // A value or an overlay's edge clicked on the desktop, or what the window opened on: its row unfolds and is selected.
  const pickOnDesktop = (overlay: number, item: string | null): void => {
    const key = item === null ? overlayKey(overlay) : itemKey(item);
    setFolded((folds) => new Set([...folds].filter((one) => one !== overlayKey(overlay) && !one.startsWith(`p:${overlay}:`))));
    select(key);
    closeList();
    focusTo(narrow ? 'pane' : key);
  };
  const pointedValue = useDesktop(
    { overlay: selected === settingsKey ? null : overlayOfRow(data, selected), selected: selected.startsWith(itemKey('')) ? selected.slice(itemKey('').length) : null, pointed: pointedRow },
    (pick) => pickOnDesktop(pick.overlay, pick.item),
  );
  useShortcuts({
    undo: () => void stepBack('undo'),
    redo: () => void stepBack('redo'),
    paste: () => void news.api.pasteLook(overlayOfRow(data, selected)),
    add: openCatalog,
    find: () => (narrow && !listOpen ? openList() : rowOf(selected)?.focus()),
    settings: toSettings,
    sheet: () => setSheet(true),
    escape: () => {
      if (narrow && listOpen) {
        closeList();
        focusTo('nav');
      }
    },
  });

  const width = narrow ? 'narrow' : 'wide';
  return (
    <NewsProvider value={news.api}>
    <div className={`t-window t-${width}${listOpen && narrow ? ' t-list-open' : ''}`} onDragOver={dragOver} onDrop={drop}>
      <TitleRow
        mark={data.view.mark}
        narrow={narrow}
        listOpen={listOpen}
        onShowList={() => (listOpen ? closeList() : openList())}
        steps={data.steps}
        onUndo={() => void stepBack('undo')}
        onRedo={() => void stepBack('redo')}
        canAddOverlay={data.settings.overlays.length < 4}
        onNewOverlay={() => runOverlayEdit({ kind: 'newOverlay' })}
        onShortcuts={() => setSheet(true)}
        onExit={() => void bridge()?.exitThermi()}
        navRef={nav}
      />
      <Bars data={data} onDriverInstalled={() => focusTo(selected)} />
      <nav className="t-side" hidden={folds}>
        <ItemList
          data={data}
          selected={selected}
          onSelect={onSelect}
          onEnter={onEnter}
          folded={folded}
          onFold={(key, open) => setFolded((before) => new Set([...before].filter((one) => one !== key).concat(open ? [] : [key])))}
          onEditLabel={(item) => {
            select(itemKey(item));
            closeList();
            askLabel((count) => count + 1);
          }}
          onAddItem={openCatalog}
          announce={announce}
          onRemoved={(name) => news.api.show({ message: t('WindowText.Removed', name), undo: true })}
          onOverlayEdit={runOverlayEdit}
          pointed={pointedValue}
          onPoint={pointRow}
        />
      </nav>
      {narrow && listOpen && (
        <div
          className="t-smoke"
          aria-hidden="true"
          onPointerDown={(event) => {
            event.preventDefault();
            closeList();
            focusTo('nav');
          }}
        />
      )}
      <Pane data={data} selected={selected} inert={narrow && listOpen} labelAsked={labelAsked} onOverlayEdit={runOverlayEdit} />
      <div className="t-announce" aria-live="polite">
        {announced}
      </div>
      <Catalog data={data} ask={catalog} onClose={() => setCatalog(null)} onAdd={addToOverlay} announce={announce} />
      <ShortcutSheet data={data} open={sheet} onClose={() => setSheet(false)} />
      {news.toast}
    </div>
    </NewsProvider>
  );
}

/** The settings window's page: the title row's content, the list and the pane, once the bridge gave its data. */
export function App() {
  const data = useWindowData();
  let shown = <main />;
  if (data?.problem != null) {
    // A file that cannot be used shows only what is wrong and its fixes: no list, no undo, no shortcuts.
    shown = (
      <div className="t-window t-wide t-broken-window">
        <header className="t-title">
          <AppName mark={data.view.mark} />
        </header>
        <BrokenPage problem={data.problem} line={data.status.problem} />
      </div>
    );
  } else if (data !== null) {
    shown = <Window data={data} />;
  }
  return (
    <TextsProvider texts={data?.texts ?? {}}>
      <ErrorBoundary>{shown}</ErrorBoundary>
    </TextsProvider>
  );
}

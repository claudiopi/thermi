import { createContext, use, useEffect, useState, type ReactNode } from 'react';
import type { Bridge } from '../bridge/bridge.g';
import { bridge } from '../bridge/useWindow';
import { LinkButton, Toast } from '../kit';
import { NameDialog } from './NameDialog';
import { useText, type TextKey } from '../texts/Texts';

/** Passing news for the toast: what happened, with Undo, and Save for a look worn from a file. */
export interface News {
  message: string;
  undo?: boolean;
  /** The overlay wearing a look from a file, which "Save as my look" names. */
  save?: { overlay: number; name: string };
  /** A way out of what happened, offered before Undo, as "Hide labels" for an overlay that no longer fits. */
  fix?: { text: string; run: () => void };
}

/** How long a toast stays, unless the pointer or the focus holds it. */
const lasts = 6_000;

export interface NewsApi {
  show: (news: News) => void;
  /** Has the overlay wear a look file: its text, or the one Windows' dialog picks when there is none. */
  importLook: (overlay: number, text: string | null) => Promise<void>;
  /** Has the overlay wear the look file Windows' clipboard holds, as C# reads it. */
  pasteLook: (overlay: number) => Promise<void>;
}

const NewsContext = createContext<NewsApi>({ show: () => undefined, importLook: async () => undefined, pasteLook: async () => undefined });

export const NewsProvider = NewsContext;

export function useNews(): NewsApi {
  return use(NewsContext);
}

/** The window's toast and what feeds it; Undo in it runs `onUndo`. */
export function useWindowNews(onUndo: () => void): { api: NewsApi; toast: ReactNode } {
  const t = useText();
  const [news, setNews] = useState<News | null>(null);
  const [held, setHeld] = useState(false);
  const [saving, setSaving] = useState<News['save'] | null>(null);
  useEffect(() => {
    if (news === null || held) {
      return;
    }
    const gone = setTimeout(() => setNews(null), lasts);
    return () => clearTimeout(gone);
  }, [news, held]);

  const show = (next: News): void => {
    setHeld(false);
    setNews(next);
  };
  // A cancelled file dialog answers as an applied look does, so only a changed look says so.
  const wear = async (overlay: number, run: (from: Bridge) => Promise<string | null>): Promise<void> => {
    const from = bridge();
    if (from === null) {
      return;
    }
    const before = JSON.stringify((await from.settings()).overlays[overlay]?.style);
    const refusal = await run(from);
    if (refusal !== null) {
      show({ message: t(refusal as TextKey) });
      return;
    }
    const style = (await from.settings()).overlays[overlay]?.style;
    if (JSON.stringify(style) !== before) {
      const name = style?.name ?? t('OverlayPageText.MyLook');
      show({ message: t('WindowText.LookApplied', name), undo: true, save: { overlay, name } });
    }
  };
  const undo = (): void => {
    setNews(null);
    onUndo();
  };
  const save = (): void => {
    setSaving(news?.save ?? null);
    setNews(null);
  };

  const fix = (): void => {
    news?.fix?.run();
    setNews(null);
  };

  const actions =
    news?.undo === true || news?.save !== undefined || news?.fix !== undefined ? (
      <>
        {news.save !== undefined && <LinkButton onClick={save}>{t('WindowText.SaveLook')}</LinkButton>}
        {news.fix !== undefined && <LinkButton onClick={fix}>{news.fix.text}</LinkButton>}
        {news.undo === true && (
          <LinkButton label={t('UiText.Undo')} keys="Control+Z" onClick={undo}>
            {t('WindowText.UndoLink')}
          </LinkButton>
        )}
      </>
    ) : undefined;
  const toast = (
    <>
      <Toast className="t-toast-room" message={news?.message} actions={actions} onHold={setHeld} />
      {saving != null && (
        <NameDialog
          title={t('UiText.SaveAsMyStyle')}
          main={t('UiText.Save')}
          start={saving.name}
          hint={t('OverlayPageText.LookKeeps')}
          onName={async (name) => (await bridge()?.saveLook(saving.overlay, name)) ?? null}
          onClose={() => setSaving(null)}
        />
      )}
    </>
  );
  const importLook = (overlay: number, text: string | null): Promise<void> => wear(overlay, (from) => from.importLook(overlay, text));
  const pasteLook = (overlay: number): Promise<void> => wear(overlay, (from) => from.pasteLook(overlay));
  return { api: { show, importLook, pasteLook }, toast };
}

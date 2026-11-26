import { useEffect, useId, useRef, useState, type RefObject } from 'react';
import type { FanFinderRefusal, FinderView, Item } from '../bridge/bridge.g';
import { bridge, type WindowData } from '../bridge/useWindow';
import { useFinder } from '../catalog/useCatalog';
import { Button, Icon, Progress } from '../kit';
import { useText, type TextKey } from '../texts/Texts';

export interface FinderRowProps {
  data: WindowData;
  item: Item;
  /** Makes the item show the fan found. */
  onUseFan: (sensor: string) => void;
}

type Shown = 'idle' | 'asking' | 'running' | 'done';

const shownOf = (finder: FinderView | null): Shown => (finder?.phase === 'running' ? 'running' : finder?.phase === 'done' ? 'done' : 'idle');
const refusalKey = (refusal: FanFinderRefusal): TextKey =>
  `FanFinderRefusal.${refusal.replace(/(^|-)(\w)/g, (_, _dash: string, letter: string) => letter.toUpperCase())}`;
const putAway = (): void => void bridge()?.cancelFind();

/**
 * The focus follows the run: from Find it to Cancel, back to Find it, and to the result's first button when it is
 * done, while it is in the row, or was on a button the run took away.
 */
function useFocusFollows(shown: Shown, row: RefObject<HTMLDivElement | null>): void {
  const was = useRef(shown);
  useEffect(() => {
    if (was.current === shown) {
      return;
    }
    was.current = shown;
    const active = document.activeElement;
    if (active === null || active === document.body || row.current?.contains(active) === true) {
      row.current?.querySelector<HTMLElement>('button:not([aria-disabled="true"])')?.focus();
    }
  }, [shown, row]);
}

function FinderIdle({ refusal, onStart }: { refusal: FanFinderRefusal | undefined; onStart: () => void }) {
  const t = useText();
  const hint = useId();
  return (
    <div className="t-finder-idle">
      <span id={hint} className="t-finder-hint">
        {refusal === undefined ? t('CatalogText.FindCpuFanHint') : t(refusalKey(refusal))}
      </span>
      <Button className="t-finder-small" describedBy={hint} disabled={refusal !== undefined} onClick={onStart}>
        {t('CatalogText.FindIt')}
      </Button>
    </div>
  );
}

// The load is loud and takes the CPU for a while, so it starts only once the person says so.
function FinderAsking({ onStart, onCancel }: { onStart: () => void; onCancel: () => void }) {
  const t = useText();
  const ask = useId();
  return (
    <div className="t-finder-idle">
      <span id={ask} className="t-finder-hint">
        {t('CatalogText.FinderAsk')}
      </span>
      <Button className="t-finder-small" kind="accent" describedBy={ask} onClick={onStart}>
        {t('CatalogText.LoadCpu')}
      </Button>
      <Button className="t-finder-small" onClick={onCancel}>
        {t('UiText.Cancel')}
      </Button>
    </div>
  );
}

function FinderRunning({ finder, onCancel }: { finder: FinderView | null; onCancel: () => void }) {
  const t = useText();
  return (
    <div className="t-finder-run">
      <div className="t-finder-head">
        <span className="t-finder-msg">{finder?.text ?? t('UiText.FindingCpuFan')}</span>
        <Button className="t-finder-small" onClick={onCancel}>
          {t('UiText.Cancel')}
        </Button>
      </div>
      <Progress label={t('UiText.FindingCpuFan')} {...(finder?.progress === undefined ? {} : { value: finder.progress })} />
    </div>
  );
}

interface FinderDoneProps {
  /** What it found, worded; or why it found nothing. */
  found: string;
  detail: string | null;
  /** The fan to use, unless the item uses it already or none was found. */
  fan: string | null;
  onUseFan: (sensor: string) => void;
}

function FinderDone({ found, detail, fan, onUseFan }: FinderDoneProps) {
  const t = useText();
  const handleUse = (): void => {
    if (fan !== null) {
      onUseFan(fan);
    }
    putAway();
  };
  return (
    <div className={`t-finder-done${detail === null && fan === null ? '' : ' t-finder-ok'}`}>
      <Icon name={detail === null && fan === null ? 'info' : 'success'} className="t-finder-icon" />
      <span className="t-finder-result">
        <b>{found}</b>
        {detail === null ? null : ` ${detail}`}
      </span>
      {fan !== null && (
        <Button className="t-finder-small" onClick={handleUse}>
          {t('CatalogText.UseIt')}
        </Button>
      )}
      <Button kind="subtle" className="t-finder-small" label={t('CatalogText.Dismiss')} icon={<Icon name="close" />} onClick={putAway} />
    </div>
  );
}

/** What the run found, worded, and the fan to use, unless the item uses it already. */
function useOutcome(data: WindowData, item: Item, finder: FinderView | null): Omit<FinderDoneProps, 'onUseFan'> {
  const t = useText();
  const pick = finder?.pick ?? null;
  const fan = data.sensors.sensors.find((sensor) => sensor.id === pick);
  const found = fan === undefined ? (finder?.text ?? '') : t('FanFinderOutcome.Found', fan.name);
  // On Auto, the item uses the fan Auto picks.
  const used = pick !== null && (item.sensor ?? data.view.items[item.id]?.auto) === pick;
  return { found, detail: used ? t('CatalogText.AlreadyUsed') : (finder?.detail ?? null), fan: used ? null : pick };
}

/**
 * "Find it", under the CPU fan's sensor: it loads the CPU for up to 30 s and watches which fan answers, with its
 * progress, Cancel and the fan it found in place. The C# side runs it and words where it stands.
 */
export function FinderRow({ data, item, onUseFan }: FinderRowProps) {
  const t = useText();
  const row = useRef<HTMLDivElement>(null);
  const finder = useFinder(data.changes);
  const [asking, ask] = useState(false);
  const run = shownOf(finder);
  const shown = asking && run === 'idle' ? 'asking' : run;
  const [spoken, say] = useState('');
  useFocusFollows(shown, row);
  const { found, detail, fan } = useOutcome(data, item, finder);
  const handleStart = (): void => {
    ask(false);
    say(t('CatalogText.FinderStarted'));
    void bridge()?.findCpuFan();
  };
  const handleCancel = (): void => {
    say(t('CatalogText.FinderStopped'));
    putAway();
  };
  return (
    <div ref={row} className="t-finder">
      {shown === 'idle' && <FinderIdle refusal={finder?.refusal} onStart={() => ask(true)} />}
      {shown === 'asking' && <FinderAsking onStart={handleStart} onCancel={() => ask(false)} />}
      {shown === 'running' && <FinderRunning finder={finder} onCancel={handleCancel} />}
      {shown === 'done' && <FinderDone found={found} detail={detail} fan={fan} onUseFan={onUseFan} />}
      <span className="t-announce" aria-live="polite">
        {shown === 'done' ? found : spoken}
      </span>
    </div>
  );
}

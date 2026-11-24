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

type Shown = 'idle' | 'running' | 'done';

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

/**
 * "Find it", under the CPU fan's sensor: it loads the CPU for up to 30 s and watches which fan answers, with its
 * progress, Cancel and the fan it found in place. The C# side runs it and words where it stands.
 */
export function FinderRow({ data, item, onUseFan }: FinderRowProps) {
  const t = useText();
  const row = useRef<HTMLDivElement>(null);
  const finder = useFinder(data.changes);
  const shown = shownOf(finder);
  const [spoken, say] = useState('');
  useFocusFollows(shown, row);

  const pick = finder?.pick ?? null;
  const fan = data.sensors.sensors.find((sensor) => sensor.id === pick);
  const found = fan === undefined ? (finder?.text ?? '') : t('FanFinderOutcome.Found', fan.name);
  const used = pick !== null && item.sensor === pick;
  const detail = used ? t('CatalogText.AlreadyUsed') : (finder?.detail ?? null);
  const handleStart = (): void => {
    say(t('CatalogText.FinderStarted'));
    void bridge()?.findCpuFan();
  };
  const handleCancel = (): void => {
    say(t('CatalogText.FinderStopped'));
    putAway();
  };
  return (
    <div ref={row} className="t-finder">
      {shown === 'idle' && <FinderIdle refusal={finder?.refusal} onStart={handleStart} />}
      {shown === 'running' && <FinderRunning finder={finder} onCancel={handleCancel} />}
      {shown === 'done' && <FinderDone found={found} detail={detail} fan={used ? null : pick} onUseFan={onUseFan} />}
      <span className="t-announce" aria-live="polite">
        {shown === 'done' ? found : spoken}
      </span>
    </div>
  );
}

import type { Monitor, Overlay, SnapSpot, Value } from '../bridge/bridge.g';
import { useEdit, type WindowData } from '../bridge/useWindow';
import { Expander } from '../app/Expander';
import { SwitchLine } from '../app/SwitchLine';
import { Card, CardRow, Dropdown } from '../kit';
import { useNews } from '../status/useNews';
import { useText, type TextKey } from '../texts/Texts';
import { MonitorMap } from './MonitorMap';
import { placedOf, positionHint, snapAt } from './placeModel';
import { usePlaces } from './useOverlayReads';

export interface PlacementProps {
  data: WindowData;
  overlay: number;
  /** The displays, the main one first. */
  shown: readonly Monitor[];
  lockOpen: boolean;
  onLockOpen: (open: boolean) => void;
}

const visibilities = [
  ['always', 'Visibility.Always'],
  ['hide-in-fullscreen', 'Visibility.HideInFullscreen'],
  ['only-in-fullscreen', 'Visibility.OnlyInFullscreen'],
] as const;

/** What "Lock, click-through and OLED care" says closed: the ones on, the fade included, or All off. */
function lockSummary(one: Overlay, say: (key: TextKey, ...blanks: string[]) => string): string {
  const on = (
    [
      [one.locked, 'OverlayPageText.Locked'],
      [one.clickThrough, 'UiText.ClickThrough'],
      [one.oled, 'OverlayPageText.OledCare'],
      [one.fade, 'OverlayPageText.Fade'],
    ] as const
  ).flatMap(([set, key]) => (set === true ? [say(key)] : []));
  return on.length === 0 ? say('OverlayPageText.AllOff') : on.reduce((joined, next) => say('OverlayPageText.SummaryJoin', joined, next));
}

export function Placement({ data, overlay, shown, lockOpen, onLockOpen }: PlacementProps) {
  const t = useText();
  const edit = useEdit();
  const news = useNews();
  const places = usePlaces(data.changes);
  const one = data.settings.overlays[overlay];
  if (one === undefined) {
    return null;
  }
  const set = (field: string, value: Value): void => void edit({ kind: 'overlay', overlay, field, value });
  const snapTo = (spot: SnapSpot, display: Monitor): void =>
    void edit({ kind: 'overlay', overlay, field: 'snap', value: snapAt(spot, display) }).then((refusal) => {
      if (refusal !== null) {
        news.show({ message: t(refusal as TextKey) });
      }
    });
  const placed = placedOf(one, shown);
  const vertical = one.style?.orientation === 'vertical';
  const hidden = one.hidden === true;
  // A hidden overlay shows only here, so the map says it, dimmed where it sits.
  const hint = positionHint(placed, vertical, shown, places[overlay] ?? null);
  const said = [hidden ? t('UiText.OverlayHidden') : null, hint === null ? null : t(hint)].filter((part) => part !== null).join(' · ');
  return (
    <section className="t-col t-col-place" aria-label={t('OverlayPageText.PlacementSection')}>
      <h3 className="t-pane-h">{t('OverlayPageText.PlacementSection')}</h3>
      <div className="t-place-rows">
        <div className="t-card t-map-card">
          <CardRow label={t('UiText.MonitorMap')} {...(said === '' ? {} : { description: said })} />
          <MonitorMap
            shown={shown}
            placed={placed}
            place={places[overlay] ?? null}
            vertical={vertical}
            hidden={hidden}
            onSnap={snapTo}
          />
        </div>
        <Card>
          <CardRow label={t('OverlayPageText.WhenToShowField')}>
            <Dropdown
              className="t-when-box"
              label={t('OverlayPageText.WhenToShowField')}
              choices={visibilities.map(([value, text]) => ({ value, text: t(text) }))}
              value={one.visibility ?? 'always'}
              onChange={(value) => set('visibility', value === 'always' ? null : value)}
            />
          </CardRow>
        </Card>
      </div>
      <Expander label={t('OverlayPageText.LockGroup')} hint={lockSummary(one, t)} hintWhenOpen open={lockOpen} onOpen={onLockOpen}>
        <SwitchLine label={t('UiText.LockPosition')} checked={one.locked === true} onChange={(value) => set('locked', value ? true : null)} />
        <SwitchLine
          label={t('UiText.ClickThrough')}
          description={t('OverlayPageText.ClickThroughHint')}
          checked={one.clickThrough === true}
          onChange={(value) => set('clickThrough', value ? true : null)}
        />
        <SwitchLine
          label={t('OverlayPageText.OledCare')}
          description={t('OverlayPageText.OledHint')}
          checked={one.oled === true}
          onChange={(value) => set('oled', value ? true : null)}
        />
        <SwitchLine
          label={t('OverlayPageText.Fade')}
          description={t('OverlayPageText.FadeHint')}
          checked={one.fade === true}
          onChange={(value) => set('fade', value ? true : null)}
        />
      </Expander>
    </section>
  );
}

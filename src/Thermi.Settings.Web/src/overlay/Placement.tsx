import type { Edit, Monitor, Overlay, SnapSpot, Value } from '../bridge/bridge.g';
import { useEdit, type WindowData } from '../bridge/useWindow';
import { Expander } from '../app/Expander';
import { SwitchLine } from '../app/SwitchLine';
import { Button, Card, CardRow, Dropdown, Icon } from '../kit';
import { useText, type TextKey } from '../texts/Texts';
import { MonitorMap } from './MonitorMap';
import { placedOf, snapAt, type Placed } from './placeModel';

export interface PlacementProps {
  data: WindowData;
  overlay: number;
  /** The displays, the main one first. */
  shown: readonly Monitor[];
  lockOpen: boolean;
  onLockOpen: (open: boolean) => void;
  onOverlayEdit: (edit: Edit) => void;
}

const visibilities = [
  ['always', 'Visibility.Always'],
  ['hide-in-fullscreen', 'Visibility.HideInFullscreen'],
  ['only-in-fullscreen', 'Visibility.OnlyInFullscreen'],
] as const;

/**
 * What the Position row says under its name: how a free overlay moves, why a taskbar spot floats beside a taskbar on a
 * side or one that hides, or where a vertical one goes.
 */
function positionHint(placed: Placed | null, vertical: boolean, shown: readonly Monitor[]): TextKey | null {
  if (placed === null) {
    return 'OverlayPageText.FreeHint';
  }
  if (!placed.spot.startsWith('taskbar')) {
    return null;
  }
  const floats = shown[placed.display]?.floats;
  if (floats !== undefined) {
    return floats === 'side' ? 'OverlayPageText.TaskbarOnSide' : 'OverlayPageText.TaskbarHides';
  }
  return vertical ? 'OverlayPageText.AboveTaskbar' : null;
}

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

export function Placement({ data, overlay, shown, lockOpen, onLockOpen, onOverlayEdit }: PlacementProps) {
  const t = useText();
  const edit = useEdit();
  const one = data.settings.overlays[overlay];
  if (one === undefined) {
    return null;
  }
  const set = (field: string, value: Value): void => void edit({ kind: 'overlay', overlay, field, value });
  const placed = placedOf(one, shown);
  const vertical = one.style?.orientation === 'vertical';
  const hidden = one.hidden === true;
  // A hidden overlay shows only here, so the map says it, dimmed where it sits.
  const hint = positionHint(placed, vertical, shown);
  const said = [hidden ? t('UiText.OverlayHidden') : null, hint === null ? null : t(hint)].filter((part) => part !== null).join(' · ');
  return (
    <section className="t-col" aria-label={t('OverlayPageText.PlacementSection')}>
      <h3 className="t-pane-h">{t('OverlayPageText.PlacementSection')}</h3>
      <div className="t-rows">
        <div className="t-card t-map-card">
          <CardRow label={t('UiText.MonitorMap')} {...(said === '' ? {} : { description: said })} />
          <MonitorMap shown={shown} placed={placed} vertical={vertical} hidden={hidden} onSnap={(spot: SnapSpot, display: Monitor) => set('snap', snapAt(spot, display))} />
        </div>
        <Card>
          <CardRow label={t('OverlayPageText.WhenToShowField')}>
            <Dropdown className="t-when-box" label={t('OverlayPageText.WhenToShowField')} choices={visibilities.map(([value, text]) => ({ value, text: t(text) }))} value={one.visibility ?? 'always'} onChange={(value) => set('visibility', value === 'always' ? null : value)} />
          </CardRow>
        </Card>
      </div>
      <Expander label={t('OverlayPageText.LockGroup')} hint={lockSummary(one, t)} hintWhenOpen open={lockOpen} onOpen={onLockOpen}>
        <SwitchLine label={t('UiText.LockPosition')} checked={one.locked === true} onChange={(value) => set('locked', value ? true : null)} />
        <SwitchLine label={t('UiText.ClickThrough')} description={t('OverlayPageText.ClickThroughHint')} checked={one.clickThrough === true} onChange={(value) => set('clickThrough', value ? true : null)} />
        <SwitchLine label={t('OverlayPageText.OledCare')} description={t('OverlayPageText.OledHint')} checked={one.oled === true} onChange={(value) => set('oled', value ? true : null)} />
        <SwitchLine label={t('OverlayPageText.Fade')} description={t('OverlayPageText.FadeHint')} checked={one.fade === true} onChange={(value) => set('fade', value ? true : null)} />
      </Expander>
      {data.settings.overlays.length > 1 && (
        <div className="t-pane-foot">
          <Button kind="subtle" icon={<Icon name="delete" />} onClick={() => onOverlayEdit({ kind: 'deleteOverlay', overlay })}>
            {t('UiText.DeleteThisOverlay')}
          </Button>
        </div>
      )}
    </section>
  );
}

import { useState } from 'react';
import type { Edit } from '../bridge/bridge.g';
import { useEdit, type WindowData } from '../bridge/useWindow';
import { PageTitle } from '../app/PageTitle';
import { focusPane } from '../app/paneFocus';
import { Button, Icon } from '../kit';
import { overlayName } from '../list/rows';
import { useText } from '../texts/Texts';
import { Appearance } from './Appearance';
import { Placement } from './Placement';
import { displays, placeText } from './placeModel';
import { useMonitors } from './useOverlayReads';
import './overlay.css';

export interface OverlayPageProps {
  data: WindowData;
  overlay: number;
  onOverlayEdit: (edit: Edit) => void;
}

/**
 * An overlay that does not fit beside the taskbar's apps keeps its width over them, so the window says so, with the way
 * out while its labels show. Its button goes with them, so the focus goes to the pane's first control.
 */
function FitNotice({ overlay, labels }: { overlay: number; labels: boolean }) {
  const t = useText();
  const edit = useEdit();
  const hideLabels = (): void => void edit({ kind: 'style', overlay, field: 'noLabels', value: true }).then(focusPane);
  return (
    <div className="t-crowd" role="status">
      <Icon name="info" className="t-crowd-icon" />
      <span className="t-crowd-text">{t('FitText.DoesNotFit')}</span>
      {labels && <Button onClick={hideLabels}>{t('FitText.HideLabels')}</Button>}
    </div>
  );
}

export function OverlayPage({ data, overlay, onOverlayEdit }: OverlayPageProps) {
  const t = useText();
  const edit = useEdit();
  const shown = displays(useMonitors(data.changes));
  // The expanders stay as they were from overlay to overlay, as More options does from item to item.
  const [textOpen, setTextOpen] = useState(false);
  const [colorsOpen, setColorsOpen] = useState(false);
  const [lockOpen, setLockOpen] = useState(false);
  const one = data.settings.overlays[overlay];
  if (one === undefined) {
    return null;
  }
  const count = one.items.length;
  const place = placeText(one, shown, t);
  const name = overlayName(one, overlay, t);
  return (
    <div className="t-ov t-page">
      <div className="t-page-head t-ov-head">
        <div className="t-head-text">
          <PageTitle name={name} onRename={(next) => next.trim() !== '' && next !== name && void edit({ kind: 'overlay', overlay, field: 'name', value: next.trim() })} />
          <div className="t-subtitle">{count === 1 ? t('OverlayPageText.OverlayHeadOneItem', place) : t('OverlayPageText.OverlayHeadItems', count, place)}</div>
        </div>
        <Button icon={<Icon name={one.hidden === true ? 'show' : 'hide'} />} onClick={() => onOverlayEdit({ kind: 'overlay', overlay, field: 'hidden', value: one.hidden === true ? null : true })}>
          {t(one.hidden === true ? 'UiText.ShowThisOverlay' : 'UiText.HideThisOverlay')}
        </Button>
      </div>
      {data.crowded.includes(overlay) && <FitNotice overlay={overlay} labels={one.style?.noLabels !== true} />}
      <div className="t-cols">
        <Appearance data={data} overlay={overlay} textOpen={textOpen} onTextOpen={setTextOpen} colorsOpen={colorsOpen} onColorsOpen={setColorsOpen} />
        <Placement data={data} overlay={overlay} shown={shown} lockOpen={lockOpen} onLockOpen={setLockOpen} onOverlayEdit={onOverlayEdit} />
      </div>
    </div>
  );
}

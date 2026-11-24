import { useState } from 'react';
import type { Edit } from '../bridge/bridge.g';
import { useEdit, type WindowData } from '../bridge/useWindow';
import { PageTitle } from '../app/PageTitle';
import { focusPane } from '../app/paneFocus';
import { Button, Icon } from '../kit';
import { overlayName } from '../list/rows';
import { useText } from '../texts/Texts';
import { Appearance } from './Appearance';
import { ColorsCard } from './ColorsCard';
import { Placement } from './Placement';
import { TextAndShape } from './TextAndShape';
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
  // Closed on each overlay's page, as More options is on each item's: the pane keys the page by its overlay.
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
          <PageTitle
            name={name}
            onRename={(next) =>
              next.trim() !== '' && next !== name && void edit({ kind: 'overlay', overlay, field: 'name', value: next.trim() })
            }
          />
          <div className="t-subtitle">
            {count === 1 ? t('OverlayPageText.OverlayHeadOneItem', place) : t('OverlayPageText.OverlayHeadItems', count, place)}
          </div>
        </div>
        <Button
          icon={<Icon name={one.hidden === true ? 'show' : 'hide'} />}
          onClick={() => onOverlayEdit({ kind: 'overlay', overlay, field: 'hidden', value: one.hidden === true ? null : true })}
        >
          {t(one.hidden === true ? 'UiText.ShowThisOverlay' : 'UiText.HideThisOverlay')}
        </Button>
      </div>
      {data.crowded.includes(overlay) && <FitNotice overlay={overlay} labels={one.style?.noLabels !== true} />}
      {/* The text and colors sit under Placement on a wide pane, so the two columns run about as long. */}
      <div className="t-cols t-ov-cols">
        <Appearance data={data} overlay={overlay} />
        <section className="t-col t-col-text" aria-label={t('OverlayPageText.TextSection')}>
          <h3 className="t-pane-h">{t('OverlayPageText.TextSection')}</h3>
          <TextAndShape overlay={overlay} style={one.style} open={textOpen} onOpen={setTextOpen} />
          <ColorsCard overlay={overlay} style={one.style} open={colorsOpen} onOpen={setColorsOpen} />
        </section>
        <Placement data={data} overlay={overlay} shown={shown} lockOpen={lockOpen} onLockOpen={setLockOpen} />
      </div>
      {data.settings.overlays.length > 1 && (
        <div className="t-pane-foot">
          <Button kind="subtle" icon={<Icon name="delete" />} onClick={() => onOverlayEdit({ kind: 'deleteOverlay', overlay })}>
            {t('UiText.DeleteThisOverlay')}
          </Button>
        </div>
      )}
    </div>
  );
}

import type { ChartView, Edit } from '../bridge/bridge.g';
import { PageTitle } from '../app/PageTitle';
import { useChart, useEdit, type WindowData } from '../bridge/useWindow';
import { Chart } from '../chart/Chart';
import { Button, CardStack, Icon, Menu, type MenuEntry } from '../kit';
import { itemName, overlayName } from '../list/rows';
import { useText } from '../texts/Texts';
import { placeOf } from './itemModel';
import { ItemRows } from './ItemRows';
import { MoreOptions } from './MoreOptions';
import './item.css';

export interface ItemPageProps {
  data: WindowData;
  id: string;
  labelAsked: number;
  moreOpen: boolean;
  onMoreOpen: (open: boolean) => void;
}

function chartMenu(chart: ChartView, item: string, edit: (edit: Edit) => void, say: ReturnType<typeof useText>): MenuEntry[] {
  return [
    { kind: 'action', id: 'reset', text: say('UiText.ResetThreshold'), onRun: () => edit({ kind: 'resetLines', item }) },
    ...(chart.warning === null
      ? [
          {
            kind: 'action' as const,
            id: 'warning',
            text: say('UiText.AddWarningLine'),
            onRun: () => edit({ kind: 'addLine', item, line: 'warning' }),
          },
        ]
      : []),
    ...(chart.critical === null
      ? [
          {
            kind: 'action' as const,
            id: 'critical',
            text: say('UiText.AddCriticalLine'),
            onRun: () => edit({ kind: 'addLine', item, line: 'critical' }),
          },
        ]
      : []),
  ];
}

/** An item's page: its name, where it lives, its chart and its rows, in two columns on a wide pane. */
export function ItemPage({ data, id, labelAsked, moreOpen, onMoreOpen }: ItemPageProps) {
  const t = useText();
  const edit = useEdit();
  const chart = useChart(id, data.changes);
  const placed = placeOf(data.settings, data.view, id);
  if (placed === null) {
    return null;
  }
  const { item, overlay } = placed;
  const where = data.settings.overlays[overlay];
  const name = itemName(item);
  const run = (one: Edit): void => void edit(one);
  return (
    <div className={`t-item${chart === null ? ' t-no-chart' : ''}`}>
      <div className="t-page-head t-item-head">
        <div className="t-head-text">
          <PageTitle
            name={name}
            onRename={(next) => next.trim() !== '' && next !== name && run({ kind: 'item', item: id, field: 'name', value: next.trim() })}
          />
          <div className="t-subtitle">
            {t('UiText.OverlayPlace', where === undefined ? '' : overlayName(where, overlay, t), t('UiText.PillName', placed.pill))}
          </div>
        </div>
        {chart !== null && item.meter !== undefined && (
          <Menu
            // In words, so it never reads as the title row's "…" just above it.
            trigger={
              <Button kind="subtle" icon={<Icon name="more" />}>
                {t('UiText.ChartActions')}
              </Button>
            }
            entries={chartMenu(chart, id, run, t)}
          />
        )}
      </div>
      {chart !== null && <Chart item={id} name={name} chart={chart} edit={edit} metered={item.meter !== undefined} />}
      <div className="t-item-rows">
        <CardStack>
          <ItemRows data={data} placed={placed} edit={run} labelAsked={labelAsked} />
          <MoreOptions data={data} placed={placed} edit={edit} open={moreOpen} onOpen={onMoreOpen} />
        </CardStack>
      </div>
    </div>
  );
}

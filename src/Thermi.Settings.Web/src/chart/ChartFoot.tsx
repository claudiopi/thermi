import type { ReactNode } from 'react';
import type { ChartView, Line } from '../bridge/bridge.g';
import { Button, Icon } from '../kit';
import { useText } from '../texts/Texts';

const lines: Line[] = ['warning', 'critical'];
const adds = { warning: 'UiText.AddWarningLine', critical: 'UiText.AddCriticalLine' } as const;

interface ChartFootProps {
  chart: ChartView;
  width: number;
  metered: boolean;
  options: ReactNode;
  onReset: () => void;
  onAdd: (line: Line) => void;
  onAddFocus: (line: Line) => void;
}

function Adds({ chart, onAdd, onAddFocus }: Pick<ChartFootProps, 'chart' | 'onAdd' | 'onAddFocus'>) {
  const t = useText();
  return (
    <div className="t-adds">
      {lines.map((one) =>
        chart[one] !== null ? null : (
          <Button
            key={one}
            data-focus={`add-${one}`}
            onFocus={() => onAddFocus(one)}
            className="t-small-button"
            icon={<Icon name="add" />}
            onClick={() => onAdd(one)}
          >
            {t(adds[one])}
          </Button>
        ),
      )}
    </div>
  );
}

/** Under the plot: its time axis, what the minutes held with the chart's options, the lines' fixes and their Add buttons. */
export function ChartFoot({ chart, width, metered, options, onReset, onAdd, onAddFocus }: ChartFootProps) {
  const t = useText();
  return (
    <div className="t-chart-foot" style={{ width: `${width}px` }}>
      {chart.empty === null && (
        <div className="t-axis" aria-hidden="true">
          <span>{chart.startText}</span>
          <span>{t('UiText.ChartNow')}</span>
        </div>
      )}
      {(chart.stats !== null || options !== undefined) && (
        <div className="t-stats-row">
          <div className="t-stats">{chart.stats}</div>
          {options}
        </div>
      )}
      {chart.fires !== null && (
        <div className="t-fires">
          <span>{t('UiText.FiresMostly')}</span>
          <Button className="t-small-button" onClick={onReset}>
            {chart.fires}
          </Button>
        </div>
      )}
      <div className="t-pushed" aria-live="polite">
        {chart.pushed ?? ''}
      </div>
      {metered && lines.some((one) => chart[one] === null) && <Adds chart={chart} onAdd={onAdd} onAddFocus={onAddFocus} />}
    </div>
  );
}

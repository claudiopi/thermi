import type { Value } from '../bridge/bridge.g';
import { asOneStep, useEdit, type WindowData } from '../bridge/useWindow';
import { Button, Card, CardRow, CardStack, Dropdown, Segmented, Slider } from '../kit';
import { useText } from '../texts/Texts';
import { LookCard } from './LookCard';
import { enumChoices as choicesOf, shownStyle } from './lookModel';
import { Preview } from './Preview';

export interface AppearanceProps {
  data: WindowData;
  overlay: number;
}

const percent = (value: number): string => `${Math.round(value * 100)} %`;

export function Appearance({ data, overlay }: AppearanceProps) {
  const t = useText();
  const edit = useEdit();
  const { overlays } = data.settings;
  const one = overlays[overlay];
  if (one === undefined) {
    return null;
  }
  const shown = shownStyle(one.style);
  const style = (field: string, value: Value): void => void edit({ kind: 'style', overlay, field, value });
  const size = one.size ?? 1;
  const themeOnAll = (): void =>
    void asOneStep(() =>
      overlays.reduce<Promise<unknown>>(
        (done, _, index) => done.then(() => edit({ kind: 'style', overlay: index, field: 'pills', value: shown.pills })),
        Promise.resolve(),
      ),
    );
  return (
    <section className="t-col t-col-look" aria-label={t('OverlayPageText.AppearanceSection')}>
      <h3 className="t-pane-h">{t('OverlayPageText.AppearanceSection')}</h3>
      <Preview data={data} overlay={overlay} />
      <LookCard data={data} overlay={overlay} />
      <CardStack>
        <Card>
          <CardRow label={t('UiText.SizeField')}>
            <Slider
              className="t-slider"
              label={t('UiText.SizeField')}
              min={0.75}
              max={2}
              step={0.05}
              value={size}
              valueText={percent(size)}
              onChange={(value) => void edit({ kind: 'overlay', overlay, field: 'size', value: Math.round(value * 100) / 100 })}
            />
          </CardRow>
        </Card>
        <Card>
          <CardRow label={t('OverlayPageText.ThemeField')} wraps>
            <Segmented
              label={t('OverlayPageText.ThemeField')}
              choices={choicesOf(t, 'PillTheme', ['dark', 'light', 'system'])}
              value={shown.pills}
              onChange={(value) => style('pills', value)}
            />
            {overlays.length > 1 && (
              <Button kind="subtle" onClick={themeOnAll}>
                {t('OverlayPageText.UseOnAllOverlays')}
              </Button>
            )}
          </CardRow>
        </Card>
        <Card>
          <CardRow label={t('UiText.LayoutField')} wraps>
            <Dropdown
              className="t-layout-box"
              label={t('UiText.LayoutField')}
              choices={choicesOf(t, 'PillLayout', ['pills', 'each', 'one'])}
              value={shown.layout}
              onChange={(value) => style('layout', value)}
            />
            <Segmented
              label={t('UiText.DirectionField')}
              choices={choicesOf(t, 'Orientation', ['horizontal', 'vertical'])}
              value={shown.orientation}
              onChange={(value) => style('orientation', value)}
            />
          </CardRow>
        </Card>
        <Card>
          <CardRow label={t('UiText.GraphField')} description={t('UiText.GraphHint')}>
            <Dropdown
              className="t-layout-box"
              label={t('UiText.GraphField')}
              choices={choicesOf(t, 'GraphStyle', ['auto', 'none', 'bars', 'line'])}
              value={one.graph ?? 'auto'}
              onChange={(value) => void edit({ kind: 'overlay', overlay, field: 'graph', value: value === 'auto' ? null : value })}
            />
          </CardRow>
        </Card>
        <Card>
          <CardRow label={t('UiText.BackgroundField')} {...(shown.background < 0.3 ? { description: t('OverlayPageText.ShadowBelow') } : {})}>
            <Slider
              className="t-slider-short"
              label={t('UiText.BackgroundField')}
              min={0}
              max={1}
              step={0.05}
              value={shown.background}
              valueText={percent(shown.background)}
              onChange={(value) =>
                style(
                  'opacity',
                  shown.overlay === 1
                    ? { background: Math.round(value * 100) / 100 }
                    : { background: Math.round(value * 100) / 100, overlay: shown.overlay },
                )
              }
            />
          </CardRow>
        </Card>
      </CardStack>
    </section>
  );
}

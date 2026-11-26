import type { Style, TextStyle, Value } from '../bridge/bridge.g';
import { asOneStep, useEdit } from '../bridge/useWindow';
import { Expander } from '../app/Expander';
import { SwitchLine } from '../app/SwitchLine';
import { CardRow, Dropdown, Segmented, Slider } from '../kit';
import { useText } from '../texts/Texts';
import { enumChoices as choices, shownStyle } from './lookModel';

export interface TextAndShapeProps {
  overlay: number;
  style: Style | undefined;
  open: boolean;
  onOpen: (open: boolean) => void;
}

const textValue = (text: TextStyle): Value => ({ size: text.size, weight: text.weight });

// The sizes each text offers, as the design prototype lists them; a size the file holds outside them shows too.
const labelSizes = [9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32, 36];
const valueSizes = [9, 10, 11, 12, 13, 13.5, 14, 16, 17, 18, 20, 24, 28, 32, 36];

const sizeChoices = (sizes: readonly number[], current: number) =>
  [...new Set([...sizes, current])].sort((a, b) => a - b).map((size) => ({ value: String(size), text: `${size} px` }));

function TextRow({
  label,
  weightLabel,
  sizes,
  text,
  onChange,
}: {
  label: string;
  weightLabel: string;
  sizes: readonly number[];
  text: TextStyle;
  onChange: (text: TextStyle) => void;
}) {
  const t = useText();
  return (
    <CardRow label={label} wraps>
      <Dropdown
        className="t-size-box"
        label={label}
        choices={sizeChoices(sizes, text.size)}
        value={String(text.size)}
        onChange={(size) => onChange({ ...text, size: Number(size) })}
      />
      <Dropdown
        className="t-weight-box"
        label={weightLabel}
        choices={choices(t, 'TextWeight', ['regular', 'semibold', 'bold'])}
        value={text.weight}
        onChange={(weight) => onChange({ ...text, weight: weight as TextStyle['weight'] })}
      />
    </CardRow>
  );
}

export function TextAndShape({ overlay, style, open, onOpen }: TextAndShapeProps) {
  const t = useText();
  const edit = useEdit();
  const shown = shownStyle(style);
  const set = (field: string, value: Value): void => void edit({ kind: 'style', overlay, field, value });
  // A size or weight of its own makes the look the overlay's own, which keeps both texts as they show.
  const setText = (which: 'label' | 'value', text: TextStyle): void => {
    const label = which === 'label' ? text : shown.label;
    const value = which === 'value' ? text : shown.value;
    void asOneStep(async () => {
      await edit({ kind: 'style', overlay, field: 'preset', value: 'custom' });
      await edit({ kind: 'style', overlay, field: 'label', value: textValue(label) });
      await edit({ kind: 'style', overlay, field: 'value', value: textValue(value) });
    });
  };
  return (
    <Expander label={t('OverlayPageText.TextAndShape')} hint={t('OverlayPageText.TextAndShapeHint')} hintWhenOpen open={open} onOpen={onOpen}>
      <TextRow
        label={t('UiText.LabelsField')}
        weightLabel={t('OverlayPageText.LabelWeight')}
        sizes={labelSizes}
        text={shown.label}
        onChange={(text) => setText('label', text)}
      />
      <TextRow
        label={t('UiText.ValuesField')}
        weightLabel={t('OverlayPageText.ValueWeight')}
        sizes={valueSizes}
        text={shown.value}
        onChange={(text) => setText('value', text)}
      />
      <CardRow label={t('OverlayPageText.FontField')}>
        <Dropdown
          className="t-font-box"
          label={t('OverlayPageText.FontField')}
          choices={choices(t, 'StyleFont', ['segoe', 'bahnschrift', 'consolas'])}
          value={shown.font}
          onChange={(font) => set('font', font)}
        />
      </CardRow>
      <CardRow label={t('OverlayPageText.CornersField')}>
        <Segmented
          label={t('OverlayPageText.CornersField')}
          choices={choices(t, 'CornerStyle', ['round', 'soft', 'square'])}
          value={shown.corners}
          onChange={(corners) => set('corners', corners)}
        />
      </CardRow>
      <CardRow label={t('OverlayPageText.GapField')}>
        <Slider
          className="t-slider-short"
          label={t('OverlayPageText.GapField')}
          min={0}
          max={16}
          step={1}
          value={shown.gap}
          valueText={`${shown.gap} px`}
          onChange={(gap) => set('gap', gap)}
        />
      </CardRow>
      <CardRow label={t('OverlayPageText.EffectField')}>
        <Dropdown
          className="t-effect-box"
          label={t('OverlayPageText.EffectField')}
          choices={choices(t, 'TextEffect', ['auto', 'shadow', 'outline', 'none'])}
          value={shown.effect}
          onChange={(effect) => set('effect', effect)}
        />
      </CardRow>
      <SwitchLine
        label={t('OverlayPageText.ShowLabels')}
        description={t('OverlayPageText.ShowLabelsHint')}
        checked={style?.noLabels !== true}
        onChange={(on) => set('noLabels', on ? null : true)}
      />
      <SwitchLine
        label={t('UiText.UnitsField')}
        description={t('OverlayPageText.UnitsHint')}
        checked={shown.units}
        onChange={(on) => set('units', on ? null : false)}
      />
    </Expander>
  );
}

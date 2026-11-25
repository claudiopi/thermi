import type { Edit, Item, Value } from '../bridge/bridge.g';
import type { WindowData } from '../bridge/useWindow';
import { Expander } from '../app/Expander';
import { SwitchLine } from '../app/SwitchLine';
import { CardRow, Dropdown, NumberField } from '../kit';
import { useText } from '../texts/Texts';
import { offsetRange, warnByChoices, type Placed } from './itemModel';
import { CombineButton, ValueColor } from './ValueTools';

export interface MoreOptionsProps {
  data: WindowData;
  placed: Placed;
  edit: (edit: Edit) => Promise<string | null>;
  open: boolean;
  onOpen: (open: boolean) => void;
}

const decimals = [0, 1, 2];

type SetField = (field: string, value: Value) => Promise<string | null>;

/** Only for a reading that is a plain number: it says nothing of what the value is. */
function DecimalsRow({ item, set }: { item: Item; set: SetField }) {
  const t = useText();
  return (
    <CardRow label={t('UiText.DecimalsField')}>
      <Dropdown
        className="t-option-box"
        label={t('UiText.DecimalsField')}
        choices={[{ value: 'auto', text: t('UiText.Auto') }, ...decimals.map((count) => ({ value: String(count), text: String(count) }))]}
        value={item.decimals === undefined ? 'auto' : String(item.decimals)}
        onChange={(value) => void set('decimals', value === 'auto' ? null : Number(value))}
      />
    </CardRow>
  );
}

function Options({ data, placed, edit }: Omit<MoreOptionsProps, 'open' | 'onOpen'>) {
  const t = useText();
  const { item } = placed;
  const view = data.view.items[item.id];
  const type = view?.type ?? '';
  const set: SetField = (field, value) => edit({ kind: 'item', item: item.id, field, value });
  const range = offsetRange(type);
  const judged = view?.judged === true;
  const warnBy = warnByChoices(item, view);
  const warnByChosen = (value: string): void => {
    const reading = warnBy.readings[Number(value)];
    if (reading !== undefined) {
      void set('warnBy', reading === null ? null : { ...reading });
    }
  };
  return (
    <>
      {range !== null && <DecimalsRow item={item} set={set} />}
      {view?.bits === true && (
        <SwitchLine
          label={t('UiText.Bits')}
          description={t('UiText.BitsHint')}
          checked={item.bits === true}
          onChange={(on) => void set('bits', on ? true : null)}
        />
      )}
      {warnBy.row && (
        <CardRow label={t('UiText.WarnByField')}>
          <Dropdown className="t-warn-box" label={t('UiText.WarnByField')} choices={warnBy.choices} value={warnBy.value} onChange={warnByChosen} />
        </CardRow>
      )}
      {/* Only on alert follows what thresholds judge, so an item that can warn by nothing is not offered it. */}
      {(judged || warnBy.row) && (
        <SwitchLine
          label={t('UiText.AlertOnly')}
          description={t(judged ? 'UiText.AlertOnlyHint' : 'UiText.AlertOnlyNeedsWarnBy')}
          checked={item.alertOnly === true}
          disabled={!judged}
          onChange={(on) => void set('alertOnly', on ? true : null)}
        />
      )}
      {range !== null && (
        <CardRow label={t('UiText.OffsetField')}>
          <NumberField
            label={t('UiText.OffsetField')}
            value={item.offset ?? 0}
            min={-range}
            max={range}
            {...(view?.offsetShown == null ? {} : { shown: view.offsetShown })}
            onCommit={(value) => void set('offset', value === 0 ? null : value)}
          />
        </CardRow>
      )}
      <CardRow label={t('UiText.ValueColor')}>
        <ValueColor item={item} edit={(field, value) => set(field, value)} />
      </CardRow>
      {range !== null && (
        <CardRow label={t('UiText.Combine')}>
          <CombineButton
            item={item}
            current={item.sensors ?? [item.sensor ?? data.view.items[item.id]?.auto].flatMap((id) => id ?? [])}
            type={type}
            sensors={data.sensors.sensors.filter((sensor) => sensor.type === type && data.view.values[sensor.id] !== undefined)}
            set={(field, value) => set(field, value)}
          />
        </CardRow>
      )}
    </>
  );
}

/** "More options": a row clicked as a whole that opens the rest of the item's settings under it. */
export function MoreOptions({ data, placed, edit, open, onOpen }: MoreOptionsProps) {
  const t = useText();
  // A reading that is no number holds only its value color, so the hint names only that.
  const hint = offsetRange(data.view.items[placed.item.id]?.type ?? '') === null ? 'UiText.ValueColor' : 'UiText.MoreOptionsHint';
  return (
    <Expander label={t('UiText.MoreOptions')} hint={t(hint)} open={open} onOpen={onOpen}>
      <Options data={data} placed={placed} edit={edit} />
    </Expander>
  );
}

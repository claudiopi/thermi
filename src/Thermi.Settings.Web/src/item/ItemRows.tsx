import { useEffect, useId, useRef, useState } from 'react';
import type { Edit, Item, Sensor, Value } from '../bridge/bridge.g';
import type { WindowData } from '../bridge/useWindow';
import { SwitchLine, type SwitchLineProps } from '../app/SwitchLine';
import { Card, CardRow, Dropdown, TextField } from '../kit';
import { useText } from '../texts/Texts';
import { FinderRow } from './FinderRow';
import { meterChoices, sensorChoices, type Placed } from './itemModel';

export interface ItemRowsProps {
  data: WindowData;
  placed: Placed;
  edit: (edit: Edit) => void;
  /** Counts the asks to edit the pill label, which put the focus in its box. */
  labelAsked: number;
}

/** A list long enough for the Sensor dropdown to filter. */
const filterFrom = 7;

/** The metric "Find it" looks for. */
const cpuFan = 'fan.cpu';

function LabelRow({ placed, edit, labelAsked }: Pick<ItemRowsProps, 'placed' | 'edit' | 'labelAsked'>) {
  const t = useText();
  const { item } = placed;
  const [typed, setTyped] = useState<string | null>(null);
  const box = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (labelAsked > 0) {
      box.current?.focus();
    }
  }, [labelAsked]);
  const commit = (value: string): void => {
    setTyped(null);
    const next = value.trim();
    if (next !== (item.label ?? '')) {
      edit({ kind: 'item', item: item.id, field: 'label', value: next === '' ? null : next });
    }
  };
  return (
    <Card>
      <CardRow label={t('UiText.LabelField')}>
        <TextField
          ref={box}
          className="t-label-box"
          label={t('UiText.LabelField')}
          labelShown={false}
          value={typed ?? item.label ?? ''}
          placeholder={placed.pill}
          onChange={setTyped}
          onCommit={commit}
          onCancel={() => setTyped(null)}
        />
      </CardRow>
    </Card>
  );
}

function SwitchRow(props: SwitchLineProps) {
  return (
    <Card>
      <SwitchLine {...props} />
    </Card>
  );
}

/**
 * What the Sensor row says under its name: why a running service gives the item no value, the driver first, as the
 * catalog's rows do; else the device it reads, after "Auto" when thermi picks the sensor.
 */
function sensorNoteOf(data: WindowData, item: Item, current: Sensor | undefined, t: ReturnType<typeof useText>): string | undefined {
  if (data.sensors.service === 'running' && data.view.items[item.id]?.value === null) {
    return t(data.status.driver ? 'CatalogText.RowNeedsDriver' : 'CatalogText.RowReadsNothing');
  }
  if (current === undefined) {
    return undefined;
  }
  return item.sensor === undefined ? t('UiText.AutoFollows', current.hardware) : current.hardware;
}

/** The item page's rows above More options: Sensor, Label, Meter, Notify me, Separate pill and Tray icon. */
export function ItemRows({ data, placed, edit, labelAsked }: ItemRowsProps) {
  const t = useText();
  const describe = useId();
  const sensorBox = useRef<HTMLInputElement>(null);
  const { item } = placed;
  const set = (field: string, value: Value): void => edit({ kind: 'item', item: item.id, field, value });
  const sensors = data.sensors.sensors;
  const choices = sensorChoices(item, data.view, sensors, t);
  const current = sensors.find((sensor) => sensor.id === (item.sensor ?? data.view.items[item.id]?.auto));
  const sensorNote = sensorNoteOf(data, item, current, t);
  const metered = meterChoices(item, data.view.items[item.id], t);
  const meter = (value: string): void => {
    const chosen = metered.meters[Number(value)];
    if (value === 'off') {
      set('meter', null);
    } else if (chosen !== undefined) {
      set('meter', { ...chosen });
    }
  };
  const ownOff = placed.firstOfPill && item.own !== true;
  return (
    <>
      <Card>
        <div role="group" aria-label={t('UiText.SensorField')}>
          <CardRow label={t('UiText.SensorField')} {...(sensorNote === undefined ? {} : { description: sensorNote, descriptionId: describe })}>
            <Dropdown
              ref={sensorBox}
              className="t-sensor-box"
              wide
              label={t('UiText.SensorField')}
              choices={choices}
              value={item.sensor ?? ''}
              {...(choices.length >= filterFrom ? { filter: t('UiText.FilterSensors', choices.length) } : {})}
              {...(sensorNote === undefined ? {} : { describedBy: describe })}
              onChange={(value) => set('sensor', value === '' ? null : value)}
            />
          </CardRow>
          {item.metric === cpuFan && (
            <FinderRow
              data={data}
              item={item}
              onUseFan={(fan) => {
                set('sensor', fan);
                sensorBox.current?.focus();
              }}
            />
          )}
        </div>
      </Card>
      <LabelRow placed={placed} edit={edit} labelAsked={labelAsked} />
      {metered.row && (
        <Card>
          <CardRow label={t('UiText.MeterField')}>
            <Dropdown className="t-meter-box" label={t('UiText.MeterField')} choices={metered.choices} value={metered.value} onChange={meter} />
          </CardRow>
        </Card>
      )}
      {item.threshold?.critical !== undefined && (
        <SwitchRow label={t('UiText.NotifyMe')} checked={item.notify === true} onChange={(on) => set('notify', on ? true : null)} />
      )}
      {/* The first item of a pill already starts one, so its switch shows On, locked. */}
      <SwitchRow
        label={t('UiText.OwnPill')}
        {...(ownOff ? { description: t('UiText.AlreadyOwnPill') } : {})}
        checked={item.own === true || ownOff}
        disabled={ownOff}
        onChange={(on) => set('own', on ? true : null)}
      />
      <SwitchRow label={t('UiText.OwnTrayIcon')} checked={item.tray === true} onChange={(on) => set('tray', on ? true : null)} />
    </>
  );
}


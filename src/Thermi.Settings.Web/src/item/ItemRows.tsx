import { useEffect, useId, useRef, useState } from 'react';
import type { Edit, Item, Sensor, Value } from '../bridge/bridge.g';
import { bridge, type WindowData } from '../bridge/useWindow';
import { SwitchLine, type SwitchLineProps } from '../app/SwitchLine';
import { Card, CardRow, Dropdown, TextField, type Find } from '../kit';
import { useText } from '../texts/Texts';
import { FinderRow } from './FinderRow';
import { graphChoices, offsetRange, sensorChoices, showAllSensors, type Placed } from './itemModel';

export interface ItemRowsProps {
  data: WindowData;
  placed: Placed;
  edit: (edit: Edit) => void;
  /** Counts the asks to edit the pill label, which put the focus in its box. */
  labelAsked: number;
}

/** A list long enough for the Sensor dropdown to filter. */
const filterFrom = 7;

/** The Sensor dropdown's filter, which the C# side's search decides, as it does the catalog's. */
const findSensors: Find = async (typed, among) => (await bridge()?.search(typed, among)) ?? [];

/** The metric "Find it" looks for. */
const cpuFan = 'fan.cpu';

/** While the item has no label of its own, shows the one the overlay draws for its run. */
function LabelRow({ placed, label, edit, labelAsked }: Pick<ItemRowsProps, 'placed' | 'edit' | 'labelAsked'> & { label: string }) {
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
          placeholder={label}
          onChange={setTyped}
          onCommit={commit}
          onCancel={() => setTyped(null)}
        />
      </CardRow>
    </Card>
  );
}

const stats = ['now', 'min', 'max', 'average'] as const;
const statKeys = { now: 'ItemStat.Now', min: 'ItemStat.Min', max: 'ItemStat.Max', average: 'ItemStat.Average' } as const;
const smoothing = [0, 2, 3, 4, 5, 6, 7, 8, 9, 10];

/** They say what the value is, so they sit with the sensor it reads, never in More options. */
function ShowsRows({ item, set }: { item: Item; set: (field: string, value: Value) => void }) {
  const t = useText();
  return (
    <Card>
      <CardRow label={t('UiText.StatField')}>
        <Dropdown
          className="t-option-box"
          label={t('UiText.StatField')}
          choices={stats.map((stat) => ({ value: stat, text: t(statKeys[stat]) }))}
          value={item.stat ?? 'now'}
          onChange={(value) => set('stat', value === 'now' ? null : value)}
        />
      </CardRow>
      <CardRow label={t('UiText.SmoothField')} description={t('UiText.SmoothHint')}>
        <Dropdown
          className="t-option-box"
          label={t('UiText.SmoothField')}
          choices={smoothing.map((seconds) => ({ value: String(seconds), text: seconds === 0 ? t('UndoText.Off') : t('UiText.SmoothSeconds', seconds) }))}
          value={String(item.smooth ?? 0)}
          onChange={(value) => set('smooth', value === '0' ? null : Number(value))}
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

export function ItemRows({ data, placed, edit, labelAsked }: ItemRowsProps) {
  const t = useText();
  const describe = useId();
  const sensorBox = useRef<HTMLInputElement>(null);
  const { item } = placed;
  const set = (field: string, value: Value): void => edit({ kind: 'item', item: item.id, field, value });
  const sensors = data.sensors.sensors;
  // Asked on one item, the other devices' sensors stay listed until another item's page opens.
  const [allFor, setAllFor] = useState<string | null>(null);
  const choices = sensorChoices(item, data.view, data.sensors, t, allFor === item.id);
  const current = sensors.find((sensor) => sensor.id === (item.sensor ?? data.view.items[item.id]?.auto));
  const sensorNote = sensorNoteOf(data, item, current, t);
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
              {...(choices.length >= filterFrom
                ? {
                    filter: t('UiText.FilterSensors', choices.length),
                    find: findSensors,
                    counted: (count: number) => (count === 1 ? t('CatalogText.OneSensor') : t('CatalogText.SensorCount', count)),
                    none: (typed: string) => t('CatalogText.NoMatchFor', typed),
                  }
                : {})}
              {...(sensorNote === undefined ? {} : { describedBy: describe })}
              onChange={(value) => (value === showAllSensors ? setAllFor(item.id) : set('sensor', value === '' ? null : value))}
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
      {offsetRange(data.view.items[item.id]?.type ?? '') !== null && <ShowsRows item={item} set={set} />}
      <LabelRow placed={placed} label={data.view.items[item.id]?.label ?? placed.pill} edit={edit} labelAsked={labelAsked} />
      {/* What it draws beside its value, which an item thresholds cannot judge has no level or scale for. */}
      {data.view.items[item.id]?.judged === true && (
        <Card>
          <CardRow label={t('UiText.GraphField')}>
            <Dropdown
              className="t-graph-box"
              label={t('UiText.GraphField')}
              choices={graphChoices(t)}
              value={item.graph ?? ''}
              onChange={(value) => set('graph', value === '' ? null : value)}
            />
          </CardRow>
        </Card>
      )}
      {item.threshold?.critical !== undefined && (
        <SwitchRow label={t('UiText.NotifyMe')} checked={item.notify === true} onChange={(on) => set('notify', on ? true : null)} />
      )}
      {/* The first item of a pill already starts one, so a switch that could not change is not offered. */}
      {!ownOff && <SwitchRow label={t('UiText.OwnPill')} checked={item.own === true} onChange={(on) => set('own', on ? true : null)} />}
      <SwitchRow label={t('UiText.OwnTrayIcon')} checked={item.tray === true} onChange={(on) => set('tray', on ? true : null)} />
    </>
  );
}


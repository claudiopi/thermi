import type { Item, ItemView, Meter, Sensor, SensorView, Settings, WindowView } from '../bridge/bridge.g';
import type { Choice } from '../kit';
import type { TextKey } from '../texts/Texts';

// What the item page reads from the settings and the window's view: where the item lives and the choices of its
// rows. The rules behind them are the C# side's; this only arranges what it gave.

type Say = (key: TextKey, ...blanks: (string | number)[]) => string;

export interface Placed {
  item: Item;
  overlay: number;
  /** Its pill's label, and whether the item is the first of its pill. */
  pill: string;
  firstOfPill: boolean;
}

export function placeOf(settings: Settings, view: WindowView, id: string): Placed | null {
  for (const [overlay, one] of settings.overlays.entries()) {
    const item = one.items.find((candidate) => candidate.id === id);
    const pill = view.pills[overlay]?.find((candidate) => candidate.items.includes(id));
    if (item !== undefined) {
      return { item, overlay, pill: pill?.label ?? '', firstOfPill: pill?.items[0] === id };
    }
  }
  return null;
}

const followsDevice = ['Network', 'Storage'];

/** The Sensor dropdown's last choice while other devices' sensors wait, which lists them; no sensor has it. */
export const showAllSensors = '\u0000all';

/**
 * The Sensor dropdown's choices: Auto first for an item made from a metric, then what C# offers it, its own device's
 * sensors of its type, and the other devices' once `all` asks for them, with a last choice that asks while they wait.
 */
export function sensorChoices(item: Item, view: WindowView, read: SensorView, say: Say, all = false): Choice[] {
  const { sensors } = read;
  const shows = view.items[item.id];
  const named = (sensor: Sensor): string => say('UiText.SensorOn', sensor.name, sensor.hardware);
  const auto = sensors.find((sensor) => sensor.id === shows?.auto);
  // An adapter's or a drive's metric follows a device, not a sensor, so Auto names the device it follows now.
  const follows = auto !== undefined && followsDevice.includes(auto.hardwareType) ? auto.hardware : auto?.name;
  const autoChoice: Choice[] =
    item.metric === undefined
      ? []
      : [
          {
            value: '',
            text: say('UiText.AutoFollows', follows ?? say('UiText.Auto')),
            shown: follows ?? say('UiText.Auto'),
            ...(auto === undefined ? {} : { aside: view.values[auto.id] ?? '' }),
          },
        ];
  const offer = shows?.offer ?? { first: [], more: [] };
  const byId = new Map(sensors.map((sensor) => [sensor.id, sensor]));
  const listed = [...offer.first, ...(all ? offer.more : [])].flatMap((id) => byId.get(id) ?? []);
  const ask: Choice[] = all || offer.more.length === 0 ? [] : [{ value: showAllSensors, text: say('CatalogText.ShowAll'), aside: String(offer.more.length) }];
  return [
    ...autoChoice,
    ...listed.map((sensor) => ({
      value: sensor.id,
      text: named(sensor),
      aside: view.values[sensor.id] ?? '',
      shown: sensor.name,
      detail: sensor.hardware,
    })),
    ...ask,
  ];
}

const sameMeter = (one: Meter, other: Meter): boolean => one.metric === other.metric && one.sensor === other.sensor;

/**
 * The Meter dropdown: Off, then the meters the item is offered, readings of itself alone, each by its place; a meter
 * written by hand that is none of them shows as itself, chosen, and is offered no more once left. With Off its only
 * choice, the row is not shown at all.
 */
export function meterChoices(
  item: Item,
  shows: ItemView | undefined,
  say: Say,
): { choices: Choice[]; value: string; meters: Meter[]; row: boolean } {
  const offered = shows?.meters ?? [];
  const at = item.meter === undefined ? -1 : offered.findIndex((choice) => sameMeter(choice.meter, item.meter!));
  const kept = item.meter !== undefined && at < 0 ? [{ value: 'kept', text: shows?.meterName ?? '' }] : [];
  const choices = [{ value: 'off', text: say('UiText.MeterNone') }, ...offered.map((choice, index) => ({ value: String(index), text: choice.name })), ...kept];
  return {
    choices,
    value: item.meter === undefined ? 'off' : at < 0 ? 'kept' : String(at),
    meters: offered.map((choice) => choice.meter),
    row: choices.length > 1,
  };
}

// Windows' address, time of day and time since start read as text: nothing averages, smooths, rounds, graphs or offsets them.
const readsNoNumber = ['Address', 'Time', 'Duration'];

/** The range of an item's offset, in the unit it reads, or null for a reading that is no number, which offers none of the number tools. */
export function offsetRange(type: string): number | null {
  const ranges: Record<string, number> = { Fan: 2000, Power: 500, Clock: 10000, Data: 100, SmallData: 100 };
  return readsNoNumber.includes(type) ? null : (ranges[type] ?? 20);
}

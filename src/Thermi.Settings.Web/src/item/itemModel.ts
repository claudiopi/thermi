import type { Item, ItemView, Sensor, SensorView, Settings, WarnBy, WindowView } from '../bridge/bridge.g';
import type { Choice } from '../kit';
import { enumChoices } from '../overlay/lookModel';
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

/**
 * Whether "Separate pill" is offered: not on a pill's first item, which starts one already, nor while the overlay lays its
 * items out in one bar or one pill each, which ignore it, unless it is on, so it can be turned off.
 */
export function offersOwnPill(settings: Settings, overlay: number, item: Item | undefined, firstOfPill: boolean): boolean {
  const layout = settings.overlays[overlay]?.style?.layout ?? 'pills';
  return item?.own === true || (!firstOfPill && layout === 'pills');
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

const sameReading = (one: WarnBy | undefined, other: WarnBy | undefined): boolean =>
  one?.metric === other?.metric && one?.sensor === other?.sensor && (one === undefined) === (other === undefined);

/**
 * The Warn by dropdown: the item's own reading, or Off for one thresholds cannot judge, then the other readings of itself
 * C# offers, each by what it reads; a reading written by hand that is none of them shows as itself, chosen, and is
 * offered no more once left. With one choice, the row is not shown at all.
 */
export function warnByChoices(item: Item, shows: ItemView | undefined): { choices: Choice[]; value: string; readings: (WarnBy | null)[]; row: boolean } {
  const offered = shows?.warnBy ?? [];
  const at = offered.findIndex((choice) => sameReading(choice.warnBy, item.warnBy));
  const kept = item.warnBy !== undefined && at < 0 ? [{ value: 'kept', text: shows?.warnByName ?? '' }] : [];
  const choices = [...offered.map((choice, index) => ({ value: String(index), text: choice.name })), ...kept];
  return { choices, value: at < 0 ? 'kept' : String(at), readings: offered.map((choice) => choice.warnBy ?? null), row: choices.length > 1 };
}

export function graphChoices(say: Say): Choice[] {
  return [{ value: '', text: say('UiText.GraphLikeOverlay') }, ...enumChoices(say, 'GraphStyle', ['none', 'bars', 'line'])];
}

// Windows' address, time of day and time since start read as text: nothing averages, smooths, rounds, graphs or offsets them.
const readsNoNumber = ['Address', 'Time', 'Duration'];

/** The range of an item's offset, in the unit it reads, or null for a reading that is no number, which offers none of the number tools. */
export function offsetRange(type: string): number | null {
  const ranges: Record<string, number> = { Fan: 2000, Power: 500, Clock: 10000, Data: 100, SmallData: 100 };
  return readsNoNumber.includes(type) ? null : (ranges[type] ?? 20);
}

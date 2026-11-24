import { describe, expect, test } from 'vitest';
import type { Item, ItemView, Sensor, Settings, WindowView } from '../bridge/bridge.g';
import { sayKeys } from '../testSay';
import { meterChoices, offsetRange, placeOf, sensorChoices } from './itemModel';

function sensor(id: string, type: string, hardwareType = 'Cpu', hardware = 'Ryzen'): Sensor {
  return { id, type, hardwareType, hardware, name: `${id} name`, value: 1 };
}

const sensors = [sensor('package', 'Temperature'), sensor('core', 'Temperature'), sensor('load', 'Load'), sensor('ethernet', 'Throughput', 'Network', 'Ethernet')];

function view(shows: Partial<ItemView>): WindowView {
  return { pills: [], items: { cpu: { value: null, level: null, type: 'Temperature', meters: [], meterName: null, ...shows } }, values: { package: '50 °C', ethernet: '1 MB/s' }, mark: 'calm' };
}

describe('the item page', () => {
  const settings: Settings = {
    version: 1,
    overlays: [
      { name: 'A', items: [{ id: 'gpu' }] },
      { name: 'B', items: [{ id: 'ram' }, { id: 'cpu' }] },
    ],
  };
  const pills: WindowView = { ...view({}), pills: [[{ label: 'GPU', items: ['gpu'] }], [{ label: 'Both', items: ['ram', 'cpu'] }]] };
  test.each([
    { id: 'gpu', view: pills, placed: { overlay: 0, pill: 'GPU', firstOfPill: true } },
    { id: 'cpu', view: pills, placed: { overlay: 1, pill: 'Both', firstOfPill: false } },
    { id: 'ram', view: view({}), placed: { overlay: 1, pill: '', firstOfPill: false } },
    { id: 'gone', view: pills, placed: null },
  ])('places $id', ({ id, view: shown, placed }) => {
    const found = placeOf(settings, shown, id);

    expect(found === null ? null : { overlay: found.overlay, pill: found.pill, firstOfPill: found.firstOfPill, id: found.item.id }).toEqual(placed === null ? null : { ...placed, id });
  });

  const each = (text: string, value: string, aside: string) => ({ value, text, aside });
  test.each([
    {
      item: { id: 'cpu', metric: 'cpu.temperature' },
      shows: { auto: 'package' },
      choices: [
        { value: '', text: 'UiText.AutoFollows package name', shown: 'package name', aside: '50 °C' },
        each('UiText.SensorOn package name Ryzen', 'package', '50 °C'),
        each('UiText.SensorOn core name Ryzen', 'core', ''),
      ],
    },
    {
      item: { id: 'cpu', metric: 'network.down' },
      shows: { auto: 'ethernet', type: 'Throughput' },
      choices: [{ value: '', text: 'UiText.AutoFollows Ethernet', shown: 'Ethernet', aside: '1 MB/s' }, each('UiText.SensorOn ethernet name Ethernet', 'ethernet', '1 MB/s')],
    },
    {
      item: { id: 'cpu', metric: 'cpu.load' },
      shows: { type: 'Load' },
      choices: [{ value: '', text: 'UiText.AutoFollows UiText.Auto', shown: 'UiText.Auto' }, each('UiText.SensorOn load name Ryzen', 'load', '')],
    },
    { item: { id: 'cpu', sensor: 'load' }, shows: { type: 'Load' }, choices: [each('UiText.SensorOn load name Ryzen', 'load', '')] },
  ] satisfies { item: Item; shows: Partial<ItemView>; choices: unknown[] }[])('offers the sensors of $item', ({ item, shows, choices }) => {
    expect(sensorChoices(item, view(shows), sensors, sayKeys)).toEqual(choices);
  });

  // The item's offered meters by place, Off first; a meter written by hand that is none of them shows kept.
  const fan = { meters: [{ meter: { sensor: 'fan' }, name: 'itself' }, { meter: { sensor: 'duty', auto: 'fan.cpu.duty' }, name: 'duty' }], meterName: 'hand' };
  test.each([
    { item: { id: 'cpu', sensor: 'fan' }, meter: 'off', offered: 3 },
    { item: { id: 'cpu', sensor: 'fan', meter: { sensor: 'fan' } }, meter: '0', offered: 3 },
    { item: { id: 'cpu', sensor: 'fan', meter: { sensor: 'duty' } }, meter: '1', offered: 3 },
    { item: { id: 'cpu', sensor: 'fan', meter: { sensor: 'package' } }, meter: 'kept', offered: 4 },
  ] satisfies { item: Item; meter: string; offered: number }[])('meters $item by $meter', ({ item, meter, offered }) => {
    const shown = meterChoices(item, { ...view({}).items['cpu']!, ...fan }, sayKeys);

    expect([shown.value, shown.choices.length]).toEqual([meter, offered]);
  });

  test.each([
    ['Fan', 2000],
    ['Power', 500],
    ['Clock', 10000],
    ['Data', 100],
    ['SmallData', 100],
    ['Temperature', 20],
    ['Address', null],
    ['Time', null],
    ['Duration', null],
  ])('offsets a %s by up to %s', (type, range) => {
    expect(offsetRange(type)).toBe(range);
  });
});

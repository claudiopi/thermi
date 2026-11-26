import { describe, expect, test } from 'vitest';
import type { Item, ItemView, Sensor, Settings, WindowView } from '../bridge/bridge.g';
import { sayKeys } from '../testSay';
import { offsetRange, placeOf, sensorChoices, showAllSensors, warnByChoices } from './itemModel';

function sensor(id: string, type: string, hardwareType = 'Cpu', hardware = 'Ryzen'): Sensor {
  return { id, type, hardwareType, hardware, name: `${id} name`, value: 1 };
}

const sensors = [
  sensor('package', 'Temperature'),
  sensor('core', 'Temperature'),
  sensor('load', 'Load'),
  sensor('gpuload', 'Load', 'GpuNvidia', 'RTX'),
  sensor('ethernet', 'Throughput', 'Network', 'Ethernet'),
  sensor('ramused', 'Data', 'Memory', 'Memory'),
  sensor('read', 'Data', 'Storage', 'SSD'),
];

function view(shows: Partial<ItemView>): WindowView {
  return {
    pills: [],
    items: {
      cpu: { value: null, level: null, type: 'Temperature', warnBy: [], warnByName: null, judged: true, bits: false, offer: { first: [], more: [] }, ...shows },
    },
    values: { package: '50 °C', ethernet: '1 MB/s' },
    mark: 'calm',
    colors: [],
    fitted: [],
  };
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
    { id: 'ram', view: view({}), placed: { overlay: 1, pill: '', firstOfPill: false } },
  ])('places $id', ({ id, view: shown, placed }) => {
    const found = placeOf(settings, shown, id);

    expect(found === null ? null : { overlay: found.overlay, pill: found.pill, firstOfPill: found.firstOfPill, id: found.item.id }).toEqual(
      placed === null ? null : { ...placed, id },
    );
  });

  const each = (text: string, value: string, aside: string, detail = 'Ryzen') => ({ value, text, aside, shown: `${value} name`, detail });
  // Rows: Auto names a sensor, a device or nothing, each an operand of its condition.
  test.each([
    {
      item: { id: 'cpu', metric: 'cpu.temperature' },
      shows: { auto: 'package', offer: { first: ['package', 'core'], more: [] } },
      all: false,
      choices: [
        { value: '', text: 'UiText.AutoFollows package name', shown: 'package name', aside: '50 °C' },
        each('UiText.SensorOn package name Ryzen', 'package', '50 °C'),
        each('UiText.SensorOn core name Ryzen', 'core', ''),
      ],
    },
    {
      item: { id: 'cpu', metric: 'network.down' },
      shows: { auto: 'ethernet', type: 'Throughput', offer: { first: ['ethernet'], more: [] } },
      all: false,
      choices: [
        { value: '', text: 'UiText.AutoFollows Ethernet', shown: 'Ethernet', aside: '1 MB/s' },
        each('UiText.SensorOn ethernet name Ethernet', 'ethernet', '1 MB/s', 'Ethernet'),
      ],
    },
    // R5-05: the other devices' sensors wait behind a last choice, which says how many, until it is taken.
    {
      item: { id: 'cpu', metric: 'cpu.load' },
      shows: { type: 'Load', offer: { first: ['load'], more: ['gpuload'] } },
      all: false,
      choices: [
        { value: '', text: 'UiText.AutoFollows UiText.Auto', shown: 'UiText.Auto' },
        each('UiText.SensorOn load name Ryzen', 'load', ''),
        { value: showAllSensors, text: 'CatalogText.ShowAll', aside: '1' },
      ],
    },
    {
      item: { id: 'cpu', sensor: 'load' },
      shows: { type: 'Load', offer: { first: ['load'], more: ['gpuload'] } },
      all: true,
      choices: [each('UiText.SensorOn load name Ryzen', 'load', ''), each('UiText.SensorOn gpuload name RTX', 'gpuload', '', 'RTX')],
    },
  ] satisfies { item: Item; shows: Partial<ItemView>; all: boolean; choices: unknown[] }[])(
    'offers the sensors of $item',
    ({ item, shows, all, choices }) => {
      expect(sensorChoices(item, view(shows), { service: 'running', sensors }, sayKeys, all)).toEqual(choices);
    },
  );

  // What the item is offered to warn by, its own reading first; a reading written by hand that is none of them shows
  // kept. An item offered nothing else, as a power or a clock, has no Warn by row at all, since Off would be its only choice.
  const fan = { warnBy: [{ name: 'own' }, { warnBy: { sensor: 'duty', auto: 'fan.cpu.duty' }, name: 'duty' }], warnByName: 'hand' };
  const none = { warnBy: [{ name: 'off' }], warnByName: 'hand' };
  // Rows: an item offered only Off, with and without a reading written by hand, is a requirement's only proof.
  test.each([
    { item: { id: 'cpu', sensor: 'fan', warnBy: { sensor: 'duty' } }, shows: fan, warnBy: '1', offered: 2, row: true },
    { item: { id: 'cpu', sensor: 'fan', warnBy: { metric: 'fan.cpu.duty' } }, shows: fan, warnBy: 'kept', offered: 3, row: true },
    { item: { id: 'cpu', sensor: 'fan', warnBy: { sensor: 'package' } }, shows: fan, warnBy: 'kept', offered: 3, row: true },
    { item: { id: 'cpu', sensor: 'power' }, shows: none, warnBy: '0', offered: 1, row: false },
    { item: { id: 'cpu', sensor: 'power', warnBy: { sensor: 'package' } }, shows: none, warnBy: 'kept', offered: 2, row: true },
  ] satisfies { item: Item; shows: Partial<ItemView>; warnBy: string; offered: number; row: boolean }[])(
    'warns $item by $warnBy',
    ({ item, shows, warnBy, offered, row }) => {
      const shown = warnByChoices(item, { ...view({}).items['cpu']!, ...shows });

      expect([shown.value, shown.choices.length, shown.row]).toEqual([warnBy, offered, row]);
    },
  );

  // Rows: each type's range is a number Stryker leaves as it is, and the types that read no number a static list.
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

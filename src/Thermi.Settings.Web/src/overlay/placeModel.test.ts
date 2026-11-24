import { describe, expect, test } from 'vitest';
import type { Monitor, Overlay } from '../bridge/bridge.g';
import type { TextKey } from '../texts/Texts';
import { displayName, displays, moveToDisplay, placeText, placedOf, snapAt, spotKey } from './placeModel';

function monitor(id: string, primary = false): Monitor {
  const rect = { x: 0, y: 0, width: 1920, height: 1080 };
  return { id, primary, bounds: rect, workArea: rect, scale: 1, taskbar: true };
}

const main = monitor('m1', true);
const second = monitor('m2');
const shown = [main, second];

// Texts in lower case, so that the place's first letter being raised shows.
const lower = (key: TextKey, ...blanks: (string | number)[]): string => [key.toLowerCase(), ...blanks].join(' ');

describe('where an overlay sits', () => {
  test.each([
    ['taskbar-left', 'SnapSpot.TaskbarLeft'],
    ['taskbar-center', 'SnapSpot.TaskbarCenter'],
    ['top-right', 'SnapSpot.TopRight'],
  ] as const)('names %s by %s', (spot, key) => {
    expect(spotKey(spot)).toBe(key);
  });

  test('counts the main display first, the others in the order Windows gives them', () => {
    const third = monitor('m3');

    expect(displays([second, main, third]).map((one) => one.id)).toEqual(['m1', 'm2', 'm3']);
  });

  test.each([
    { overlay: { position: { x: 5, y: 5 } }, placed: null },
    { overlay: {}, placed: { spot: 'taskbar-right', display: 0, gone: false } },
    { overlay: { snap: { spot: 'top-left' }, position: { x: 5, y: 5 } }, placed: { spot: 'top-left', display: 0, gone: false } },
    { overlay: { snap: { spot: 'top-left', monitor: 'm2' } }, placed: { spot: 'top-left', display: 1, gone: false } },
    { overlay: { snap: { spot: 'top-left', monitor: 'unplugged' } }, placed: { spot: 'top-left', display: 0, gone: true } },
  ] satisfies { overlay: Partial<Overlay>; placed: unknown }[])('places $overlay at $placed', ({ overlay, placed }) => {
    expect(placedOf({ name: 'A', items: [], ...overlay }, shown)).toEqual(placed);
  });

  test.each([
    { overlay: { snap: { spot: 'top-left' } }, display: second, snap: { spot: 'top-left', monitor: 'm2' } },
    { overlay: { snap: { spot: 'top-left', monitor: 'm2' } }, display: main, snap: { spot: 'top-left' } },
    { overlay: {}, display: second, snap: { spot: 'taskbar-right', monitor: 'm2' } },
    { overlay: { position: { x: 5, y: 5 } }, display: second, snap: { spot: 'top-right', monitor: 'm2' } },
  ] satisfies { overlay: Partial<Overlay>; display: Monitor; snap: unknown }[])('moves $overlay to $display.id at $snap', ({ overlay, display, snap }) => {
    expect(moveToDisplay({ name: 'A', items: [], ...overlay }, display)).toEqual(snap);
  });

  test('writes a spot of the map without the display when it is the main one or none', () => {
    expect([snapAt('top-left', undefined), snapAt('top-left', main), snapAt('top-left', second)]).toEqual([{ spot: 'top-left' }, { spot: 'top-left' }, { spot: 'top-left', monitor: 'm2' }]);
  });

  test.each([
    { overlay: { position: { x: 5, y: 5 } }, text: 'nametext.floating' },
    { overlay: { snap: { spot: 'top-left' } }, text: 'Snapspot.topleft' },
    { overlay: { snap: { spot: 'top-left', monitor: 'm2' } }, text: 'overlaypagetext.spotonmonitor Snapspot.topleft 2' },
  ] satisfies { overlay: Partial<Overlay>; text: string }[])('says where $overlay sits', ({ overlay, text }) => {
    expect(placeText({ name: 'A', items: [], ...overlay }, shown, lower)).toBe(text);
  });

  test('names the main display apart from the others, counting from 1', () => {
    expect([displayName(main, 0, lower), displayName(second, 2, lower)]).toEqual(['overlaypagetext.maindisplay 1', 'overlaypagetext.displayname 3']);
  });
});

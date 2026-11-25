import type { Bridge, LookSamples, Monitor, OverlayPicture, Rect, Style, WornLook } from '../bridge/bridge.g';
import { useBridgeRead } from '../bridge/useWindow';

const readMonitors = (from: Bridge): Promise<Monitor[]> => from.monitors();
const readLooks = (from: Bridge): Promise<Record<string, Style>> => from.looks();
const readWorn = (from: Bridge): Promise<(WornLook | null)[]> => from.worn();
const readPlaces = (from: Bridge): Promise<(Rect | null)[]> => from.places();
// An overlay and the room for its picture in device pixels, as one key, so a new room reads it again.
const readPreview = (from: Bridge, overlayInRoom: string): Promise<OverlayPicture | null> => {
  const [overlay = 0, room = 0] = overlayInRoom.split(' ').map(Number);
  return from.preview(overlay, room);
};
const readSamples = (from: Bridge, scale: number): Promise<LookSamples> => from.lookSamples(scale);

const noMonitors: Monitor[] = [];
const noLooks: Record<string, Style> = {};
const noPlaces: (Rect | null)[] = [];
const noneWorn: (WornLook | null)[] = [];

/** The monitors Windows has now. */
export function useMonitors(version: number): Monitor[] {
  return useBridgeRead(readMonitors, '', version) ?? noMonitors;
}

/** Where each overlay shows on the desktop, as C# works it out from the settings now. */
export function usePlaces(version: number): (Rect | null)[] {
  return useBridgeRead(readPlaces, '', version) ?? noPlaces;
}

/** The saved looks by their names. */
export function useLooks(version: number): Record<string, Style> {
  return useBridgeRead(readLooks, '', version) ?? noLooks;
}

/** The look each overlay wears; null for none. */
export function useWorn(version: number): (WornLook | null)[] {
  return useBridgeRead(readWorn, '', version) ?? noneWorn;
}

/**
 * The overlay as C# paints it now, at most `room` device pixels wide, read again with each change and each second's
 * readings; null until it answers.
 */
export function usePreview(overlay: number, room: number, version: number): OverlayPicture | null {
  return useBridgeRead(readPreview, `${overlay} ${room}`, version, true);
}

/**
 * Each look's sample, read again with each change but not each second: painting every look each second would cost the
 * idle window more than its budget.
 */
export function useLookSamples(scale: number, version: number): LookSamples | null {
  return useBridgeRead(readSamples, scale, version);
}

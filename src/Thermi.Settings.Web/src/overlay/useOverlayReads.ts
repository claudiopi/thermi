import type { Bridge, LookSamples, Monitor, OverlayPicture, Rect, Style } from '../bridge/bridge.g';
import { useBridgeRead } from '../bridge/useWindow';

const readMonitors = (from: Bridge): Promise<Monitor[]> => from.monitors();
const readLooks = (from: Bridge): Promise<Record<string, Style>> => from.looks();
const readPlaces = (from: Bridge): Promise<(Rect | null)[]> => from.places();
const readPreview = (from: Bridge, overlay: number): Promise<OverlayPicture | null> => from.preview(overlay);
const readSamples = (from: Bridge): Promise<LookSamples> => from.lookSamples();

const noMonitors: Monitor[] = [];
const noLooks: Record<string, Style> = {};
const noPlaces: (Rect | null)[] = [];

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

/** The overlay as C# paints it now, read again with each change and each second's readings; null until it answers. */
export function usePreview(overlay: number, version: number): OverlayPicture | null {
  return useBridgeRead(readPreview, overlay, version, true);
}

/**
 * Each look's sample, read again with each change but not each second: painting every look each second would cost the
 * idle window more than its budget.
 */
export function useLookSamples(version: number): LookSamples | null {
  return useBridgeRead(readSamples, '', version);
}

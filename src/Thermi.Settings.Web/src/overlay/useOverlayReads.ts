import type { Bridge, Monitor, Rect, Style } from '../bridge/bridge.g';
import { useBridgeRead } from '../bridge/useWindow';

const readMonitors = (from: Bridge): Promise<Monitor[]> => from.monitors();
const readLooks = (from: Bridge): Promise<Record<string, Style>> => from.looks();
const readPlaces = (from: Bridge): Promise<(Rect | null)[]> => from.places();

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

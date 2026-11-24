import type { Bridge, Monitor, Snap, Style } from '../bridge/bridge.g';
import { useBridgeRead } from '../bridge/useWindow';

const readMonitors = (from: Bridge): Promise<Monitor[]> => from.monitors();
const readLooks = (from: Bridge): Promise<Record<string, Style>> => from.looks();
const readRoomless = (from: Bridge, overlay: number): Promise<Snap[]> => from.roomless(overlay);

const noMonitors: Monitor[] = [];
const noLooks: Record<string, Style> = {};
const noSpots: Snap[] = [];

/** The monitors Windows has now. */
export function useMonitors(version: number): Monitor[] {
  return useBridgeRead(readMonitors, '', version) ?? noMonitors;
}

/** The saved looks by their names. */
export function useLooks(version: number): Record<string, Style> {
  return useBridgeRead(readLooks, '', version) ?? noLooks;
}

/** The taskbar spots where the overlay would have no room beside the app buttons, as it shows now. */
export function useRoomless(overlay: number, version: number): Snap[] {
  return useBridgeRead(readRoomless, overlay, version) ?? noSpots;
}

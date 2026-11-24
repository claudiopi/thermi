import type { Bridge, Monitor, Style } from '../bridge/bridge.g';
import { useBridgeRead } from '../bridge/useWindow';

const readMonitors = (from: Bridge): Promise<Monitor[]> => from.monitors();
const readLooks = (from: Bridge): Promise<Record<string, Style>> => from.looks();

const noMonitors: Monitor[] = [];
const noLooks: Record<string, Style> = {};

/** The monitors Windows has now. */
export function useMonitors(version: number): Monitor[] {
  return useBridgeRead(readMonitors, '', version) ?? noMonitors;
}

/** The saved looks by their names. */
export function useLooks(version: number): Record<string, Style> {
  return useBridgeRead(readLooks, '', version) ?? noLooks;
}

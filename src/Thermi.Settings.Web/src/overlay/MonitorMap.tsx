import { useRef, useState, type CSSProperties, type KeyboardEvent } from 'react';
import type { Monitor, Rect, SnapSpot } from '../bridge/bridge.g';
import { useText } from '../texts/Texts';
import { useWidth } from './pixels';
import { alongOf, cornerSpots, displayName, spotKey, type Placed } from './placeModel';

export interface MonitorMapProps {
  /** The displays, the main one first, as the window numbers them. */
  shown: readonly Monitor[];
  /** Where the overlay sits now, or null for one dropped anywhere. */
  placed: Placed | null;
  /** Where the overlay shows on the desktop, or null while C# has not said. */
  place: Rect | null;
  vertical: boolean;
  /** Hidden, the overlay's spot shows dimmed and says so. */
  hidden: boolean;
  onSnap: (spot: SnapSpot, display: Monitor) => void;
}

/** The map's width in units for all its displays together, and the room between two, as the prototype draws them. */
const units = 400;
const between = 16;

interface Spot {
  spot: SnapSpot;
  display: number;
  x: number;
  y: number;
}

/** The least room between a corner's center and the taskbar spot's below, half of each one's target, 26 and 24 px. */
const apart = 25;

/**
 * A display's spots, from its top left: four corners, and one on its taskbar unless the overlay is vertical, at the share
 * of its width `along` gives, its target kept whole inside it. The lower corners keep a target's room above the taskbar's.
 */
function spotsOf(display: number, width: number, height: number, bar: number, along: number | null): Spot[] {
  const lower = Math.min(height - bar - 12, height - bar / 2 - apart);
  const corners: [number, number][] = [
    [12, 12],
    [width - 12, 12],
    [12, lower],
    [width - 12, lower],
  ];
  const onBar = along === null ? [] : [{ spot: 'taskbar' as const, display, x: Math.min(width - 14, Math.max(14, along * width)), y: height - bar / 2 }];
  return [...onBar, ...cornerSpots.map((spot, index) => ({ spot, display, x: corners[index]?.[0] ?? 0, y: corners[index]?.[1] ?? 0 }))];
}

interface Box {
  index: number;
  one: Monitor;
  x: number;
  w: number;
  h: number;
}

/** The displays as drawn, left to right as Windows places them, at the map's scale, and the map's size. */
function layoutOf(shown: readonly Monitor[], scale: number): { drawn: Box[]; wide: number; tallest: number } {
  const unit = (units / shown.reduce((sum, one) => sum + one.bounds.width, 0)) * scale;
  const order = shown.map((one, index) => ({ one, index })).sort((a, b) => a.one.bounds.x - b.one.bounds.x);
  const drawn: Box[] = [];
  for (const { one, index } of order) {
    const last = drawn.at(-1);
    const x = last === undefined ? 0 : last.x + last.w + between * scale;
    drawn.push({ index, one, x, w: one.bounds.width * unit, h: one.bounds.height * unit });
  }
  const end = drawn.at(-1);
  return { drawn, wide: end === undefined ? 0 : end.x + end.w, tallest: Math.max(0, ...drawn.map((box) => box.h)) };
}

export function MonitorMap({ shown, placed, place, vertical, hidden, onSnap }: MonitorMapProps) {
  const t = useText();
  const [block, width] = useWidth();
  const group = useRef<HTMLDivElement>(null);
  const scale = Math.min(2, Math.max(0.8, Math.floor(((width - 32) / (units + between)) * 10) / 10));
  const { drawn, wide, tallest } = layoutOf(shown, scale);
  const bar = 14 * scale;
  const along = (box: Box): number | null => (!box.one.taskbar || vertical ? null : alongOf(placed, box.index, box.one, place));
  const all = drawn.flatMap((box) => spotsOf(box.index, box.w, box.h, box.one.taskbar ? bar : 0, along(box)));
  const isOn = (spot: Spot): boolean => placed !== null && placed.spot === spot.spot && placed.display === spot.display;
  const tabStop = all.find(isOn) ?? all[0];
  // The spots are dots: the one pointed at or focused, else the overlay's, names itself in words under the map.
  const [pointed, setPointed] = useState<Spot | null>(null);
  const nameOf = (spot: Spot): string => t('OverlayPageText.SpotOnMonitor', t(spotKey(spot.spot)), spot.display + 1);
  const said = pointed ?? all.find(isOn);
  const keyDown = (event: KeyboardEvent<HTMLDivElement>): void => {
    const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[event.key];
    const at = all.findIndex((spot) => spot === tabStop);
    const next = step === undefined ? undefined : all[(at + step + all.length) % all.length];
    const display = next === undefined ? undefined : shown[next.display];
    if (next !== undefined && display !== undefined) {
      event.preventDefault();
      onSnap(next.spot, display);
      group.current?.querySelectorAll<HTMLElement>('[role="radio"]')[all.indexOf(next)]?.focus();
    }
  };
  const label = (one: Monitor, index: number): string => {
    const size =
      one.scale === 1
        ? t('OverlayPageText.DisplaySize', one.bounds.width, one.bounds.height)
        : t('OverlayPageText.DisplayScaled', one.bounds.width, one.bounds.height, Math.round(one.scale * 100));
    return `${displayName(one, index, t)}\n${size}`;
  };
  return (
    <div ref={block} className="t-map-block">
      <div ref={group} className="t-map" role="radiogroup" aria-label={t('UiText.MonitorMap')} style={{ width: wide, height: tallest }} onKeyDown={keyDown}>
        {drawn.map((box) => {
          const style: CSSProperties = { left: box.x, top: tallest - box.h, width: box.w, height: box.h };
          const mine = placed?.display === box.index;
          return (
            <div key={box.one.id} className={`t-mon${mine ? ' t-on' : ''}`} style={style}>
              <span className="t-mon-name">{label(box.one, box.index)}</span>
              {box.one.taskbar && <span className="t-mon-bar" style={{ height: bar }} />}
              {all
                .filter((spot) => spot.display === box.index)
                .map((spot) => (
                  <button
                    key={spot.spot}
                    type="button"
                    role="radio"
                    aria-checked={isOn(spot)}
                    aria-label={nameOf(spot)}
                    aria-description={hidden && isOn(spot) ? t('UiText.OverlayHidden') : undefined}
                    title={spot.spot.startsWith('top') ? `${t(spotKey(spot.spot))}\n${t('OverlayPageText.TopSpotHint')}` : t(spotKey(spot.spot))}
                    tabIndex={spot === tabStop ? 0 : -1}
                    className={
                      `t-spot${spot.spot.startsWith('taskbar') ? ' t-spot-bar' : ''}` +
                      `${isOn(spot) ? ' t-on' : ''}${hidden && isOn(spot) ? ' t-hidden' : ''}`
                    }
                    style={{ left: spot.x, top: spot.y }}
                    onClick={() => onSnap(spot.spot, box.one)}
                    onPointerEnter={() => setPointed(spot)}
                    onPointerLeave={() => setPointed(null)}
                    onFocus={() => setPointed(spot)}
                    onBlur={() => setPointed(null)}
                  />
                ))}
            </div>
          );
        })}
      </div>
      <div className="t-map-said" aria-hidden="true">
        {said === undefined ? '' : nameOf(said)}
      </div>
    </div>
  );
}

import { useEffect, useRef, useState } from 'react';
import type { DesktopPick } from '../bridge/bridge.g';
import { bridge } from '../bridge/useWindow';

/** What the window edits, which the real overlay marks: the overlay, its selected item and the item pointed at. */
export interface Editing {
  overlay: number | null;
  selected: string | null;
  pointed: string | null;
}

/**
 * The real overlays and the window, both ways (R99, R104). The window opens on what it was opened on, and a click or a
 * double-click on a value or an overlay's edge selects it; pointing at a value marks its row, which this gives back.
 * The overlay is told what the window edits each time that changes.
 */
export function useDesktop(editing: Editing, onPick: (pick: DesktopPick) => void): string | null {
  const [pointed, setPointed] = useState<string | null>(null);
  const picked = useRef(onPick);
  useEffect(() => {
    picked.current = onPick;
  });

  useEffect(() => {
    const from = bridge();
    if (from === null) {
      return;
    }
    let live = true;
    const stopPicks = from.picked((pick) => {
      if (pick.how === 'point-in' || pick.how === 'point-out') {
        setPointed(pick.how === 'point-in' ? pick.item : null);
      } else {
        picked.current(pick);
      }
    });
    // A pick raised before the page listened never reached it, so what the window opened on is asked for.
    void from.start().then((pick) => {
      if (live && pick !== null) {
        picked.current(pick);
      }
    });
    return () => {
      live = false;
      stopPicks();
    };
  }, []);

  const { overlay, selected, pointed: pointedRow } = editing;
  useEffect(() => {
    void bridge()?.editing(overlay, selected, pointedRow);
  }, [overlay, selected, pointedRow]);
  return pointed;
}

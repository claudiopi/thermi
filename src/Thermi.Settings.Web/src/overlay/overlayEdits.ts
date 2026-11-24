import type { Edit, Overlay } from '../bridge/bridge.g';
import { overlayName } from '../list/rows';
import type { TextKey } from '../texts/Texts';

type Say = (key: TextKey, ...blanks: (string | number)[]) => string;

/** What follows an edit of the overlays: the overlay row to select and focus, if any, and what Narrator hears. */
export interface OverlayOutcome {
  select: number | null;
  say: string;
}

/** Where the selection goes after an edit of the overlays, from the overlays as they were before it. */
export function overlayOutcome(edit: Edit, overlays: readonly Overlay[], say: Say): OverlayOutcome {
  const name = (index: number): string => {
    const one = overlays[index];
    return one === undefined ? '' : overlayName(one, index, say);
  };
  switch (edit.kind) {
    case 'newOverlay':
      return { select: overlays.length, say: say('OverlayPageText.OverlayAdded') };
    case 'duplicateOverlay':
      return { select: edit.overlay + 1, say: say('OverlayPageText.OverlayAdded') };
    case 'mergeOverlay':
      return { select: edit.into > edit.overlay ? edit.into - 1 : edit.into, say: say('OverlayPageText.OverlayMerged', name(edit.overlay), name(edit.into)) };
    case 'deleteOverlay':
      return { select: Math.max(0, Math.min(edit.overlay, overlays.length - 2)), say: say('OverlayPageText.OverlayDeleted', name(edit.overlay)) };
    case 'overlay':
      return edit.field === 'hidden' ? { select: null, say: say(edit.value === true ? 'OverlayPageText.OverlayHiddenSaid' : 'OverlayPageText.OverlayShownSaid', name(edit.overlay)) } : { select: null, say: '' };
    default:
      return { select: null, say: '' };
  }
}

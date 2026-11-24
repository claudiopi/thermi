import { expect, test } from 'vitest';
import type { Edit, Overlay } from '../bridge/bridge.g';
import { sayKeys } from '../testSay';
import { overlayOutcome, type OverlayOutcome } from './overlayEdits';

const named = (...names: string[]): Overlay[] => names.map((name) => ({ name, items: [] }));
const three = named('A', 'B', 'C');

test.each([
  { edit: { kind: 'newOverlay' }, overlays: three, outcome: { select: 3, say: 'OverlayPageText.OverlayAdded' } },
  { edit: { kind: 'duplicateOverlay', overlay: 1 }, overlays: three, outcome: { select: 2, say: 'OverlayPageText.OverlayAdded' } },
  { edit: { kind: 'mergeOverlay', overlay: 0, into: 2 }, overlays: three, outcome: { select: 1, say: 'OverlayPageText.OverlayMerged A C' } },
  { edit: { kind: 'mergeOverlay', overlay: 0, into: 1 }, overlays: three, outcome: { select: 0, say: 'OverlayPageText.OverlayMerged A B' } },
  { edit: { kind: 'mergeOverlay', overlay: 2, into: 0 }, overlays: three, outcome: { select: 0, say: 'OverlayPageText.OverlayMerged C A' } },
  { edit: { kind: 'mergeOverlay', overlay: 0, into: 5 }, overlays: three, outcome: { select: 4, say: 'OverlayPageText.OverlayMerged A ' } },
  { edit: { kind: 'deleteOverlay', overlay: 2 }, overlays: three, outcome: { select: 1, say: 'OverlayPageText.OverlayDeleted C' } },
  { edit: { kind: 'deleteOverlay', overlay: 1 }, overlays: three, outcome: { select: 1, say: 'OverlayPageText.OverlayDeleted B' } },
  { edit: { kind: 'deleteOverlay', overlay: 0 }, overlays: named('A'), outcome: { select: 0, say: 'OverlayPageText.OverlayDeleted A' } },
  {
    edit: { kind: 'overlay', overlay: 1, field: 'hidden', value: true },
    overlays: three,
    outcome: { select: null, say: 'OverlayPageText.OverlayHiddenSaid B' },
  },
  {
    edit: { kind: 'overlay', overlay: 1, field: 'hidden', value: null },
    overlays: three,
    outcome: { select: null, say: 'OverlayPageText.OverlayShownSaid B' },
  },
  { edit: { kind: 'overlay', overlay: 1, field: 'locked', value: true }, overlays: three, outcome: { select: null, say: '' } },
  { edit: { kind: 'removeItem', item: 'cpu' }, overlays: three, outcome: { select: null, say: '' } },
] satisfies { edit: Edit; overlays: Overlay[]; outcome: OverlayOutcome }[])('after $edit the list selects and says $outcome', ({ edit, overlays, outcome }) => {
  expect(overlayOutcome(edit, overlays, sayKeys)).toEqual(outcome);
});

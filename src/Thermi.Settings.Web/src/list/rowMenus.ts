import type { Edit, Monitor, Settings } from '../bridge/bridge.g';
import { offersOwnPill } from '../item/itemModel';
import type { MenuEntry } from '../kit';
import { displayName, moveToDisplay, placedOf } from '../overlay/placeModel';
import type { TextKey } from '../texts/Texts';
import { overlayName, pillKey, type Row } from './rows';

type Say = (key: TextKey, ...blanks: (string | number)[]) => string;

/** What a row's menu runs, which the list wires to its own state and the bridge. */
export interface RowActions {
  rename: (row: Row) => void;
  editLabel: (item: string) => void;
  fold: (row: Row, open: boolean) => void;
  edit: (edit: Edit) => void;
  /** The displays, the main one first, for "Move to display". */
  displays: readonly Monitor[];
}

/** An overlay holds at most this many; "Move to a new overlay" goes once there are as many. */
const mostOverlays = 4;

function itemEntries(row: Row, settings: Settings, say: Say, run: RowActions): MenuEntry[] {
  const id = row.items[0] ?? '';
  const item = settings.overlays[row.overlay]?.items.find((one) => one.id === id);
  const firstOfPill = row.parent === pillKey(row.overlay, id);
  const moves: MenuEntry[] = settings.overlays.flatMap((overlay, index) =>
    index === row.overlay
      ? []
      : [
          {
            kind: 'action' as const,
            id: `move-${index}`,
            text: say('UiText.MoveToOverlay', overlayName(overlay, index, say)),
            onRun: () => run.edit({ kind: 'moveItem', item: id, overlay: index, index: overlay.items.length }),
          },
        ],
  );
  const fresh: MenuEntry[] =
    settings.overlays.length < mostOverlays
      ? [
          {
            kind: 'action',
            id: 'move-new',
            text: say('UiText.MoveToNewOverlay'),
            onRun: () => run.edit({ kind: 'moveItem', item: id, overlay: null, index: 0 }),
          },
        ]
      : [];
  return [
    { kind: 'action', id: 'rename', text: say('UiText.Rename'), keys: 'F2', onRun: () => run.rename(row) },
    { kind: 'action', id: 'label', text: say('UiText.EditPillLabel'), onRun: () => run.editLabel(id) },
    ...(!offersOwnPill(settings, row.overlay, item, firstOfPill)
      ? []
      : [
          {
            kind: 'check' as const,
            id: 'own',
            text: say('UiText.OwnPill'),
            checked: item?.own === true,
            onRun: () => run.edit({ kind: 'item', item: id, field: 'own', value: item?.own === true ? null : true }),
          },
        ]),
    {
      kind: 'check',
      id: 'tray',
      text: say('UiText.OwnTrayIcon'),
      checked: item?.tray === true,
      onRun: () => run.edit({ kind: 'item', item: id, field: 'tray', value: item?.tray === true ? null : true }),
    },
    { kind: 'divider', id: 'moves' },
    ...moves,
    ...fresh,
    { kind: 'divider', id: 'end' },
    { kind: 'action', id: 'remove', text: say('UiText.Remove'), keys: 'Delete', onRun: () => run.edit({ kind: 'removeItem', item: id }) },
  ];
}

export function rowMenu(row: Row, settings: Settings, say: Say, run: RowActions): MenuEntry[] {
  switch (row.kind) {
    case 'item':
      return itemEntries(row, settings, say, run);
    case 'pill':
      return [
        { kind: 'action', id: 'label', text: say('UiText.EditPillLabel'), onRun: () => run.editLabel(row.items[0] ?? '') },
        { kind: 'action', id: 'fold', text: say(row.expanded ? 'UiText.Collapse' : 'UiText.Expand'), onRun: () => run.fold(row, !row.expanded) },
      ];
    case 'overlay':
      return overlayEntries(row, settings, say, run);
  }
}

function overlayEntries(row: Row, settings: Settings, say: Say, run: RowActions): MenuEntry[] {
  const { overlays } = settings;
  const overlay = overlays[row.overlay];
  if (overlay === undefined) {
    return [];
  }
  const here = placedOf(overlay, run.displays)?.display ?? -1;
  const toDisplays: MenuEntry[] = run.displays.flatMap((display, index) =>
    index === here
      ? []
      : [
          {
            kind: 'action' as const,
            id: `display-${index}`,
            text: displayName(display, index, say),
            onRun: () => run.edit({ kind: 'overlay', overlay: row.overlay, field: 'snap', value: moveToDisplay(overlay, display) }),
          },
        ],
  );
  const into: MenuEntry[] = overlays.flatMap((other, index) =>
    index === row.overlay
      ? []
      : [
          {
            kind: 'action' as const,
            id: `into-${index}`,
            text: overlayName(other, index, say),
            onRun: () => run.edit({ kind: 'mergeOverlay', overlay: row.overlay, into: index }),
          },
        ],
  );
  const hidden = overlay.hidden === true;
  return [
    { kind: 'action', id: 'rename', text: say('UiText.Rename'), keys: 'F2', icon: 'edit', onRun: () => run.rename(row) },
    {
      kind: 'action',
      id: 'duplicate',
      text: say('UiText.DuplicateOverlay'),
      icon: 'copy',
      disabled: overlays.length >= mostOverlays,
      onRun: () => run.edit({ kind: 'duplicateOverlay', overlay: row.overlay }),
    },
    ...(toDisplays.length > 0
      ? [{ kind: 'submenu' as const, id: 'display', text: say('UiText.MoveToDisplay'), icon: 'monitor' as const, entries: toDisplays }]
      : []),
    ...(into.length > 0 ? [{ kind: 'submenu' as const, id: 'merge', text: say('UiText.MergeInto'), icon: 'merge' as const, entries: into }] : []),
    {
      kind: 'action',
      id: 'hide',
      text: say(hidden ? 'UiText.ShowThisOverlay' : 'UiText.HideThisOverlay'),
      icon: hidden ? 'show' : 'hide',
      onRun: () => run.edit({ kind: 'overlay', overlay: row.overlay, field: 'hidden', value: hidden ? null : true }),
    },
    { kind: 'divider', id: 'end' },
    // The last overlay stays, so its Delete shows disabled, telling that it cannot go.
    {
      kind: 'action',
      id: 'delete',
      text: say('UiText.Delete'),
      keys: 'Delete',
      icon: 'delete',
      disabled: overlays.length < 2,
      onRun: () => run.edit({ kind: 'deleteOverlay', overlay: row.overlay }),
    },
  ];
}

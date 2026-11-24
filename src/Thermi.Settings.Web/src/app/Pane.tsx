import { useLayoutEffect, useRef } from 'react';
import type { Edit } from '../bridge/bridge.g';
import type { WindowData } from '../bridge/useWindow';
import { ItemPage } from '../item/ItemPage';
import { settingsKey } from '../list/rows';
import { OverlayPage } from '../overlay/OverlayPage';
import { SettingsPage } from '../settings/SettingsPage';
import { useFocusKept } from './paneFocus';

export interface PaneProps {
  data: WindowData;
  selected: string;
  /** Under the narrow list while it is open, which takes all input. */
  inert: boolean;
  /** Counts the asks to edit the selected item's pill label, from the list's menu. */
  labelAsked: number;
  /** An edit of the overlays themselves, as Hide or Delete this overlay. */
  onOverlayEdit: (edit: Edit) => void;
}

/** The pane: the page of what the list selected. */
export function Pane({ data, selected, inert, labelAsked, onOverlayEdit }: PaneProps) {
  const main = useRef<HTMLElement>(null);
  useFocusKept(main, selected);
  // Each page opens at its top, its title in view, never where the last one was scrolled to.
  useLayoutEffect(() => {
    main.current?.scrollTo({ top: 0 });
  }, [selected]);
  const [kind, overlay] = selected.split(':');
  const item = kind === 'i' ? selected.slice(2) : null;
  return (
    <main ref={main} className="t-pane" inert={inert}>
      <div className="t-pane-in">
        {selected === settingsKey && <SettingsPage data={data} />}
        {item !== null && <ItemPage key={item} data={data} id={item} labelAsked={labelAsked} />}
        {(kind === 'o' || kind === 'p') && <OverlayPage key={overlay} data={data} overlay={Number(overlay)} onOverlayEdit={onOverlayEdit} />}
      </div>
    </main>
  );
}

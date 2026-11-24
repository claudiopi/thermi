import { useRef, useState } from 'react';
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
  // More options stays open from item to item, so a person comparing items finds the same rows.
  const [moreOpen, setMoreOpen] = useState(false);
  const main = useRef<HTMLElement>(null);
  useFocusKept(main, selected);
  const [kind, overlay] = selected.split(':');
  const item = kind === 'i' ? selected.slice(2) : null;
  return (
    <main ref={main} className="t-pane" inert={inert}>
      <div className="t-pane-in">
        {selected === settingsKey && <SettingsPage data={data} />}
        {item !== null && <ItemPage key={item} data={data} id={item} labelAsked={labelAsked} moreOpen={moreOpen} onMoreOpen={setMoreOpen} />}
        {(kind === 'o' || kind === 'p') && <OverlayPage data={data} overlay={Number(overlay)} onOverlayEdit={onOverlayEdit} />}
      </div>
    </main>
  );
}

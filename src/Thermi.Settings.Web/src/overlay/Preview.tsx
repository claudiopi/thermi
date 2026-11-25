import type { OverlayPicture } from '../bridge/bridge.g';
import type { WindowData } from '../bridge/useWindow';
import { useText } from '../texts/Texts';
import { useDevicePixels, useWidth } from './pixels';
import { usePreview } from './useOverlayReads';

/**
 * A picture C# painted, one of its pixels on each of the page's device pixels, so it is never resampled, or nothing until
 * it answers; without `alt`, one a name beside it already says.
 */
export function Picture({ picture, alt }: { picture: OverlayPicture | null; alt?: string }) {
  const ratio = useDevicePixels();
  return picture === null ? null : (
    <img src={picture.png} width={picture.width / ratio} height={picture.height / ratio} alt={alt ?? ''} draggable={false} />
  );
}

export interface PreviewProps {
  data: WindowData;
  overlay: number;
}

// As wide as the overlay shows, or painted smaller by C# to the column's width.
export function Preview({ data, overlay }: PreviewProps) {
  const t = useText();
  const [block, width] = useWidth();
  const room = Math.floor(width * useDevicePixels());
  const picture = usePreview(overlay, room, data.changes);
  const taskbar = picture?.taskbar ?? null;
  return (
    <div ref={block} className={`t-preview${taskbar === null ? '' : ' t-preview-bar'}`} style={taskbar === null ? undefined : { background: taskbar }}>
      <Picture picture={picture} alt={t('OverlayPageText.Preview', data.settings.overlays[overlay]?.name ?? '')} />
    </div>
  );
}

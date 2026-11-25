import type { OverlayPicture } from '../bridge/bridge.g';
import type { WindowData } from '../bridge/useWindow';
import { useText } from '../texts/Texts';
import { usePreview } from './useOverlayReads';

/** A picture C# painted, at its size at 100 %, or nothing until it answers; without `alt`, one a name beside it already says. */
export function Picture({ picture, alt }: { picture: OverlayPicture | null; alt?: string }) {
  return picture === null ? null : <img src={picture.png} width={picture.width} height={picture.height} alt={alt ?? ''} draggable={false} />;
}

export interface PreviewProps {
  data: WindowData;
  overlay: number;
}

export function Preview({ data, overlay }: PreviewProps) {
  const t = useText();
  const picture = usePreview(overlay, data.changes);
  const taskbar = picture?.taskbar ?? null;
  return (
    <div className={`t-preview${taskbar === null ? '' : ' t-preview-bar'}`} style={taskbar === null ? undefined : { background: taskbar }}>
      <Picture picture={picture} alt={t('OverlayPageText.Preview', data.settings.overlays[overlay]?.name ?? '')} />
    </div>
  );
}

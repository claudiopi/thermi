import { useEffect, useRef, useState, type RefObject } from 'react';
import { flushSync } from 'react-dom';

/** A block's width inside its padding, followed as the pane changes. */
export function useWidth(): [RefObject<HTMLDivElement | null>, number] {
  const block = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  useEffect(() => {
    const element = block.current;
    if (element === null) {
      return;
    }
    // Drawn at the new width in the frame that resized, before it paints: left to React's scheduler, the render can come
    // a frame or more later on a busy PC, which shows the map or the preview a frame at the old width, cut.
    const observer = new ResizeObserver(([entry]) => flushSync(() => setWidth(entry?.contentRect.width ?? 0)));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  return [block, width];
}

/**
 * The page's device pixels for each of its own, which Windows' scale and text size both set, followed as the window moves
 * to another monitor or either changes.
 */
export function useDevicePixels(): number {
  const [ratio, setRatio] = useState(() => window.devicePixelRatio);
  useEffect(() => {
    const now = matchMedia(`(resolution: ${ratio}dppx)`);
    const follow = (): void => setRatio(window.devicePixelRatio);
    now.addEventListener('change', follow);
    return () => now.removeEventListener('change', follow);
  }, [ratio]);
  return ratio;
}

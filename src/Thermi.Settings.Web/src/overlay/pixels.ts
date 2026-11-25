import { useEffect, useRef, useState, type RefObject } from 'react';
import { useMeasured } from '../app/measured';

function contentWidth(element: HTMLElement): number {
  const style = getComputedStyle(element);
  const edges = [style.paddingLeft, style.paddingRight, style.borderLeftWidth, style.borderRightWidth].reduce((sum, one) => sum + parseFloat(one), 0);
  return element.getBoundingClientRect().width - edges;
}

/** A block's width inside its padding, followed as the pane changes. */
export function useWidth(): [RefObject<HTMLDivElement | null>, number] {
  const block = useRef<HTMLDivElement>(null);
  return [block, useMeasured(block, contentWidth, 0)];
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

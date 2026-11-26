import { useLayoutEffect, useRef, type RefObject } from 'react';

/**
 * Widens a dropdown's box, past the width its page gives it, to the room its longest choice takes, so a long translation
 * of any choice shows whole once chosen. It measures the box's text in its own font, as the closed box draws it.
 */
export function useFitsChoices(texts: readonly string[], on: boolean): RefObject<HTMLInputElement | null> {
  const input = useRef<HTMLInputElement>(null);
  const key = texts.join('\n');
  useLayoutEffect(() => {
    const box = input.current?.parentElement;
    const context = on ? document.createElement('canvas').getContext('2d') : null;
    if (input.current === null || box === null || box === undefined || context === null) {
      return;
    }
    box.style.minWidth = '';
    const style = getComputedStyle(input.current);
    context.font = style.font;
    const longest = Math.max(...key.split('\n').map((text) => context.measureText(text).width));
    const room = input.current.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
    const needs = Math.ceil(box.offsetWidth - room + longest);
    box.style.minWidth = needs > box.offsetWidth ? `${needs}px` : '';
  }, [key, on]);
  return input;
}

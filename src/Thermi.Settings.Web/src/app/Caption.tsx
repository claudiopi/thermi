import { useRef, type MouseEvent, type PointerEvent } from 'react';
import type { CaptionAction, WindowCaption } from '../bridge/bridge.g';
import { bridge } from '../bridge/useWindow';
import { Icon } from '../kit';
import { useText } from '../texts/Texts';

/** How far a press on the title row moves before it moves the window, as a title bar waits, so a double-click stays one. */
const dragFrom = 4;

const ask = (action: CaptionAction): void => {
  void bridge()?.caption(action);
};

// A press on the row's own space, not on one of its controls.
const onRow = (target: EventTarget): boolean => target instanceof Element && target.closest('button, a, input, [role="menu"]') === null;

/**
 * What the title row does as a title bar where the OS draws no caption, as on Linux: a drag from its own space moves the
 * window, and a double-click does what the desktop says a title bar's does.
 * Windows makes the row its caption itself, so there it does nothing.
 */
export function useCaptionRow(caption: WindowCaption | null) {
  const pressed = useRef<{ x: number; y: number } | null>(null);
  // After a move the desktop took the release, so WebKit sends no pointerdown for the next press, only moves held down: a
  // held move right after one with no button down in the row is that press.
  const moved = useRef(false);
  const up = useRef(false);
  if (caption === null) {
    return {};
  }
  return {
    onPointerDown: (event: PointerEvent<HTMLElement>) => {
      moved.current = false;
      pressed.current = event.button === 0 && onRow(event.target) ? { x: event.clientX, y: event.clientY } : null;
    },
    onPointerMove: (event: PointerEvent<HTMLElement>) => {
      const held = (event.buttons & 1) === 1;
      if (moved.current && held) {
        moved.current = false;
        pressed.current = up.current && onRow(event.target) ? { x: event.clientX, y: event.clientY } : null;
      }
      up.current = !held;
      const from = pressed.current;
      if (from !== null && held && Math.hypot(event.clientX - from.x, event.clientY - from.y) >= dragFrom) {
        pressed.current = null;
        moved.current = true;
        ask('move');
      }
    },
    onPointerUp: () => {
      moved.current = false;
      pressed.current = null;
    },
    // A press held down from elsewhere into the row moves nothing.
    onPointerLeave: () => {
      up.current = false;
    },
    onDoubleClick: (event: MouseEvent<HTMLElement>) => {
      if (onRow(event.target)) {
        ask('title-double-click');
      }
    },
  };
}

/**
 * The caption buttons where the OS draws none, where Windows draws its own: minimize, maximize or restore, and close, at
 * the title row's end. As Windows' they take no focus from the keyboard, which closes the window with Alt+F4.
 */
export function CaptionButtons({ caption }: { caption: WindowCaption }) {
  const t = useText();
  return (
    <span className="t-caption">
      <button type="button" tabIndex={-1} className="t-caption-button" aria-label={t('WindowText.Minimize')} onClick={() => ask('minimize')}>
        <Icon name="minimize" />
      </button>
      <button
        type="button"
        tabIndex={-1}
        className="t-caption-button"
        aria-label={caption.maximized ? t('WindowText.RestoreDown') : t('WindowText.Maximize')}
        onClick={() => ask('maximize')}
      >
        <Icon name={caption.maximized ? 'restore' : 'maximize'} />
      </button>
      <button type="button" tabIndex={-1} className="t-caption-button t-caption-close" aria-label={t('UiText.Close')} onClick={() => ask('close')}>
        <Icon name="close" />
      </button>
    </span>
  );
}

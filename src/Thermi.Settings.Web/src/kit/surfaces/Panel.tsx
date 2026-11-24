import { OverlayDrawer, makeStyles, tokens } from '@fluentui/react-components';
import { useRef, useState, type ReactNode } from 'react';
import { useFocusBack } from './focus';
import { fluentRef } from '../fluentRef';
import { space, surface, text } from '../theme/look';

export interface PanelProps {
  open: boolean;
  /** What Narrator calls it. */
  label: string;
  /** Esc that nothing inside took, or a click beside it. */
  onClose: () => void;
  children: ReactNode;
}

// The prototype's catalog: 440 px from the window's right edge, under the title row, which stays in reach, over a
// light smoke.
const below = space.titleRow;
// Above what a page raises, as the map's spots and the chart's knobs, as a dialog is.
const useStyles = makeStyles({
  surface: {
    zIndex: 30,
    top: below,
    height: `calc(100% - ${below})`,
    width: 'min(440px, 100%)',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'stretch',
    gap: '12px',
    color: text.primary,
    padding: '20px 20px 0',
    boxSizing: 'border-box',
    backgroundColor: surface.panel,
    borderLeft: `1px solid ${tokens.colorNeutralStroke3}`,
    borderTopLeftRadius: '8px',
    boxShadow: '-16px 0 32px rgba(0, 0, 0, 0.18)',
    // Its size is the window's, so what is inside can ask how short it is, as the catalog's chips do.
    containerType: 'size',
    containerName: 'panel',
    '@media (forced-colors: active)': { backgroundColor: 'Canvas', border: '1px solid CanvasText' },
  },
  smoke: { zIndex: 30, top: below, backgroundColor: 'rgba(0, 0, 0, 0.18)' },
  host: { display: 'contents' },
});

/**
 * A panel from the window's right edge that holds the focus while it is open, like the catalog. Esc or a click on the
 * smoke beside it closes it, and the focus goes back to what opened it unless what closed it put it elsewhere. Unlike a
 * dialog's, its changes are the window's own, so Ctrl+Z and Ctrl+Y reach the window from inside it (`data-undoes`).
 */
export function Panel({ open, label, onClose, children }: PanelProps) {
  const styles = useStyles();
  const root = useRef<HTMLDivElement>(null);
  // Mounted inside the window rather than at the document's end, so it takes the window's surfaces and text colors.
  const [host, setHost] = useState<HTMLDivElement | null>(null);
  useFocusBack(open, root);
  return (
    <div ref={setHost} className={styles.host}>
      {host !== null && (
        <OverlayDrawer
          ref={fluentRef(root)}
          mountNode={host}
          open={open}
          position="end"
          size="medium"
          className={styles.surface}
          backdrop={{ className: styles.smoke }}
          aria-label={label}
          data-undoes=""
          onOpenChange={(_, data) => {
            if (!data.open) {
              onClose();
            }
          }}
        >
          {children}
        </OverlayDrawer>
      )}
    </div>
  );
}

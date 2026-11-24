import { Dialog as FluentDialog, DialogSurface, DialogTitle, makeStyles, tokens } from '@fluentui/react-components';
import { useRef, type KeyboardEvent, type ReactNode } from 'react';
import { Button } from '../controls/Button';
import { Icon } from '../controls/Icon';
import { fluentRef } from '../fluentRef';
import { space, surface as surfaces, text } from '../theme/look';
import { useFocusBack } from './focus';
import { useKitRoot } from './root';

export interface SheetProps {
  open: boolean;
  title: string;
  closeLabel: string;
  onClose: () => void;
  children: ReactNode;
}

const useStyles = makeStyles({
  surface: {
    zIndex: 30,
    top: space.titleRow,
    width: '480px',
    maxWidth: 'calc(100vw - 32px)',
    maxHeight: `calc(100vh - ${space.titleRow} - 24px)`,
    overflowY: 'auto',
    boxSizing: 'border-box',
    padding: '20px 24px 24px',
    borderRadius: '8px',
    border: `1px solid ${surfaces.flyoutStroke}`,
    background: surfaces.flyout,
    boxShadow: '0 8px 16px rgba(0, 0, 0, 0.26)',
    '@media (forced-colors: active)': { border: '1px solid CanvasText', background: 'Canvas' },
  },
  backdrop: { zIndex: 30, top: space.titleRow, backgroundColor: surfaces.smoke },
  head: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '8px', marginBottom: '4px' },
  title: { margin: '0', fontSize: '20px', lineHeight: '28px', fontWeight: tokens.fontWeightSemibold },
  // The prototype's × is a 12 px glyph in a 32 px button.
  close: { color: text.secondary, '& .fui-Button__icon': { fontSize: '12px', width: '12px', height: '12px' } },
  host: { display: 'contents' },
});

/** A sheet of what to know at a glance, like the shortcuts: Esc, × or the key that opened it closes it. */
export function Sheet({ open, title, closeLabel, onClose, children }: SheetProps) {
  const styles = useStyles();
  const surface = useRef<HTMLDivElement>(null);
  const [anchor, host] = useKitRoot();
  useFocusBack(open, surface);
  const closeOnF1 = (event: KeyboardEvent<HTMLDivElement>): void => {
    if (event.key === 'F1') {
      event.preventDefault();
      onClose();
    }
  };
  return (
    <span ref={anchor} className={styles.host}>
      {host !== null && (
        <FluentDialog
          open={open}
          onOpenChange={(_, data) => {
            if (!data.open) {
              onClose();
            }
          }}
        >
          <DialogSurface
            ref={fluentRef(surface)}
            mountNode={host}
            className={styles.surface}
            backdrop={{ className: styles.backdrop }}
            onKeyDown={closeOnF1}
            data-modal=""
          >
            <div className={styles.head}>
              <DialogTitle className={styles.title}>{title}</DialogTitle>
              <Button kind="subtle" className={styles.close} icon={<Icon name="close" />} label={closeLabel} onClick={onClose} />
            </div>
            {children}
          </DialogSurface>
        </FluentDialog>
      )}
    </span>
  );
}

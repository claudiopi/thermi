import { Dialog as FluentDialog, DialogContent, DialogSurface, DialogTitle, makeStyles } from '@fluentui/react-components';
import { useRef, type FormEvent, type ReactNode } from 'react';
import { Button } from '../controls/Button';
import { useFocusBack, useFocusOnOpen } from './focus';
import { useKitRoot } from './root';
import { fluentRef } from '../fluentRef';
import { surface as surfaces } from '../theme/look';

export interface DialogProps {
  open: boolean;
  title: string;
  children?: ReactNode;
  /** The main button's text, which Enter runs. */
  main: string;
  onMain: () => void;
  /** Its Cancel button, left out for a dialog that only shows something and closes. */
  cancel?: string;
  /** Cancel, Esc, or the main button of a dialog without Cancel. */
  onCancel: () => void;
  /** A dialog that destroys shows its action then Cancel, and Cancel takes the focus as the accent button. */
  destroys?: boolean;
  /** A main button that asks Windows for administrator rights wears its shield. */
  shield?: boolean;
}

// WinUI's content dialog, as the prototype draws it: its words on the dialog's layer, its buttons sharing the width in a
// darker band under a divider.
const useStyles = makeStyles({
  // Above the folded list (10) and the toasts (20).
  surface: { zIndex: 30, width: '448px', maxWidth: 'calc(100vw - 32px)', padding: '0', borderRadius: '8px', background: surfaces.dialog },
  backdrop: { zIndex: 30, backgroundColor: 'rgba(0, 0, 0, 0.3)' },
  body: { padding: '24px' },
  title: { marginBottom: '12px' },
  foot: {
    display: 'flex',
    gap: '8px',
    padding: '24px',
    background: surfaces.dialogFoot,
    borderTop: `1px solid ${surfaces.divider}`,
    '@media (forced-colors: active)': { borderTop: '1px solid CanvasText' },
  },
  button: { flex: '1' },
  host: { display: 'contents' },
});

/**
 * A dialog. It takes the focus and keeps Tab inside; Enter runs its main button, Esc cancels, and either gives the
 * focus back to what opened it.
 */
export function Dialog({ open, title, children, main, onMain, cancel, onCancel, destroys = false, shield = false }: DialogProps) {
  const styles = useStyles();
  const surface = useRef<HTMLDivElement>(null);
  const cancelButton = useRef<HTMLButtonElement>(null);
  const [anchor, host] = useKitRoot();
  useFocusOnOpen(open && destroys, cancelButton);
  useFocusBack(open, surface);

  const submit = (event: FormEvent): void => {
    event.preventDefault();
    onMain();
  };
  return (
    <span ref={anchor} className={styles.host}>
      {host !== null && (
        <FluentDialog
          open={open}
          modalType={destroys ? 'alert' : 'modal'}
          onOpenChange={(_, data) => {
            if (!data.open) {
              onCancel();
            }
          }}
        >
          <DialogSurface ref={fluentRef(surface)} mountNode={host} className={styles.surface} backdrop={{ className: styles.backdrop }}>
            <form onSubmit={submit}>
              <div className={styles.body}>
                <DialogTitle className={styles.title}>{title}</DialogTitle>
                <DialogContent>{children}</DialogContent>
              </div>
              <div className={styles.foot}>
                <Button className={styles.button} kind={destroys ? 'standard' : 'accent'} type="submit" shield={shield}>
                  {main}
                </Button>
                {cancel !== undefined && (
                  <Button ref={cancelButton} className={styles.button} kind={destroys ? 'accent' : 'standard'} onClick={onCancel}>
                    {cancel}
                  </Button>
                )}
              </div>
            </form>
          </DialogSurface>
        </FluentDialog>
      )}
    </span>
  );
}

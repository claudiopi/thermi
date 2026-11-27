import type { Ref } from 'react';
import type { MarkState, UndoSteps, WindowCaption } from '../bridge/bridge.g';
import { Button, Icon, Menu, steady, Tooltip } from '../kit';
import { useText } from '../texts/Texts';
import { AppName } from './AppName';
import { CaptionButtons, useCaptionRow } from './Caption';

export interface TitleRowProps {
  /** thermi's mark as the tray shows it now. */
  mark: MarkState;
  /** The caption buttons the page draws where the OS draws none, or null where it draws its own. */
  caption: WindowCaption | null;
  /** Whether the list folds into the Navigation button, under 860 px. */
  narrow: boolean;
  listOpen: boolean;
  onShowList: () => void;
  /** The steps Undo and Redo would take, which their tooltips name; a button without one is unavailable. */
  steps: UndoSteps;
  onUndo: () => void;
  onRedo: () => void;
  /** "New overlay" is off once there are four. */
  canAddOverlay: boolean;
  onNewOverlay: () => void;
  onShortcuts: () => void;
  onExit: () => void;
  navRef: Ref<HTMLButtonElement>;
}

/**
 * The title row's content: the mark, the list's button while it folds, Undo, Redo and "…". Windows draws the caption
 * buttons at its end and takes the rest of the row as caption, so it drags and snaps the window; where the OS draws
 * none, the page draws them and the row asks the window to move.
 */
function TitleRowDrawn(props: TitleRowProps) {
  const { mark, caption, narrow, listOpen, onShowList, steps, onUndo, onRedo, canAddOverlay, onNewOverlay, onShortcuts, onExit, navRef } = props;
  const t = useText();
  const row = useCaptionRow(caption);
  return (
    <header className="t-title" {...row}>
      {narrow && (
        <span className="t-tool">
          <Tooltip text={t('UiText.ShowList')} names>
            <Button ref={navRef} kind="subtle" icon={<Icon name="menu" />} keys="Control+F" aria-expanded={listOpen} onClick={onShowList} />
          </Tooltip>
        </span>
      )}
      <AppName mark={mark} />
      <span className="t-grow" />
      <span className="t-tool">
        <Tooltip text={steps.undo === null ? t('UiText.Undo') : t('WindowText.UndoStep', steps.undo)}>
          <Button kind="subtle" label={t('UiText.Undo')} icon={<Icon name="undo" />} keys="Control+Z" disabled={steps.undo === null} onClick={onUndo} />
        </Tooltip>
      </span>
      <span className="t-tool">
        <Tooltip text={steps.redo === null ? t('UiText.Redo') : t('WindowText.RedoStep', steps.redo)}>
          <Button kind="subtle" label={t('UiText.Redo')} icon={<Icon name="redo" />} keys="Control+Y" disabled={steps.redo === null} onClick={onRedo} />
        </Tooltip>
      </span>
      <span className="t-tool">
        <Menu
          trigger={<Button kind="subtle" label={t('UiText.MoreActions')} icon={<Icon name="more" />} />}
          entries={[
            { kind: 'action', id: 'new', text: t('UiText.NewOverlay'), disabled: !canAddOverlay, onRun: onNewOverlay },
            { kind: 'action', id: 'keys', text: t('UiText.KeyboardShortcuts'), keys: 'F1', onRun: onShortcuts },
            { kind: 'divider', id: 'end' },
            { kind: 'action', id: 'exit', text: t('MenuEntry.Exit'), onRun: onExit },
          ]}
        />
      </span>
      {caption !== null && <CaptionButtons caption={caption} />}
    </header>
  );
}

export const TitleRow = steady(TitleRowDrawn);

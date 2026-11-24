import type { Ref } from 'react';
import type { MarkState, UndoSteps } from '../bridge/bridge.g';
import { Button, Icon, Menu, Tooltip } from '../kit';
import { useText } from '../texts/Texts';
import { Creature } from './Creature';

export interface TitleRowProps {
  /** thermi's mark as the tray shows it now. */
  mark: MarkState;
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

/** The mark, in the tray's state, and thermi's name, at the title row's start. */
export function AppName({ mark }: { mark: MarkState }) {
  const t = useText();
  return (
    <h1 className="t-app">
      <Creature state={mark} className="t-mark" />
      <span>{t('UiText.AppName')}</span>
    </h1>
  );
}

/**
 * The title row's content: the mark, the list's button while it folds, Undo, Redo and "…". Windows draws the caption
 * buttons at its end and takes the rest of the row as caption, so it drags and snaps the window.
 */
export function TitleRow({ mark, narrow, listOpen, onShowList, steps, onUndo, onRedo, canAddOverlay, onNewOverlay, onShortcuts, onExit, navRef }: TitleRowProps) {
  const t = useText();
  return (
    <header className="t-title">
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
            { kind: 'action', id: 'exit', text: t('UiText.ExitThermi'), onRun: onExit },
          ]}
        />
      </span>
    </header>
  );
}

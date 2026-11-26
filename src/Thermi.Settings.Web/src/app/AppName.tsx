import type { MarkState } from '../bridge/bridge.g';
import { useText } from '../texts/Texts';
import { Creature } from './Creature';

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

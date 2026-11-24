import type { Problem } from '../bridge/bridge.g';
import { bridge } from '../bridge/useWindow';
import { Creature } from '../app/Creature';
import { Button } from '../kit';
import { useText, type TextKey } from '../texts/Texts';
import './status.css';

const titles: Record<Problem, TextKey> = {
  broken: 'ConfigProblemKind.Broken',
  newer: 'ConfigProblemKind.Newer',
  locked: 'ConfigProblemKind.Locked',
  missing: 'ConfigProblemKind.Missing',
};

/**
 * The window while the settings file cannot be used: what is wrong and where, the file in Windows' editor, and the
 * last good settings back. Nothing else shows, so nothing can be changed or undone.
 */
export function BrokenPage({ problem, line }: { problem: Problem; line: string | null }) {
  const t = useText();
  return (
    <main className="t-broken">
      <Creature state="hot" className="t-broken-mark" />
      <h2 tabIndex={-1}>{t(titles[problem])}</h2>
      {line !== null && <div className="t-broken-line">{line}</div>}
      <p>{t('WindowText.BrokenKeeps')}</p>
      <div className="t-broken-actions">
        <Button kind="accent" onClick={() => void bridge()?.open('settings-file')}>
          {t('WindowText.OpenInEditor')}
        </Button>
        <Button onClick={() => void bridge()?.restore()}>{t('UiText.RestoreSettings')}</Button>
      </div>
    </main>
  );
}

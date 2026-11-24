import { useState } from 'react';
import { Dialog, TextField } from '../kit';
import { useText, type TextKey } from '../texts/Texts';

export interface NameDialogProps {
  title: string;
  main: string;
  start: string;
  hint?: string;
  /** Null once the name is taken, else the refusal's key, which the dialog shows and stays. */
  onName: (name: string) => Promise<string | null>;
  onClose: () => void;
}

/** The name a dialog asks for, with the refusal Core gave as its reason. */
export function NameDialog({ title, main, start, hint, onName, onClose }: NameDialogProps) {
  const t = useText();
  const [name, setName] = useState(start);
  const [refused, setRefused] = useState<string | null>(null);
  const save = (): void => {
    void onName(name.trim()).then((refusal) => (refusal === null ? onClose() : setRefused(t(refusal as TextKey))));
  };
  return (
    <Dialog open title={title} main={main} onMain={save} cancel={t('UiText.Cancel')} onCancel={onClose}>
      {hint !== undefined && <p className="t-dialog-text">{hint}</p>}
      <TextField label={t('UiText.StyleNameField')} labelShown={false} value={name} onChange={setName} {...(refused === null ? {} : { error: refused })} />
    </Dialog>
  );
}

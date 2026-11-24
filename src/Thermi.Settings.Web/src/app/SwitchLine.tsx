import { useId } from 'react';
import { CardRow, Switch } from '../kit';
import { useText } from '../texts/Texts';

export interface SwitchLineProps {
  label: string;
  description?: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (on: boolean) => void;
}

/** A row of a card with a switch at its end, its state written before it, as Windows' settings draw one. */
export function SwitchLine({ label, description, checked, disabled = false, onChange }: SwitchLineProps) {
  const t = useText();
  const id = useId();
  const described = description === undefined ? {} : { description, descriptionId: id };
  return (
    <CardRow label={label} {...described}>
      <Switch
        label={label}
        stateText={t(checked ? 'UndoText.On' : 'UndoText.Off')}
        checked={checked}
        disabled={disabled}
        {...(description === undefined ? {} : { describedBy: id })}
        onChange={onChange}
      />
    </CardRow>
  );
}

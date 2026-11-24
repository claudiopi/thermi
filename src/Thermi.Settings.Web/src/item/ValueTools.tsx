import { useState } from 'react';
import type { Combine, Item, Sensor } from '../bridge/bridge.g';
import { asOneStep } from '../bridge/useWindow';
import { Button, Checkbox, Dialog, Dropdown, Popover, TextField } from '../kit';
import { useText, type TextKey } from '../texts/Texts';

/** The value's own color: a hex box over the look's, which the C# side checks for 3:1. */
export function ValueColor({ item, edit }: { item: Item; edit: (field: string, value: string | null) => Promise<string | null> }) {
  const t = useText();
  const [open, setOpen] = useState(false);
  const [typed, setTyped] = useState<string | null>(null);
  const [refused, setRefused] = useState<string | null>(null);
  const close = (): void => {
    setOpen(false);
    setTyped(null);
    setRefused(null);
  };
  const commit = (value: string): void => {
    if (!open || value === (item.color ?? '')) {
      return;
    }
    // A refusal comes back as a text's key, or already worded with its numbers.
    void edit('color', value).then((refusal) => (refusal === null ? close() : setRefused(/^\w+\.\w+$/.test(refusal) ? t(refusal as TextKey) : refusal)));
  };
  const swatch = <span className="t-swatch" style={{ background: item.color ?? 'currentColor' }} />;
  const shownColor = item.color ?? t('UiText.StyleColorShown');
  return (
    <Popover
      open={open}
      onOpenChange={(next) => (next ? setOpen(true) : close())}
      label={t('UiText.ValueColor')}
      trigger={
        // The row names it; the button shows the color it holds, and Narrator reads both.
        <Button className="t-color-button" icon={swatch} label={`${t('UiText.ValueColor')}, ${shownColor}`}>
          {shownColor}
        </Button>
      }
    >
      <TextField
        label={t('UiText.HexColor')}
        value={typed ?? item.color ?? ''}
        onChange={setTyped}
        onCommit={commit}
        {...(refused === null ? {} : { error: refused })}
      />
      <Button
        kind="subtle"
        onClick={() => {
          close();
          void edit('color', null);
        }}
      >
        {t('UiText.StyleColor')}
      </Button>
    </Popover>
  );
}

export interface CombineProps {
  item: Item;
  /** The sensors of the item's type, which it can combine. */
  sensors: readonly Sensor[];
  type: string;
  set: (field: string, value: string | string[] | null) => Promise<unknown>;
}

/** "Choose sensors…" in the Combine row: several sensors of one type as their highest, average or sum, added as one undo step. */
export function CombineButton({ item, sensors, type, set }: CombineProps) {
  const t = useText();
  const [open, setOpen] = useState(false);
  const [chosen, setChosen] = useState<readonly string[]>(item.sensors ?? []);
  const [how, setHow] = useState<Combine>(item.combine ?? 'max');
  const picked = new Set(chosen);
  const ways: Combine[] = type === 'Temperature' ? ['max', 'average'] : ['max', 'average', 'sum'];
  const choices = ways.map((way) => ({ value: way, text: t(`Combine.${way === 'max' ? 'Max' : way === 'average' ? 'Average' : 'Sum'}`) }));
  const add = (): void => {
    setOpen(false);
    void asOneStep(async () => {
      await set('sensors', [...chosen]);
      await set('combine', how);
    });
  };
  return (
    <>
      <Button
        label={`${t('UiText.Combine')}, ${t('UiText.CombineChoose')}`}
        onClick={() => {
          setChosen(item.sensors ?? []);
          setOpen(true);
        }}
      >
        {t('UiText.CombineChoose')}
      </Button>
      <Dialog open={open} title={t('UiText.Combine')} main={t('UiText.CombineAdd')} onMain={add} cancel={t('UiText.Cancel')} onCancel={() => setOpen(false)}>
        <div className="t-combine">
          <div className="t-combine-list">
            {sensors.map((sensor) => (
              <Checkbox
                key={sensor.id}
                label={sensor.name}
                checked={picked.has(sensor.id)}
                onChange={(on) => setChosen((before) => (on ? [...before, sensor.id] : before.filter((id) => id !== sensor.id)))}
              />
            ))}
          </div>
          <Dropdown label={t('UiText.CombineField')} choices={choices} value={how} onChange={(value) => setHow(value as Combine)} />
        </div>
      </Dialog>
    </>
  );
}

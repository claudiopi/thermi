import { useState, type KeyboardEvent } from 'react';
import { useText } from '../texts/Texts';

/** A page's title, which a click renames in place: Enter keeps the name, Esc puts it back. */
export function PageTitle({ name, onRename }: { name: string; onRename: (name: string) => void }) {
  const t = useText();
  const [typed, setTyped] = useState<string | null>(null);
  const keyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    if (event.key === 'Enter') {
      onRename(typed ?? name);
      setTyped(null);
    } else if (event.key === 'Escape') {
      event.stopPropagation();
      setTyped(null);
    }
  };
  return (
    <h2 className="t-heading">
      {typed === null ? (
        <button type="button" className="t-title-name" aria-description={t('UiText.Rename')} onClick={() => setTyped(name)}>
          {name}
        </button>
      ) : (
        <input
          className="t-title-box"
          aria-label={t('UiText.Rename')}
          value={typed}
          autoFocus
          onChange={(event) => setTyped(event.target.value)}
          onKeyDown={keyDown}
          onBlur={() => setTyped(null)}
        />
      )}
    </h2>
  );
}

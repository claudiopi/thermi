import { useRef, useState, type KeyboardEvent } from 'react';

/** A box whose text C# reads once kept: what is typed in it, what Narrator last heard of it, and its keys. */
export interface TypedBox {
  /** What is typed, or null while the box shows the value. */
  typed: string | null;
  said: string;
  type: (text: string) => void;
  /** Enter, or the focus leaving; either may come after the other, and the second keeps nothing. */
  keep: () => void;
  /** Esc, which puts the value back when something is typed, else leaves the key to what holds the box. */
  escape: (event: KeyboardEvent<HTMLInputElement>) => void;
  /** Back to the value, as a choice picked from a list leaves it. */
  clear: () => void;
}

/** What a typed box holds, its text sent to `onTyped` once kept, which answers null or what Narrator hears. */
export function useTypedBox(onTyped: (typed: string) => Promise<string | null>): TypedBox {
  const [typed, setTyped] = useState<string | null>(null);
  const [said, setSaid] = useState('');
  const sent = useRef(false);
  return {
    typed,
    said,
    type: (text) => {
      setSaid('');
      setTyped(text);
    },
    keep: () => {
      if (typed === null || sent.current) {
        return;
      }
      sent.current = true;
      void onTyped(typed).then((answer) => {
        sent.current = false;
        setTyped(null);
        setSaid(answer ?? '');
      });
    },
    escape: (event) => {
      if (typed !== null) {
        event.preventDefault();
        event.stopPropagation();
        setTyped(null);
      }
    },
    clear: () => setTyped(null),
  };
}

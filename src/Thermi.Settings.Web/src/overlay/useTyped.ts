import type { TypedField } from '../bridge/bridge.g';
import { useEdit } from '../bridge/useWindow';
import { useText, type TextKey } from '../texts/Texts';

/** A number typed in one of the overlay's slider boxes, which C# reads: null once kept as typed, else what Narrator hears. */
export function useTyped(overlay: number): (field: TypedField) => (typed: string) => Promise<string | null> {
  const t = useText();
  const edit = useEdit();
  // A refusal comes back as a text's key, and what C# says of the number already worded.
  return (field) => (typed) => edit({ kind: 'typed', overlay, field, typed }).then((said) => (said === null ? null : t(said as TextKey)));
}

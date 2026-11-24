import { useCallback, useState } from 'react';

/**
 * Where a dialog mounts: the end of the kit's root rather than of the document, so it takes the window's surfaces and
 * shows above everything in the window. The anchor is any element inside the root, given as a ref.
 */
export function useKitRoot(): [anchor: (element: HTMLElement | null) => void, host: HTMLElement | null] {
  const [host, setHost] = useState<HTMLElement | null>(null);
  const anchor = useCallback((element: HTMLElement | null) => {
    if (element !== null) {
      setHost(element.closest<HTMLElement>('.fui-FluentProvider'));
    }
  }, []);
  return [anchor, host];
}

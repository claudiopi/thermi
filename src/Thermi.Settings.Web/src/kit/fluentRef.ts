import type { Ref } from 'react';

/**
 * A ref as Fluent's components take it. Fluent infers their element's type from props that React 19's types no longer
 * carry a ref in, so it types every ref as `Ref<never>`; the element is still the one each wrapper names.
 */
export function fluentRef<T>(ref: Ref<T> | undefined): Ref<never> | undefined {
  return ref as Ref<never> | undefined;
}

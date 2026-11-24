import { isValidElement, memo, useLayoutEffect, useState, type ComponentType, type JSX } from 'react';

type Props = Record<string, unknown>;

/** How deep `alike` looks: far enough for an icon inside a button, or a list of choices. */
const deepest = 4;

/**
 * Whether two props draw the same: the same value, or elements, arrays and plain objects that hold the same. A function
 * inside them, as a button's handler in an element, must be the same function, and a ref the same ref.
 */
function alike(one: unknown, other: unknown, depth: number): boolean {
  if (Object.is(one, other)) {
    return true;
  }
  if (depth > deepest || typeof one !== 'object' || typeof other !== 'object' || one === null || other === null) {
    return false;
  }
  if (isValidElement(one) || isValidElement(other)) {
    return isValidElement(one) && isValidElement(other) && one.type === other.type && one.key === other.key && alike(one.props, other.props, depth + 1);
  }
  if (Array.isArray(one) || Array.isArray(other)) {
    return Array.isArray(one) && Array.isArray(other) && one.length === other.length && one.every((each, index) => alike(each, other[index], depth + 1));
  }
  if (Object.getPrototypeOf(one) !== Object.prototype || Object.getPrototypeOf(other) !== Object.prototype) {
    return false;
  }
  const keys = Object.keys(one);
  return keys.length === Object.keys(other).length && keys.every((key) => (key === 'ref' ? Object.is((one as Props)[key], (other as Props)[key]) : alike((one as Props)[key], (other as Props)[key], depth + 1)));
}

/** The props a component was last drawn with, and a stand-in for each of its functions that calls the latest one. */
class Latest {
  private props: Props;
  private readonly standIns = new Map<string, (...args: unknown[]) => unknown>();

  constructor(props: Props) {
    this.props = props;
  }

  keep(props: Props): void {
    this.props = props;
  }

  steady(props: Props): Props {
    return Object.fromEntries(Object.entries(props).map(([key, value]) => [key, typeof value === 'function' ? this.standIn(key) : value]));
  }

  private standIn(key: string): (...args: unknown[]) => unknown {
    let made = this.standIns.get(key);
    if (made === undefined) {
      made = (...args) => (this.props[key] as (...args: unknown[]) => unknown)(...args);
      this.standIns.set(key, made);
    }
    return made;
  }
}

/**
 * The component drawn again only when what it shows changed, so a second's readings redraw only what they change. Its
 * handlers may be new functions at each render, as arrows written inline are: it calls the latest ones, so they never
 * hold an old state.
 */
export function steady<P extends object>(Component: ComponentType<P>): (props: P) => JSX.Element {
  const Drawn = memo(Component, (one, other) => alike(one, other, 0)) as unknown as ComponentType<Props>;
  return function Steady(props: P) {
    const [latest] = useState(() => new Latest(props as Props));
    useLayoutEffect(() => latest.keep(props as Props));
    return <Drawn {...latest.steady(props as Props)} />;
  };
}

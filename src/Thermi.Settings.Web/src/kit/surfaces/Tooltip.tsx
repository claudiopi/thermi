import { Tooltip as FluentTooltip } from '@fluentui/react-components';
import type { ReactElement } from 'react';

export interface TooltipProps {
  text: string;
  /** Whether the text names the control, for one that shows only an icon, or only describes it. */
  names?: boolean;
  children: ReactElement;
}

/** A tooltip on hover and on focus, which Narrator reads with its control. */
export function Tooltip({ text, names = false, children }: TooltipProps) {
  return (
    <FluentTooltip content={text} relationship={names ? 'label' : 'description'} withArrow={false}>
      {children}
    </FluentTooltip>
  );
}

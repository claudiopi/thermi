// The kit: the only place that imports Fluent, so replacing Fluent, on another platform, touches only this folder.
// Each part is a thin wrapper with the design prototype's look; the page builds from these and never from Fluent.

export { Button, LinkButton, type ButtonKind, type ButtonProps, type LinkButtonProps } from './controls/Button';
export { Dropdown, type Choice, type DropdownProps } from './controls/Dropdown';
export { type Find } from './controls/dropdownSearch';
export { Checkbox, NumberField, type CheckboxProps, type NumberFieldProps } from './controls/Fields';
export { HighlightedText, type HighlightedTextProps, type Marked } from './controls/HighlightedText';
export { Icon, type IconName } from './controls/Icon';
export { Progress, type ProgressProps } from './controls/Progress';
export { Chips, Segmented, ToggleChip, type SegmentedProps, type ToggleChipProps } from './controls/Segmented';
export { SizeBox, type SizeBoxProps } from './controls/SizeBox';
export { Slider, type SliderProps } from './controls/Slider';
export { Switch, type SwitchProps } from './controls/Switch';
export { SearchField, TextField, type SearchFieldProps, type TextFieldProps } from './controls/TextField';
export { Card, CardRow, CardStack, type CardRowProps } from './surfaces/Card';
export { Dialog, type DialogProps } from './surfaces/Dialog';
export { InfoBar, type InfoBarIntent, type InfoBarProps } from './surfaces/InfoBar';
export { Menu, type MenuEntry, type MenuProps } from './surfaces/Menu';
export { Panel, type PanelProps } from './surfaces/Panel';
export { Popover, type PopoverProps } from './surfaces/Popover';
export { Sheet, type SheetProps } from './surfaces/Sheet';
export { Toast, type ToastProps } from './surfaces/Toast';
export { Tooltip, type TooltipProps } from './surfaces/Tooltip';
export { KitProvider, type KitProviderProps } from './theme/KitProvider';
export { steady } from './steady';

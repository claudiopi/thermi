import type { Edit, HotkeyRole, Hotkeys, Settings, Undone, WindowStatus } from '../bridgeTypes';

// The C# side's window status, About, hotkeys and undo, as the fake gives them. Core decides these for real and tests
// them (Hotkeys.Judge, Hotkeys.Offered, the undo history's names); the fake follows the same rules in rough form, only
// so the page has something true to show. It runs in the page after the fake's other parts, so it may use nothing but
// types.
export function installFakeWindow(): void {
  const fake = window.thermiFake;
  const original = window.thermi;
  if (fake === undefined || original === undefined) {
    throw new Error('The fake bridge is not installed.');
  }
  const bridge = original;
  const state = fake.state;
  const say = (key: string, ...blanks: string[]): string => (state.texts[key] ?? key).replace(/\{(\d+)\}/g, (blank, index: string) => blanks[Number(index)] ?? blank);
  const roles: HotkeyRole[] = ['toggle', 'nextStyle', 'clickThrough', 'resetMinMax'];
  const common = ['C', 'V', 'X', 'Z', 'Y', 'A', 'S', 'F', 'N', 'W', 'T'];
  const system = ['Alt+F4', 'Alt+Tab', 'Ctrl+Alt+Delete', 'Ctrl+Shift+Esc'];

  // Hotkeys.Judge, roughly: the first rule a combination breaks gives its reason.
  const judge = (combination: string, role: HotkeyRole, hotkeys: Hotkeys | undefined): string | null => {
    const parts = combination.split('+');
    const key = parts.at(-1) ?? '';
    const has = (modifier: string): boolean => parts.slice(0, -1).includes(modifier);
    const ctrlAlt = `${has('Ctrl') ? 'Ctrl' : ''}${has('Alt') ? 'Alt' : ''}`;
    const rules: [boolean, string][] = [
      [system.includes(combination), 'SystemKeys'],
      [has('Win'), 'WithWin'],
      [ctrlAlt === '' && !/^F(1[3-9]|2[0-4])$/.test(key), 'NeedsCtrlOrAlt'],
      [ctrlAlt === 'Alt' && /^[A-Z0-9]$/.test(key), 'AltWithCharacter'],
      [ctrlAlt === 'Ctrl' && common.includes(key), 'CommonShortcut'],
      [roles.some((other) => other !== role && hotkeys?.[other] === combination), 'OtherHotkey'],
    ];
    const broken = rules.find(([breaks]) => breaks);
    return broken === undefined ? null : `HotkeyRefusal.${broken[1]}`;
  };

  const showsMinOrMax = (): boolean => state.settings.overlays.some((overlay) => overlay.items.some((item) => item.stat === 'min' || item.stat === 'max'));
  const cpuReadsZero = (): boolean => state.sensors.some((sensor) => sensor.hardwareType === 'Cpu' && ['Temperature', 'Power', 'Clock'].includes(sensor.type) && sensor.value === 0);
  const status = (): WindowStatus => ({
    overlay: state.overlayRunning,
    driver: state.service !== 'stopped' && cpuReadsZero(),
    hotkeys: roles.filter((role) => state.settings.hotkeys?.[role] !== undefined || (role === 'nextStyle' ? Object.keys(state.looks).length > 0 : role !== 'resetMinMax' || showsMinOrMax())),
    failed: [],
    problem: state.problem === null ? null : say('ConfigProblemText.AtLine', '12', 'a comma is missing after "size": 1'),
  });

  // What an undo or redo took back: the first item whose fields changed, else the first overlay that did.
  const changedBetween = (before: Settings, after: Settings): Undone | null => {
    const items = (settings: Settings) => new Map(settings.overlays.flatMap((overlay) => overlay.items.map((item) => [item.id, JSON.stringify(item)] as const)));
    const was = items(before);
    const now = items(after);
    const item = [...new Set([...was.keys(), ...now.keys()])].find((id) => was.get(id) !== now.get(id) && now.has(id)) ?? null;
    const overlay = after.overlays.findIndex((one, index) => JSON.stringify(one) !== JSON.stringify(before.overlays[index]));
    if (item === null && overlay < 0 && JSON.stringify(before) === JSON.stringify(after)) {
      return null;
    }
    return { said: say('UndoText.Undone', say('ChangedField.Changes')), item, overlay: item === null && overlay >= 0 ? overlay : null };
  };
  const stepped = (run: () => Promise<unknown>) => async (): Promise<Undone | null> => {
    const before = structuredClone(state.settings);
    await run();
    return changedBetween(before, state.settings);
  };
  const undo = original.undo;
  const redo = original.redo;
  const edit = original.edit;

  bridge.status = async () => status();
  bridge.about = async () => ({ version: '0.6.0', languages: state.languages, windows: state.languages[0]?.name ?? '' });
  bridge.steps = async () => {
    const { done, undone } = fake.history();
    return { undo: done > 0 ? say('ChangedField.Changes') : null, redo: undone > 0 ? say('ChangedField.Changes') : null };
  };
  bridge.undo = stepped(undo);
  bridge.redo = stepped(redo);
  bridge.open = async () => {};
  bridge.pasteLook = async (overlay) => (state.clipboard === null ? 'Refusal.PasteFailed' : bridge.importLook(overlay, state.clipboard));
  // A hotkey is one field of thermi's, judged before it is set, as Core's ThermiEdits.SetHotkey.
  bridge.edit = async (asked: Edit) => {
    if (asked.kind !== 'thermi' || !asked.field.startsWith('hotkeys.')) {
      return edit(asked);
    }
    const role = asked.field.slice('hotkeys.'.length) as HotkeyRole;
    const combination = typeof asked.value === 'string' ? asked.value : null;
    const refused = combination === null ? null : judge(combination, role, state.settings.hotkeys);
    if (refused !== null) {
      return refused;
    }
    const { [role]: _old, ...others } = state.settings.hotkeys ?? {};
    const hotkeys: Hotkeys = combination === null ? others : { ...others, [role]: combination };
    return edit({ kind: 'thermi', field: 'hotkeys', value: Object.keys(hotkeys).length === 0 ? null : { ...hotkeys } });
  };
}

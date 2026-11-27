import type { HotkeyRole, WindowStatus } from '../bridgeTypes';

// The C# side's window status, About and the AI agents section, as the fake gives them. Core decides these for real and tests them; the fake
// follows the same rules in rough form, only so the page has something true to show. The hotkeys and undo are the real
// bridge's answers, which the fake replays. It runs in the page after the fake's other parts, so it may use nothing but
// types.
export function installFakeWindow(): void {
  const fake = window.thermiFake;
  const bridge = window.thermi;
  if (fake === undefined || bridge === undefined) {
    throw new Error('The fake bridge is not installed.');
  }
  const state = fake.state;
  const say = (key: string, ...blanks: string[]): string =>
    (state.texts[key] ?? key).replace(/\{(\d+)\}/g, (blank, index: string) => blanks[Number(index)] ?? blank);
  const roles: HotkeyRole[] = ['toggle', 'nextStyle', 'clickThrough', 'resetMinMax'];

  const showsMinOrMax = (): boolean => state.settings.overlays.some((overlay) => overlay.items.some((item) => item.stat === 'min' || item.stat === 'max'));
  const cpuReadsZero = (): boolean =>
    state.sensors.some(
      (sensor) => sensor.hardwareType === 'Cpu' && ['Temperature', 'Power', 'Clock'].includes(sensor.type) && sensor.value === 0,
    );
  const status = (): WindowStatus => ({
    overlay: state.overlayRunning,
    // Only Windows keeps the CPU's sensors behind a driver; on Linux only GNOME's extension has a fix.
    driver: state.os === 'windows' && state.service !== 'stopped' && cpuReadsZero(),
    os: state.os === 'linux' ? 'linux' : 'windows',
    accent: state.accent,
    contrast: state.contrast,
    extension: state.os === 'linux' && (state.extension === 'missing' || state.extension === 'disabled') ? state.extension : null,
    hotkeys: roles.filter(
      (role) =>
        state.settings.hotkeys?.[role] !== undefined ||
        (role === 'nextStyle' ? Object.keys(state.looks).length > 0 : role !== 'resetMinMax' || showsMinOrMax()),
    ),
    failed: [],
    problem: state.problem === null ? null : say('ConfigProblemText.AtLine', '12', 'a comma is missing after "size": 1'),
    lightTaskbar: false,
    forEveryone: false,
  });

  bridge.status = async () => status();
  bridge.about = async () => ({ version: '0.6.0', languages: state.languages, windows: state.languages[0]?.name ?? '', updates: 'Up to date', restart: false });
  bridge.open = async () => {};
  bridge.agents = async () => state.agents;
  // What a copy button copied is what Windows' clipboard then holds, unless another app holds it.
  bridge.copy = async (copied) => {
    if (state.clipboard === null) {
      return false;
    }
    state.clipboard = copied;
    return true;
  };
}

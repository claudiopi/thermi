import type { DesktopAnswer, HotkeyRole, WindowStatus } from '../bridgeTypes';

// The C# side's window status, About and the AI agents section, as the fake gives them. What Core decides of the desktop,
// the fixes it offers, its caption and the words it says them in, is Core's answer, which the fake asks the oracle for; the
// rest Core decides for real and tests, and the fake follows its rules in rough form, only so the page has something true
// to show. The hotkeys and undo are the real bridge's answers, which the fake replays. It runs in the page after the fake's
// other parts, so it may use nothing but types.
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
  const desktop = async (): Promise<DesktopAnswer> => {
    const said = { os: state.os, accent: state.accent, extension: state.extension, contrast: state.contrast, bar: state.bar, maximized: state.maximized };
    const answer = await fake.answered(window.thermiPure?.({ method: 'desktop', args: [said, state.texts] }));
    if (answer === undefined || 'missing' in answer) {
      throw new Error(`The oracle has no answer: ${answer?.missing ?? 'it is not installed'}.`);
    }
    return answer.result as DesktopAnswer;
  };
  const status = async (): Promise<WindowStatus> => {
    const { offersDriver, os, accent, extension, contrast, bar, caption } = await desktop();
    return {
      overlay: state.overlayRunning,
      driver: offersDriver && state.service !== 'stopped' && cpuReadsZero(),
      os,
      // The bridge leaves a null out of what it writes.
      accent: accent ?? null,
      contrast,
      caption: caption ?? null,
      bar,
      extension: extension ?? null,
      hotkeys: roles.filter(
        (role) =>
          state.settings.hotkeys?.[role] !== undefined ||
          (role === 'nextStyle' ? Object.keys(state.looks).length > 0 : role !== 'resetMinMax' || showsMinOrMax()),
      ),
      failed: [],
      problem: state.problem === null ? null : say('ConfigProblemText.AtLine', '12', 'a comma is missing after "size": 1'),
      lightTaskbar: false,
      forEveryone: false,
    };
  };

  bridge.status = status;
  bridge.texts = async () => ({ ...state.texts, ...(await desktop()).words });
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

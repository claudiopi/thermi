import type { Style } from '../bridgeTypes';

// The C# side's look files written through Windows' save dialog, as the fake keeps them: the text it would write. The
// saved looks themselves, and wearing, importing and pasting one, are the real bridge's answers, which the fake replays.
// It runs in the page after the fake and its views, so it may use nothing but types.
export function installFakeLooks(): void {
  const fake = window.thermiFake;
  const bridge = window.thermi;
  if (fake === undefined || bridge === undefined) {
    throw new Error('The fake bridge is not installed.');
  }
  const state = fake.state;
  const changed = (): void => fake.set({});
  const lookOf = (overlay: number): Style => {
    const { layout: _layout, orientation: _orientation, ...look } = state.settings.overlays[overlay]?.style ?? {};
    return look;
  };

  bridge.exportLook = async (overlay) => {
    state.exported = JSON.stringify({ thermiStyle: 1, style: lookOf(overlay) });
    changed();
  };
  bridge.exportSavedLook = async (name) => {
    state.exported = JSON.stringify({ thermiStyle: 1, style: state.looks[name] ?? {} });
    changed();
  };
}

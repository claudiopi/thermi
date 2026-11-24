import type { Style, Value } from '../bridgeTypes';

// The C# side's saved looks and look files, as the fake gives them. Core decides them for real and tests them; the fake
// follows the same rules in rough form, only so the page has something true to show. It runs in the page after the fake
// and its views, so it may use nothing but types.
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
  const lookFields = ['gap', 'units', 'name', 'preset', 'label', 'value', 'opacity', 'pills', 'colors', 'font', 'corners', 'effect'] as const;
  const maxLength = 1024 * 1024;

  // A look file's text as Core reads it, or the refusal's key.
  const read = (text: string): Style | string => {
    if (text.length > maxLength) {
      return 'Refusal.StyleTooLarge';
    }
    try {
      const file = JSON.parse(text) as { thermiStyle?: unknown; style?: unknown };
      if (typeof file.thermiStyle === 'number' && file.thermiStyle > 1) {
        return 'Refusal.NewerStyle';
      }
      return file.thermiStyle === 1 && typeof file.style === 'object' && file.style !== null ? (file.style as Style) : 'Refusal.BrokenStyle';
    } catch {
      return 'Refusal.BrokenStyle';
    }
  };

  bridge.saveLook = async (overlay, name) => {
    if (Object.keys(state.looks).some((taken) => taken.toLowerCase() === name.toLowerCase())) {
      return 'Refusal.StyleNameTaken';
    }
    state.looks = { ...state.looks, [name]: lookOf(overlay) };
    changed();
    return null;
  };
  bridge.renameLook = async (name, to) => {
    const look = state.looks[name];
    if (look === undefined) {
      return 'Refusal.StyleNotFound';
    }
    const { [name]: _old, ...others } = state.looks;
    state.looks = { ...others, [to]: look };
    state.settings.overlays.filter((overlay) => overlay.style?.name === name).forEach((overlay) => {
      overlay.style = { ...overlay.style, name: to };
    });
    changed();
    return null;
  };
  bridge.deleteLook = async (name) => {
    const { [name]: _gone, ...others } = state.looks;
    state.looks = others;
    changed();
  };
  bridge.exportLook = async (overlay) => {
    state.exported = JSON.stringify({ thermiStyle: 1, style: lookOf(overlay) });
    changed();
  };
  bridge.exportSavedLook = async (name) => {
    state.exported = JSON.stringify({ thermiStyle: 1, style: state.looks[name] ?? {} });
    changed();
  };
  // The look worn is one undo step, as the C# side's: its fields as one gesture.
  bridge.importLook = async (overlay, text) => {
    const picked = text ?? state.importFile;
    if (picked === null) {
      return null;
    }
    const look = read(picked);
    if (typeof look === 'string') {
      return look;
    }
    const fields = look as Record<string, Value | undefined>;
    await bridge.gesture(true);
    try {
      for (const field of lookFields) {
        const refused = await bridge.edit({ kind: 'style', overlay, field, value: fields[field] ?? null });
        if (refused !== null) {
          return refused;
        }
      }
    } finally {
      await bridge.gesture(false);
    }
    return null;
  };
}

import Clutter from 'gi://Clutter';
import Meta from 'gi://Meta';
import St from 'gi://St';

// GNOME 50 runs on Wayland alone, and no longer has the call that asks.
export const ON_X11 = Meta.is_wayland_compositor ? !Meta.is_wayland_compositor() : false;

// The pointer's shapes thermi asks for over an overlay, by their CSS names. GNOME 50 shows the shape of the actor under
// the pointer, or of the one a grab holds, so each box of the overlay takes it, and the pointer is the Shell's again off
// them. Earlier Shells have one shape for the whole Shell, the display's, which goes back to the default once the
// pointer leaves the boxes or they go; their names for it changed in GNOME 48, so the first one the Shell has is taken.
const ACTOR = {'default': 'INHERIT', 'not-allowed': 'NOT_ALLOWED', 'nwse-resize': 'NWSE_RESIZE'};
const DISPLAY = {'default': ['DEFAULT'], 'not-allowed': ['NOT_ALLOWED', 'DND_UNSUPPORTED_TARGET'], 'nwse-resize': ['NWSE_RESIZE', 'SE_RESIZE']};
const ON_ACTORS = typeof Clutter.Actor.prototype.set_cursor_type === 'function';

export const isShape = shape => Object.hasOwn(ACTOR, shape);

// What thermi said: on a Shell of one shape, it shows over the whole Shell until the pointer leaves the boxes.
export function showShape(boxes, shape) {
    if (ON_ACTORS) {
        boxes.forEach(box => box.set_cursor_type(Clutter.CursorType[ACTOR[shape]]));
        return;
    }
    global.display.set_cursor(Meta.Cursor[DISPLAY[shape].find(name => name in Meta.Cursor)]);
}

// A box made again, while the pointer may be off the overlay: on a Shell of one shape, only what thermi says next shows.
export function boxShape(box, shape) {
    if (ON_ACTORS)
        box.set_cursor_type(Clutter.CursorType[ACTOR[shape]]);
}

// On Wayland a Shell of one shape shows the shape a window's program set wherever a window is under the pointer, even
// while a press of thermi's holds it. A cover above the windows while the press holds keeps them from under the pointer,
// so what thermi asks shows there too. It takes nothing, since the press's grab sends all to its box. On X11, and on
// Shells whose actors take shapes, the press's shape shows anywhere already.
export function coverWindows() {
    if (ON_ACTORS || ON_X11)
        return null;
    const cover = new St.Widget({reactive: true, width: global.stage.width, height: global.stage.height});
    global.window_group.get_parent().insert_child_above(cover, global.window_group);
    return cover;
}

// The pointer left the boxes, or they went: a Shell of one shape shows its own again.
export function leftShape(shape) {
    if (!ON_ACTORS && shape && shape !== 'default')
        global.display.set_cursor(Meta.Cursor.DEFAULT);
}

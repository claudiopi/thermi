import Clutter from 'gi://Clutter';
import Meta from 'gi://Meta';

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

// The pointer left the boxes, or they went: a Shell of one shape shows its own again.
export function leftShape(shape) {
    if (!ON_ACTORS && shape && shape !== 'default')
        global.display.set_cursor(Meta.Cursor.DEFAULT);
}

import Atk from 'gi://Atk';
import Clutter from 'gi://Clutter';
import Cogl from 'gi://Cogl';
import GLib from 'gi://GLib';
import St from 'gi://St';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';

// What thermi's overlay and its icons and hotkeys share in the protocol with thermi: who may call, how a call out of its
// bounds is refused, how a picture shows and how a menu's rows read.
export const OVERLAY = 'io.github.claudiopi.thermi.Overlay';
export const PATH = '/io/github/claudiopi/thermi';

// Cairo's ARGB32, as thermi paints, on a little-endian machine.
const FORMAT = Cogl.PixelFormat.BGRA_8888_PRE;

// What a call may ask, far past what thermi asks, so no call can fill the Shell: a picture's side in pixels and its
// scale, and a menu's rows.
const MOST_SIDE = 16384;
const MOST_SCALE = 16;
const MOST_ROWS = 256;

export const finite = (...numbers) => numbers.every(Number.isFinite);

// What takes the pointer where an event happened, as the stage sees it now rather than as it last told the pointer.
export function under(event) {
    const [x, y] = event.get_coords();
    return global.stage.get_actor_at_pos(Clutter.PickMode.REACTIVE, x, y);
}

// A call thermi's protocol does not have: refused before it changes anything.
class Refused extends Error {}

export function refuseUnless(holds, why) {
    if (!holds)
        throw new Refused(why);
}

// Only thermi, the overlay's name's owner, changes what shows; any other caller, or a call out of the protocol's bounds,
// is refused whole. What the call answers is act's variant, if it gives one.
export function answer(owner, invocation, act) {
    if (owner === null || invocation.get_sender() !== owner) {
        invocation.return_dbus_error('org.freedesktop.DBus.Error.AccessDenied', `Only ${OVERLAY} shows thermi's overlays.`);
        return;
    }

    let answered;
    try {
        answered = act();
    } catch (error) {
        if (!(error instanceof Refused))
            throw error;
        invocation.return_dbus_error('org.freedesktop.DBus.Error.InvalidArgs', error.message);
        return;
    }

    invocation.return_value(answered instanceof GLib.Variant ? answered : null);
}

// The picture's pixels are what its sizes say, at a scale a monitor has.
export function refusePicture(pixels, width, height, stride, scale) {
    refuseUnless([width, height].every(side => Number.isInteger(side) && side >= 1 && side <= MOST_SIDE), 'No picture is that size.');
    refuseUnless(stride >= width * 4 && pixels.length === stride * height, 'The pixels are not the picture the sizes say.');
    refuseUnless(finite(scale) && scale > 0 && scale <= MOST_SCALE, 'No scale is that.');
}

// Each row is (its submenu's row, an earlier one, or -1, text, note, checked, grayed, starts a group).
export function refuseRows(rows) {
    refuseUnless(rows.length <= MOST_ROWS && rows.every(([parent], index) => parent === -1 || (parent >= 0 && parent < index)),
        'No menu has those rows.');
}

// The picture's pixels in an image of their own, made again only when its size changes; shown.image and shown.size hold
// it between calls.
export function setPicture(shown, pixels, width, height, stride) {
    if (shown.size !== `${width}x${height}`) {
        shown.image = St.ImageContent.new_with_preferred_size(width, height);
        shown.size = `${width}x${height}`;
    }

    const bytes = new GLib.Bytes(pixels);
    try {
        shown.image.set_bytes(global.stage.context.get_backend().get_cogl_context(), bytes, FORMAT, width, height, stride);
    } catch {
        // GNOME 46 takes no Cogl context.
        shown.image.set_bytes(bytes, FORMAT, width, height, stride);
    }
}

// Fills a menu with the rows; choosing one calls chosen with its index. A screen reader reads each menu as the menu it
// is, which the Shell's own box no longer says.
export function fillMenu(menu, rows, chosen) {
    menu.box.accessible_role = Atk.Role.MENU;
    const parents = new Map([[-1, menu]]);
    rows.forEach(([parent, text, note, checked, grayed, starts], index) => {
        const into = parents.get(parent);
        if (starts && into.numMenuItems > 0)
            into.addMenuItem(new PopupMenu.PopupSeparatorMenuItem());
        const opens = rows.some(row => row[0] === index);
        const item = opens ? new PopupMenu.PopupSubMenuMenuItem(text) : new PopupMenu.PopupMenuItem(text);
        if (note)
            item.add_child(new St.Label({text: note, x_expand: true, x_align: Clutter.ActorAlign.END}));
        item.setOrnament(checked ? PopupMenu.Ornament.CHECK : PopupMenu.Ornament.NONE);
        item.setSensitive(!grayed);
        if (opens) {
            parents.set(index, item.menu);
            item.menu.box.accessible_role = Atk.Role.MENU;
        } else {
            item.connect('activate', () => chosen(index));
        }
        into.addMenuItem(item);
    });
}

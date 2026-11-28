import GLib from 'gi://GLib';
import St from 'gi://St';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';
import {fillMenu, finite, refuseRows, refuseUnless} from './protocol.js';

// An overlay's menu, as the Shell's own popup menu, against a box of the stage thermi gives, on the side it says, or on
// the other where it does not fit, as the Shell's own do. Each overlay's shown holds its menu between calls.

// The side of the box a menu opens on, as the Shell's menus say it: by the side of the menu its arrow is on.
const SIDES = {below: St.Side.TOP, above: St.Side.BOTTOM};

export function refuseMenu(x, y, width, height, side, rows) {
    refuseUnless(finite(x, y, width, height) && width >= 0 && height >= 0, 'No box is that.');
    refuseUnless(Object.hasOwn(SIDES, side), 'No side is that.');
    refuseRows(rows);
}

// Opens the menu in place of the one open; choosing a row calls chosen with its index, and the menu closing, chosen or
// not, with -1 before it. What it opens against draws nothing and takes no click, so it stays out of the Shell's input
// region.
export function openMenu(shown, [x, y, width, height], side, rows, chosen) {
    closeMenu(shown);
    if (!shown.anchor) {
        shown.anchor = new St.Widget({reactive: false});
        Main.uiGroup.add_child(shown.anchor);
    }
    shown.anchor.set({x, y, width, height});
    const menu = new PopupMenu.PopupMenu(shown.anchor, 0, SIDES[side]);
    const manager = new PopupMenu.PopupMenuManager(shown.anchor);
    manager.addMenu(menu);
    Main.uiGroup.add_child(menu.actor);
    fillMenu(menu, rows, chosen);
    // A menu closes before its choice is sent, and is destroyed once it is.
    menu.connect('open-state-changed', (_, open) => {
        if (!open && menu === shown.menu && !shown.closing) {
            chosen(-1);
            shown.closing = GLib.idle_add(GLib.PRIORITY_DEFAULT, () => {
                shown.closing = 0;
                return closeMenu(shown);
            });
        }
    });
    shown.menu = menu;
    shown.manager = manager;
    menu.open();
}

export function closeMenu(shown) {
    if (shown.closing)
        GLib.source_remove(shown.closing);
    const menu = shown.menu;
    Object.assign(shown, {closing: 0, menu: null});
    menu?.destroy();
    return GLib.SOURCE_REMOVE;
}

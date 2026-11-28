import GLib from 'gi://GLib';
import St from 'gi://St';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';
import {fillMenu, finite, refuseRows, refuseUnless} from './protocol.js';

// An overlay's menu, as the Shell's own popup menu, against a box of the stage thermi gives, whole on the box's monitor
// as Windows' menus show: on the side thermi says, or on the other where only that has room, or else on the side with
// more room, slid over the box as far as it must, as it slides when a submenu opens in it. Each overlay's shown holds
// its menu between calls.

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
export function openMenu(shown, box, side, rows, chosen) {
    closeMenu(shown);
    if (!shown.anchor) {
        shown.anchor = new St.Widget({reactive: false});
        Main.uiGroup.add_child(shown.anchor);
        // The menu places itself against what it opens against as it lays out, which can come before that moves.
        shown.anchor.connect('notify::allocation', () => shown.menu?.actor.queue_relayout());
    }
    const [x, y, width, height] = box;
    shown.anchor.set({x, y, width, height});
    let menu = madeMenu(shown, side, rows, chosen);
    const fit = measured(menu, box);
    const onSide = sideOf(fit, side);
    if (onSide !== side) {
        menu.destroy();
        menu = madeMenu(shown, onSide, rows, chosen);
    }
    // A submenu opens inside its menu, which slides as far as it must for the submenu to show whole too; the Shell's
    // submenus scroll within the height the menu has where the monitor has no room for them.
    const scale = St.ThemeContext.get_for_stage(global.stage).scale_factor;
    const slide = need => {
        const spot = fit.spot(onSide, need);
        shown.anchor.set({y: spot.y, height: spot.height});
        menu.actor.style = `max-height: ${Math.floor(spot.room / scale)}px;`;
        // A height it may take is no change of its size to St, which lays it out again only when asked.
        menu.actor.queue_relayout();
    };
    slide(fit.tall);
    for (const submenu of menu.thermiSubmenus)
        submenu.connect('open-state-changed', (_, open) => slide(fit.tall + (open ? heightOf(submenu.actor) : 0)));
    const manager = new PopupMenu.PopupMenuManager(shown.anchor);
    manager.addMenu(menu);
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

function madeMenu(shown, side, rows, chosen) {
    const menu = new PopupMenu.PopupMenu(shown.anchor, 0, SIDES[side]);
    Main.uiGroup.add_child(menu.actor);
    menu.thermiSubmenus = fillMenu(menu, rows, chosen);
    return menu;
}

const heightOf = actor => actor.get_preferred_height(-1)[1];

// The menu's height, alone and with its tallest submenu open, and the room on each side of the box on its monitor; and
// where a menu of a height opens on a side: the box's top, moved as far as the menu must slide over it to show whole,
// and the height the menu has there.
function measured(menu, [, y, , height]) {
    const area = Main.layoutManager.getWorkAreaForMonitor(Main.layoutManager.findIndexForActor(menu.sourceActor));
    const gap = menu.actor.get_theme_node().get_length('-boxpointer-gap');
    const [top, bottom] = [area.y, area.y + area.height];
    const tall = heightOf(menu.actor);
    return {
        tall,
        most: tall + Math.max(0, ...menu.thermiSubmenus.map(submenu => heightOf(submenu.actor))),
        room: {below: bottom - (y + height) - gap, above: y - top - gap},
        // What the menu opens against stays on the monitor, which the Shell takes the menu's work area from, so a menu
        // taller than the monitor opens from its edge.
        spot(side, need) {
            if (side === 'below') {
                const edge = Math.max(top + 1, Math.min(y + height, bottom - gap - need));
                const from = Math.max(top, edge - Math.max(1, height));
                return {y: from, height: edge - from, room: bottom - edge - gap};
            }
            const edge = Math.min(bottom - 1, Math.max(y, top + gap + need));
            return {y: edge, height: Math.min(bottom, edge + Math.max(1, height)) - edge, room: edge - top - gap};
        },
    };
}

// A side that has room for the menu with its tallest submenu open wins over one that has room for the menu alone.
function sideOf(fit, side) {
    const sides = [side, side === 'below' ? 'above' : 'below'];
    const fits = need => sides.find(each => fit.room[each] >= need);
    return fits(fit.most) ?? fits(fit.tall) ?? (fit.room.below >= fit.room.above ? 'below' : 'above');
}

export function closeMenu(shown) {
    if (shown.closing)
        GLib.source_remove(shown.closing);
    const menu = shown.menu;
    Object.assign(shown, {closing: 0, menu: null});
    menu?.destroy();
    return GLib.SOURCE_REMOVE;
}

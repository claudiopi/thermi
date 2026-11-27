import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import Shell from 'gi://Shell';
import St from 'gi://St';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';
import {PATH, answer, fillMenu, inside, refusePicture, refuseRows, refuseUnless, setPicture} from './protocol.js';

// thermi's icons in the top bar and its hotkeys: each icon shows the picture thermi paints for it and passes its presses
// back, its menu is the rows thermi sends, and each hotkey is grabbed through the Shell as its own are, a press of one
// passed back by its place in the list. It decides nothing.
const DESKTOP = 'io.github.claudiopi.thermi.Desktop';
const XML = `<node><interface name="${DESKTOP}">
  <method name="Icons"><arg type="a(ssayiiid)" direction="in"/></method>
  <method name="IconMenu"><arg type="s" direction="in"/><arg type="a(issbbb)" direction="in"/></method>
  <method name="Hotkeys"><arg type="as" direction="in"/><arg type="ab" direction="out"/></method>
  <signal name="IconPressed"><arg type="s"/><arg type="u"/><arg type="u"/></signal>
  <signal name="IconChosen"><arg type="s"/><arg type="i"/></signal>
  <signal name="Hotkey"><arg type="u"/></signal>
</interface></node>`;

// What a call may ask, far past what thermi asks: icons, an icon's side in pixels, hotkeys and an accelerator's length.
const MOST_ICONS = 64;
const MOST_ICON_SIDE = 512;
const MOST_HOTKEYS = 8;
const MOST_ACCELERATOR = 64;
const ID = /^[A-Za-z0-9_.-]{1,128}$/;

export class ThermiDesktop {
    constructor(owner) {
        this._owner = owner;
        this._icons = new Map();
        this._order = '';
        this._grabs = new Map();
        this._service = Gio.DBusExportedObject.wrapJSObject(XML, this);
        this._service.export(Gio.DBus.session, PATH);
        global.display.connectObject('accelerator-activated', (_, action) => this._pressed(action), this);
    }

    destroy() {
        this.clear();
        global.display.disconnectObject(this);
        this._service.unexport();
        this._service = null;
    }

    // thermi went: none of its icons or hotkeys stays.
    clear() {
        this._icons.forEach(icon => icon.button.destroy());
        this._icons.clear();
        this._order = '';
        this._ungrab();
    }

    IconsAsync([icons], invocation) {
        answer(this._owner(), invocation, () => this._show(icons));
    }

    IconMenuAsync([id, rows], invocation) {
        answer(this._owner(), invocation, () => this._menu(id, rows));
    }

    HotkeysAsync([accelerators], invocation) {
        answer(this._owner(), invocation, () => new GLib.Variant('(ab)', [this._grab(accelerators)]));
    }

    // The icons, in thermi's order, replace those shown: each keeps its button while its place stays, so its menu stays
    // open over a new picture.
    _show(icons) {
        refuseUnless(icons.length <= MOST_ICONS, 'No more icons show.');
        icons.forEach(([id, , pixels, width, height, stride, scale]) => {
            refuseUnless(ID.test(id), 'No icon is that.');
            refuseUnless(width <= MOST_ICON_SIDE && height <= MOST_ICON_SIDE, 'No icon is that size.');
            refusePicture(pixels, width, height, stride, scale);
        });
        refuseUnless(new Set(icons.map(([id]) => id)).size === icons.length, 'An icon shows once.');

        const order = icons.map(([id]) => id).join('\n');
        if (order !== this._order) {
            this.clear();
            this._order = order;
        }

        icons.forEach(([id, name, pixels, width, height, stride, scale], index) => {
            const icon = this._icons.get(id) ?? this._add(id, index);
            icon.button.accessible_name = name;
            setPicture(icon, pixels, width, height, stride);
            icon.picture.set({content: icon.image, width: width / scale, height: height / scale});
        });
    }

    // thermi's icons sit at the right of the top bar, left of the Shell's own menu, in thermi's order.
    _add(id, index) {
        const button = new PanelMenu.Button(0.5, id, true);
        const picture = new St.Widget({y_align: Clutter.ActorAlign.CENTER});
        button.add_child(picture);
        // A press of the icon is thermi's, which answers with a menu; one while the menu is open closes it, as the Shell's do.
        // Only one inside it counts: once a menu that held the pointer closes, GNOME 50 can hand the next press to the actor
        // the pointer was on before.
        button.connect('event', (actor, event) => {
            if (event.type() !== Clutter.EventType.BUTTON_PRESS || button.menu?.isOpen || !inside(button, event))
                return Clutter.EVENT_PROPAGATE;
            this._service?.emit_signal('IconPressed', new GLib.Variant('(suu)', [id, event.get_button(), event.get_time()]));
            return Clutter.EVENT_STOP;
        });
        Main.panel.addToStatusArea(`thermi-${id}`, button, index, 'right');
        const icon = {button, picture};
        this._icons.set(id, icon);
        return icon;
    }

    _menu(id, rows) {
        refuseUnless(ID.test(id), 'No icon is that.');
        refuseRows(rows);
        const icon = this._icons.get(id);
        if (!icon)
            return;
        const menu = new PopupMenu.PopupMenu(icon.button, 0.5, St.Side.TOP);
        fillMenu(menu, rows, index => this._service?.emit_signal('IconChosen', new GLib.Variant('(si)', [id, index])));
        // The menu goes once it closed, after its choice was sent.
        menu.connect('open-state-changed', (_, open) => {
            if (!open && icon.button.menu === menu) {
                GLib.idle_add(GLib.PRIORITY_DEFAULT, () => {
                    if (icon.button.menu === menu && !menu.isOpen)
                        icon.button.setMenu(new PopupMenu.PopupDummyMenu(icon.button));
                    return GLib.SOURCE_REMOVE;
                });
            }
        });
        icon.button.setMenu(menu);
        menu.open();
    }

    // Each accelerator, in GTK's form as "<Control><Alt>F10", or empty for none, is taken as the Shell takes its own: true
    // when it is thermi's now, false when another program or the Shell holds it. The previous ones are let go first.
    _grab(accelerators) {
        refuseUnless(accelerators.length <= MOST_HOTKEYS && accelerators.every(each => each.length <= MOST_ACCELERATOR),
            'No hotkeys are those.');
        this._ungrab();
        return accelerators.map((accelerator, index) => {
            if (accelerator === '')
                return true;
            const action = global.display.grab_accelerator(accelerator, Meta.KeyBindingFlags.NONE);
            if (action === Meta.KeyBindingAction.NONE)
                return false;
            Main.wm.allowKeybinding(Meta.external_binding_name_for_action(action), Shell.ActionMode.NORMAL | Shell.ActionMode.OVERVIEW);
            this._grabs.set(action, index);
            return true;
        });
    }

    _ungrab() {
        for (const action of this._grabs.keys()) {
            Main.wm.allowKeybinding(Meta.external_binding_name_for_action(action), Shell.ActionMode.NONE);
            global.display.ungrab_accelerator(action);
        }
        this._grabs.clear();
    }

    _pressed(action) {
        if (this._grabs.has(action))
            this._service?.emit_signal('Hotkey', new GLib.Variant('(u)', [this._grabs.get(action)]));
    }
}

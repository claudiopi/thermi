import Clutter from 'gi://Clutter';
import Cogl from 'gi://Cogl';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import St from 'gi://St';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

// thermi's surface on GNOME Shell (docs/adr/0019-linux.md): it shows what thermi paints, where thermi says, and passes
// the pointer, the monitors and the menu's choice back. It decides nothing. The protocol's version is metadata.json's
// thermi-protocol, which thermi checks before it shows anything through it.
const OVERLAY = 'io.github.claudiopi.thermi.Overlay';
const PATH = '/io/github/claudiopi/thermi';
const SHELL = 'io.github.claudiopi.thermi.Shell';
const XML = `<node><interface name="${SHELL}">
  <property name="Protocol" type="u" access="read"/>
  <method name="Show"><arg type="u" direction="in"/><arg type="ay" direction="in"/><arg type="i" direction="in"/>
    <arg type="i" direction="in"/><arg type="i" direction="in"/><arg type="d" direction="in"/><arg type="d" direction="in"/>
    <arg type="d" direction="in"/><arg type="a(dddd)" direction="in"/></method>
  <method name="Move"><arg type="u" direction="in"/><arg type="d" direction="in"/><arg type="d" direction="in"/></method>
  <method name="Hide"><arg type="u" direction="in"/></method>
  <method name="Menu"><arg type="u" direction="in"/><arg type="d" direction="in"/><arg type="d" direction="in"/>
    <arg type="a(issbbb)" direction="in"/></method>
  <method name="Monitors"><arg type="a(ddddddbb)" direction="out"/></method>
  <signal name="Ready"><arg type="u"/></signal>
  <signal name="Pointer"><arg type="u"/><arg type="s"/><arg type="d"/><arg type="d"/><arg type="u"/><arg type="u"/>
    <arg type="u"/><arg type="d"/><arg type="d"/></signal>
  <signal name="Key"><arg type="u"/><arg type="s"/></signal>
  <signal name="MonitorsChanged"><arg type="a(ddddddbb)"/></signal>
  <signal name="Chosen"><arg type="u"/><arg type="i"/></signal>
</interface></node>`;

// Cairo's ARGB32, as thermi paints, on a little-endian machine.
const FORMAT = Cogl.PixelFormat.BGRA_8888_PRE;

// What a call may ask, far past what thermi asks, so no call can fill the Shell: overlays, a picture's side in pixels and
// its scale, the boxes that take the pointer, and a menu's rows.
const MOST_OVERLAYS = 16;
const MOST_SIDE = 16384;
const MOST_SCALE = 16;
const MOST_BOXES = 64;
const MOST_ROWS = 256;

const finite = (...numbers) => numbers.every(Number.isFinite);

// A call thermi's protocol does not have: refused before it changes anything.
class Refused extends Error {}

function refuseUnless(holds, why) {
    if (!holds)
        throw new Refused(why);
}

export default class ThermiExtension extends Extension {
    enable() {
        this._overlays = new Map();
        this._owner = null;
        this._service = Gio.DBusExportedObject.wrapJSObject(XML, this);
        this._service.export(Gio.DBus.session, PATH);
        this._watch = Gio.bus_watch_name(Gio.BusType.SESSION, OVERLAY, Gio.BusNameWatcherFlags.NONE,
            (_, __, owner) => {
                this._owner = owner;
            },
            () => {
                this._owner = null;
                this._clear();
            });
        Main.layoutManager.connectObject('monitors-changed', () => this._monitorsChanged(), this);
        global.display.connectObject('in-fullscreen-changed', () => this._monitorsChanged(), this);
        Main.layoutManager.panelBox.connectObject('notify::visible', () => this._monitorsChanged(), this);
        this._service.emit_signal('Ready', new GLib.Variant('(u)', [this.Protocol]));
    }

    disable() {
        this._clear();
        Main.layoutManager.disconnectObject(this);
        global.display.disconnectObject(this);
        Main.layoutManager.panelBox.disconnectObject(this);
        Gio.bus_unwatch_name(this._watch);
        this._service.unexport();
        this._service = null;
        this._overlays = null;
    }

    get Protocol() {
        return this.metadata['thermi-protocol'];
    }

    ShowAsync(args, invocation) {
        this._answer(invocation, () => this._show(...args));
    }

    MoveAsync(args, invocation) {
        this._answer(invocation, () => this._move(...args));
    }

    HideAsync(args, invocation) {
        this._answer(invocation, () => this._hide(...args));
    }

    MenuAsync(args, invocation) {
        this._answer(invocation, () => this._menu(...args));
    }

    Monitors() {
        const panel = Main.layoutManager.panelBox;
        return Main.layoutManager.monitors.map(monitor => [
            monitor.x, monitor.y, monitor.width, monitor.height, monitor.geometry_scale,
            monitor.index === Main.layoutManager.primaryIndex && panel.visible ? panel.height : 0,
            monitor.index === Main.layoutManager.primaryIndex,
            global.display.get_monitor_in_fullscreen(monitor.index),
        ]);
    }

    // Only thermi's overlay, the name's owner, changes what shows; any other caller, or a call out of the protocol's
    // bounds, is refused whole.
    _answer(invocation, act) {
        if (this._owner === null || invocation.get_sender() !== this._owner) {
            invocation.return_dbus_error('org.freedesktop.DBus.Error.AccessDenied', `Only ${OVERLAY} shows thermi's overlays.`);
            return;
        }

        try {
            act();
        } catch (error) {
            if (!(error instanceof Refused))
                throw error;
            invocation.return_dbus_error('org.freedesktop.DBus.Error.InvalidArgs', error.message);
            return;
        }

        invocation.return_value(null);
    }

    _show(overlay, pixels, width, height, stride, x, y, scale, clicks) {
        refuseUnless([width, height].every(side => Number.isInteger(side) && side >= 1 && side <= MOST_SIDE), 'No picture is that size.');
        refuseUnless(stride >= width * 4 && pixels.length === stride * height, 'The pixels are not the picture the sizes say.');
        refuseUnless(finite(x, y) && scale > 0 && scale <= MOST_SCALE, 'No place or scale is that.');
        refuseUnless(clicks.length <= MOST_BOXES && clicks.every(box => finite(...box) && box[2] >= 0 && box[3] >= 0), 'No boxes are those.');
        refuseUnless(this._overlays.has(overlay) || this._overlays.size < MOST_OVERLAYS, 'No more overlays show.');

        const shown = this._overlays.get(overlay) ?? this._add(overlay);
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

        shown.picture.set({content: shown.image, width: width / scale, height: height / scale});
        // The boxes stay while they are the same, so the pointer on one stays there; a press holds its box until its
        // release, so a drag keeps the boxes it began with.
        const boxes = JSON.stringify(clicks);
        if (!shown.grab && boxes !== shown.boxes) {
            shown.clicks.forEach(click => click.destroy());
            shown.clicks = clicks.map(box => this._clickable(overlay, box));
            shown.boxes = boxes;
        }
        this._move(overlay, x, y);
    }

    _move(overlay, x, y) {
        refuseUnless(finite(x, y), 'No place is that.');
        const shown = this._overlays.get(overlay);
        if (!shown)
            return;
        shown.picture.set_position(x, y);
        shown.clicks.forEach(click => click.set_position(x + click.thermiAt[0], y + click.thermiAt[1]));
    }

    _hide(overlay) {
        const shown = this._overlays.get(overlay);
        if (!shown)
            return;
        this._release(shown);
        this._closeMenu(shown);
        shown.anchor?.destroy();
        shown.picture.destroy();
        shown.clicks.forEach(click => click.destroy());
        this._overlays.delete(overlay);
    }

    // Each row is (its submenu's row, an earlier one, or -1, text, note, checked, grayed, starts a group); a choice sends
    // the row's index. The menu takes the pointer, so a press that holds the overlay lets it go first.
    _menu(overlay, x, y, rows) {
        refuseUnless(finite(x, y), 'No place is that.');
        refuseUnless(rows.length <= MOST_ROWS && rows.every(([parent], index) => parent === -1 || (parent >= 0 && parent < index)),
            'No menu has those rows.');
        const shown = this._overlays.get(overlay);
        if (!shown)
            return;
        this._release(shown);
        this._closeMenu(shown);
        shown.anchor ??= this._anchor();
        shown.anchor.set_position(x, y);
        const menu = new PopupMenu.PopupMenu(shown.anchor, 0, St.Side.TOP);
        const manager = new PopupMenu.PopupMenuManager(shown.anchor);
        manager.addMenu(menu);
        Main.uiGroup.add_child(menu.actor);
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
            if (opens)
                parents.set(index, item.menu);
            else
                item.connect('activate', () => this._emit('Chosen', '(ui)', [overlay, index]));
            into.addMenuItem(item);
        });
        // A menu closes before its choice is sent, and is destroyed once it is.
        menu.connect('open-state-changed', (_, open) => {
            if (!open && menu === shown.menu && !shown.closing) {
                shown.closing = GLib.idle_add(GLib.PRIORITY_DEFAULT, () => {
                    shown.closing = 0;
                    return this._closeMenu(shown);
                });
            }
        });
        shown.menu = menu;
        shown.manager = manager;
        menu.open();
    }

    _monitorsChanged() {
        this._emit('MonitorsChanged', '(a(ddddddbb))', [this.Monitors()]);
    }

    _emit(name, type, values) {
        this._service?.emit_signal(name, new GLib.Variant(type, values));
    }

    // The picture draws and takes no click, so one passes to what is under it, and leaves the input region as it is.
    _add(overlay) {
        const picture = new St.Widget({reactive: false});
        try {
            Main.layoutManager.addTopChrome(picture, {trackFullscreen: true, affectsInputRegion: false});
        } catch {
            // Newer Shells take no affectsInputRegion here, and on Wayland a widget that is not reactive passes clicks anyway.
            Main.layoutManager.addTopChrome(picture, {trackFullscreen: true});
        }

        const shown = {picture, clicks: []};
        this._overlays.set(overlay, shown);
        return shown;
    }

    _anchor() {
        const anchor = new St.Widget({width: 1, height: 1, reactive: false});
        Main.layoutManager.addTopChrome(anchor, {trackFullscreen: true});
        return anchor;
    }

    _closeMenu(shown) {
        if (shown.closing)
            GLib.source_remove(shown.closing);
        const menu = shown.menu;
        Object.assign(shown, {closing: 0, menu: null});
        menu?.destroy();
        return GLib.SOURCE_REMOVE;
    }

    // A box of the picture where thermi takes the pointer. A press of the first button holds it until its release, wherever
    // the pointer goes then, and Esc while it holds lets thermi take back the drag; no other key is thermi's.
    _clickable(overlay, [x, y, width, height]) {
        const click = new St.Widget({reactive: true, width, height});
        click.thermiAt = [x, y];
        Main.layoutManager.addTopChrome(click, {trackFullscreen: true});
        const pointer = (kind, event, button = 0, [dx, dy] = [0, 0]) => {
            const [px, py] = event.get_coords();
            this._emit('Pointer', '(usdduuudd)', [overlay, kind, px, py, button, event.get_state(), event.get_time(), dx, dy]);
            return Clutter.EVENT_STOP;
        };
        click.connect('button-press-event', (actor, event) => {
            const shown = this._overlays.get(overlay);
            if (shown && !shown.grab && event.get_button() === Clutter.BUTTON_PRIMARY) {
                shown.focus = global.stage.get_key_focus();
                shown.grab = global.stage.grab(actor);
                global.stage.set_key_focus(actor);
            }
            return pointer('press', event, event.get_button());
        });
        click.connect('button-release-event', (actor, event) => {
            if (event.get_button() === Clutter.BUTTON_PRIMARY)
                this._release(this._overlays.get(overlay));
            return pointer('release', event, event.get_button());
        });
        click.connect('motion-event', (actor, event) => pointer('motion', event));
        click.connect('leave-event', (actor, event) => pointer('leave', event));
        click.connect('scroll-event', (actor, event) => {
            const step = {[Clutter.ScrollDirection.UP]: [0, -1], [Clutter.ScrollDirection.DOWN]: [0, 1]};
            const delta = event.get_scroll_direction() === Clutter.ScrollDirection.SMOOTH
                ? event.get_scroll_delta() : step[event.get_scroll_direction()] ?? [0, 0];
            return pointer('scroll', event, 0, delta);
        });
        click.connect('key-press-event', (actor, event) => {
            if (!this._overlays.get(overlay)?.grab || event.get_key_symbol() !== Clutter.KEY_Escape)
                return Clutter.EVENT_PROPAGATE;
            this._emit('Key', '(us)', [overlay, 'Escape']);
            return Clutter.EVENT_STOP;
        });
        return click;
    }

    // The keys go back where they went before the press.
    _release(shown) {
        if (!shown?.grab)
            return;
        shown.grab.dismiss();
        shown.grab = null;
        if (shown.clicks.includes(global.stage.get_key_focus()))
            global.stage.set_key_focus(shown.focus?.get_stage() ? shown.focus : null);
        shown.focus = null;
    }

    // thermi went: nothing of what it showed stays.
    _clear() {
        for (const overlay of [...this._overlays.keys()])
            this._hide(overlay);
    }
}

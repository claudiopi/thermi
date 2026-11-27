import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import St from 'gi://St';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';
import {boxShape, isShape, leftShape, showShape} from './cursor.js';
import {ThermiDesktop} from './desktop.js';
import {closeMenu, openMenu, refuseMenu} from './overlayMenu.js';
import {PointerWatch} from './pointerWatch.js';
import {OVERLAY, PATH, answer, finite, refusePicture, refuseUnless, setPicture, under} from './protocol.js';

// thermi's surface on GNOME Shell (docs/adr/0019-linux.md): it shows what thermi paints, where thermi says, and passes
// the pointer, the monitors and the menu's choice back; its icons in the top bar and its hotkeys are desktop.js'. It
// decides nothing. The protocol's version is metadata.json's thermi-protocol, which thermi checks before it shows
// anything through it.
const SHELL = 'io.github.claudiopi.thermi.Shell';
const XML = `<node><interface name="${SHELL}">
  <property name="Protocol" type="u" access="read"/>
  <method name="Show"><arg type="u" direction="in"/><arg type="ay" direction="in"/><arg type="i" direction="in"/>
    <arg type="i" direction="in"/><arg type="i" direction="in"/><arg type="d" direction="in"/><arg type="d" direction="in"/>
    <arg type="d" direction="in"/><arg type="a(dddd)" direction="in"/></method>
  <method name="Move"><arg type="u" direction="in"/><arg type="d" direction="in"/><arg type="d" direction="in"/></method>
  <method name="Hide"><arg type="u" direction="in"/></method>
  <method name="Fade"><arg type="u" direction="in"/><arg type="d" direction="in"/></method>
  <method name="Menu"><arg type="u" direction="in"/><arg type="d" direction="in"/><arg type="d" direction="in"/>
    <arg type="d" direction="in"/><arg type="d" direction="in"/><arg type="s" direction="in"/>
    <arg type="a(issbbbbb)" direction="in"/></method>
  <method name="Cursor"><arg type="u" direction="in"/><arg type="s" direction="in"/></method>
  <method name="Copy"><arg type="ay" direction="in"/></method>
  <method name="Monitors"><arg type="a(ddddddbbdddd)" direction="out"/></method>
  <method name="Watch"><arg type="b" direction="in"/></method>
  <method name="Activation"><arg type="u" direction="in"/><arg type="s" direction="out"/></method>
  <signal name="Ready"><arg type="u"/></signal>
  <signal name="Pointer"><arg type="u"/><arg type="s"/><arg type="d"/><arg type="d"/><arg type="u"/><arg type="u"/>
    <arg type="u"/><arg type="d"/><arg type="d"/></signal>
  <signal name="Key"><arg type="u"/><arg type="s"/></signal>
  <signal name="MonitorsChanged"><arg type="a(ddddddbbdddd)"/></signal>
  <signal name="Chosen"><arg type="u"/><arg type="i"/></signal>
</interface></node>`;

// What a call may ask, far past what thermi asks, so no call can fill the Shell: overlays, the boxes that take the
// pointer, and a picture's bytes for the clipboard, which start as every PNG does.
const MOST_OVERLAYS = 64;
const MOST_BOXES = 512;
const MOST_PNG = 16 * 1024 * 1024;
const PNG = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

// GNOME 50 runs on Wayland alone, and no longer has the call that asks.
const ON_X11 = Meta.is_wayland_compositor ? !Meta.is_wayland_compositor() : false;

export default class ThermiExtension extends Extension {
    enable() {
        this._overlays = new Map();
        this._owner = null;
        this._pointer = new PointerWatch(values => this._emit('Pointer', '(usdduuudd)', values));
        this._service = Gio.DBusExportedObject.wrapJSObject(XML, this);
        this._service.export(Gio.DBus.session, PATH);
        this._desktop = new ThermiDesktop(() => this._owner);
        this._watch = Gio.bus_watch_name(Gio.BusType.SESSION, OVERLAY, Gio.BusNameWatcherFlags.NONE,
            (_, __, owner) => {
                this._owner = owner;
            },
            () => {
                this._owner = null;
                this._clear();
                this._desktop.clear();
            });
        Main.layoutManager.connectObject('monitors-changed', () => this._monitorsChanged(), this);
        global.display.connectObject('in-fullscreen-changed', () => this._monitorsChanged(), this);
        Main.layoutManager.panelBox.connectObject('notify::visible', () => this._monitorsChanged(), this);
        [Main.panel._centerBox, Main.panel._rightBox].forEach(box => box.connectObject('notify::allocation', () => this._monitorsChanged(), this));
        global.stage.connectObject('captured-event', (stage, event) => this._captured(event), this);
        this._service.emit_signal('Ready', new GLib.Variant('(u)', [this.Protocol]));
    }

    disable() {
        this._clear();
        Main.layoutManager.disconnectObject(this);
        global.display.disconnectObject(this);
        Main.layoutManager.panelBox.disconnectObject(this);
        [Main.panel._centerBox, Main.panel._rightBox].forEach(box => box.disconnectObject(this));
        global.stage.disconnectObject(this);
        Gio.bus_unwatch_name(this._watch);
        this._desktop.destroy();
        this._desktop = null;
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

    // Only a drag moves an overlay, so it rises above the snap marks shown after it.
    MoveAsync(args, invocation) {
        this._answer(invocation, () => {
            this._move(...args);
            this._raise(args[0]);
        });
    }

    HideAsync(args, invocation) {
        this._answer(invocation, () => this._hide(...args));
    }

    FadeAsync(args, invocation) {
        this._answer(invocation, () => this._fade(...args));
    }

    MenuAsync(args, invocation) {
        this._answer(invocation, () => this._menu(...args));
    }

    CursorAsync(args, invocation) {
        this._answer(invocation, () => this._cursor(...args));
    }

    CopyAsync([png], invocation) {
        this._answer(invocation, () => {
            refuseUnless(png.length <= MOST_PNG && PNG.every((byte, at) => png[at] === byte), 'No PNG is that.');
            St.Clipboard.get_default().set_content(St.ClipboardType.CLIPBOARD, 'image/png', new GLib.Bytes(png));
        });
    }

    WatchAsync(args, invocation) {
        this._answer(invocation, () => this._pointer.set(...args));
    }

    // Each monitor, with the top bar's height on the one that shows it, and the left and right edges of the bar's centre box,
    // the clock, and its right box, the system menu, which no overlay covers.
    Monitors() {
        const panel = Main.layoutManager.panelBox;
        const kept = [Main.panel._centerBox, Main.panel._rightBox].flatMap(box => {
            const [left] = box.get_transformed_position();
            const [width] = box.get_transformed_size();
            return [left, left + width];
        });
        return Main.layoutManager.monitors.map(monitor => {
            const bar = monitor.index === Main.layoutManager.primaryIndex && panel.visible;
            return [
                monitor.x, monitor.y, monitor.width, monitor.height, monitor.geometry_scale, bar ? panel.height : 0,
                monitor.index === Main.layoutManager.primaryIndex, global.display.get_monitor_in_fullscreen(monitor.index),
                ...bar ? kept : [0, 0, 0, 0],
            ];
        });
    }

    // A token for a program thermi starts on a person's click at a time of the Shell's, as the dash's own launches take
    // one: the window it shows comes in front, past GNOME's guard against windows that take the keys unasked. 0 is now, as
    // for a menu's choice, whose click came before the menu gave the keys back to the window under it.
    ActivationAsync([time], invocation) {
        this._answer(invocation, () => {
            const thermi = Gio.AppInfo.create_from_commandline('thermi', 'thermi', Gio.AppInfoCreateFlags.NONE);
            const context = global.create_app_launch_context(time || global.display.get_current_time_roundtrip(), -1);
            return new GLib.Variant('(s)', [context.get_startup_notify_id(thermi, []) ?? '']);
        });
    }

    _answer(invocation, act) {
        answer(this._owner, invocation, act);
    }

    _show(overlay, pixels, width, height, stride, x, y, scale, clicks) {
        refusePicture(pixels, width, height, stride, scale);
        refuseUnless(finite(x, y), 'No place is that.');
        refuseUnless(clicks.length <= MOST_BOXES && clicks.every(box => finite(...box) && box[2] >= 0 && box[3] >= 0), 'No boxes are those.');
        refuseUnless(this._overlays.has(overlay) || this._overlays.size < MOST_OVERLAYS, 'No more overlays show.');

        const shown = this._overlays.get(overlay) ?? this._add(overlay);
        setPicture(shown, pixels, width, height, stride);
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

    // The actor fades, not the picture: thermi sends no new one as the pointer nears.
    _fade(overlay, opacity) {
        refuseUnless(Number.isFinite(opacity) && opacity >= 0 && opacity <= 1, 'No opacity is that.');
        const shown = this._overlays.get(overlay);
        if (shown)
            shown.picture.opacity = Math.round(opacity * 255);
    }

    _move(overlay, x, y) {
        refuseUnless(finite(x, y), 'No place is that.');
        const shown = this._overlays.get(overlay);
        if (!shown)
            return;
        shown.picture.set_position(x, y);
        shown.clicks.forEach(click => click.set_position(x + click.thermiAt[0], y + click.thermiAt[1]));
    }

    _raise(overlay) {
        const shown = this._overlays.get(overlay);
        [shown?.picture, ...shown?.clicks ?? []].filter(actor => actor)
            .forEach(actor => actor.get_parent()?.set_child_above_sibling(actor, null));
    }

    _hide(overlay) {
        const shown = this._overlays.get(overlay);
        if (!shown)
            return;
        this._release(shown);
        closeMenu(shown);
        leftShape(shown.shape);
        shown.anchor?.destroy();
        shown.picture.destroy();
        shown.clicks.forEach(click => click.destroy());
        this._overlays.delete(overlay);
    }

    // The shape stays the overlay's, so boxes made again take it too.
    _cursor(overlay, shape) {
        refuseUnless(isShape(shape), 'No pointer is that.');
        const shown = this._overlays.get(overlay);
        if (!shown)
            return;
        shown.shape = shape;
        showShape(shown.clicks, shape);
    }

    // The menu takes the pointer, so a press that holds the overlay lets it go first.
    _menu(overlay, x, y, width, height, side, rows) {
        refuseMenu(x, y, width, height, side, rows);
        const shown = this._overlays.get(overlay);
        if (!shown)
            return;
        this._release(shown);
        openMenu(shown, [x, y, width, height], side, rows, index => this._emit('Chosen', '(ui)', [overlay, index]));
    }

    _monitorsChanged() {
        this._emit('MonitorsChanged', '(a(ddddddbbdddd))', [this.Monitors()]);
    }

    _emit(name, type, values) {
        this._service?.emit_signal(name, new GLib.Variant(type, values));
    }

    // The picture draws and takes no click, so one passes to what is under it, and leaves the input region as it is. It
    // shows over a full-screen window too: thermi hides it there or not, as its settings say, from what Monitors tells.
    _add(overlay) {
        const picture = new St.Widget({reactive: false});
        try {
            Main.layoutManager.addTopChrome(picture, {affectsInputRegion: false});
        } catch {
            // Newer Shells take no affectsInputRegion here, and on Wayland a widget that is not reactive passes clicks anyway.
            Main.layoutManager.addTopChrome(picture);
        }

        const shown = {picture, clicks: []};
        this._overlays.set(overlay, shown);
        return shown;
    }

    // A box of the picture where thermi takes the pointer, in the shape thermi last said. A press of the first button holds
    // it until its release, wherever the pointer goes then, and Esc while it holds lets thermi take back the drag; no other
    // key is thermi's. Another grab that takes the pointer from the press, as a modal dialog's or the lock's, ends it.
    _clickable(overlay, [x, y, width, height]) {
        const click = new St.Widget({reactive: true, width, height});
        click.thermiAt = [x, y];
        Main.layoutManager.addTopChrome(click);
        const pointer = (kind, event, button = 0, [dx, dy] = [0, 0]) => {
            const [px, py] = event.get_coords();
            this._emit('Pointer', '(usdduuudd)', [overlay, kind, px, py, button, event.get_state(), event.get_time(), dx, dy]);
            return Clutter.EVENT_STOP;
        };
        // While no press of thermi's holds, the box takes only what is on it now (see _captured).
        const ours = event => this._overlays.get(overlay)?.grab || under(event) === click;
        const press = event => {
            const shown = this._overlays.get(overlay);
            if (shown && !shown.grab && event.get_button() === Clutter.BUTTON_PRIMARY)
                this._hold(overlay, shown, click);
            return pointer('press', event, event.get_button());
        };
        // Clutter tells each turn of the wheel twice, as itself and as the other kind's emulation of it: thermi takes it once.
        const scroll = event => {
            if (event.get_flags() & Clutter.EventFlags.FLAG_POINTER_EMULATED)
                return Clutter.EVENT_STOP;
            const step = {[Clutter.ScrollDirection.UP]: [0, -1], [Clutter.ScrollDirection.DOWN]: [0, 1]};
            const delta = event.get_scroll_direction() === Clutter.ScrollDirection.SMOOTH
                ? event.get_scroll_delta() : step[event.get_scroll_direction()] ?? [0, 0];
            return pointer('scroll', event, 0, delta);
        };
        click.thermiTake = event => event.type() === Clutter.EventType.SCROLL ? scroll(event) : press(event);
        click.connect('button-press-event', (actor, event) => ours(event) ? press(event) : Clutter.EVENT_PROPAGATE);
        click.connect('button-release-event', (actor, event) => {
            if (!ours(event))
                return Clutter.EVENT_PROPAGATE;
            if (event.get_button() === Clutter.BUTTON_PRIMARY)
                this._release(this._overlays.get(overlay));
            return pointer('release', event, event.get_button());
        });
        click.connect('motion-event', (actor, event) => ours(event) ? pointer('motion', event) : Clutter.EVENT_PROPAGATE);
        click.connect('leave-event', (actor, event) => {
            const shown = this._overlays.get(overlay);
            if (!shown?.grab)
                leftShape(shown?.shape);
            return pointer('leave', event);
        });
        click.connect('scroll-event', (actor, event) => ours(event) ? scroll(event) : Clutter.EVENT_PROPAGATE);
        click.connect('key-press-event', (actor, event) => {
            if (!this._overlays.get(overlay)?.grab || event.get_key_symbol() !== Clutter.KEY_Escape)
                return Clutter.EVENT_PROPAGATE;
            this._emit('Key', '(us)', [overlay, 'Escape']);
            return Clutter.EVENT_STOP;
        });
        const shape = this._overlays.get(overlay)?.shape;
        if (shape)
            boxShape(click, shape);
        return click;
    }

    // On Wayland a grab's actor takes the keys the stage has no actor of its own for, so the focused window keeps its
    // focus; X11 sends them to the Shell only once its stage has the focus, which goes back at the release.
    _hold(overlay, shown, click) {
        const grab = global.stage.grab(click);
        shown.grab = grab;
        grab.connect('notify::revoked', () => {
            if (shown.grab === grab && grab.is_revoked())
                this._lose(overlay, shown);
        });
        if (ON_X11) {
            shown.focus = global.stage.get_key_focus();
            global.stage.set_key_focus(click);
        }
    }

    // A press another grab took ends as Esc ends it, where the pointer is now.
    _lose(overlay, shown) {
        this._release(shown);
        const [x, y, mods] = global.get_pointer();
        // No event is underway as a grab is revoked, so the time is the clock events are stamped with: milliseconds of
        // the monotonic clock, in 32 bits as theirs.
        const time = global.get_current_time() || Math.floor(GLib.get_monotonic_time() / 1000) % 2 ** 32;
        this._emit('Pointer', '(usdduuudd)', [overlay, 'cancel', x, y, 0, mods, time, 0, 0]);
    }

    // Once a grab ends, a menu's or a drag's, GNOME 50 can send the next press or turn of the wheel to the actor the pointer
    // was on before it, a window or another box, wherever the pointer is now; so the stage asks what is under it, and a box
    // of thermi's there takes it. While a grab holds, the stage sees nothing outside it.
    _captured(event) {
        const type = event.type();
        if (type !== Clutter.EventType.BUTTON_PRESS && type !== Clutter.EventType.SCROLL)
            return Clutter.EVENT_PROPAGATE;
        const box = under(event);
        return box?.thermiTake && box !== global.stage.get_event_actor(event) ? box.thermiTake(event) : Clutter.EVENT_PROPAGATE;
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
        this._pointer.stop();
        for (const overlay of [...this._overlays.keys()])
            this._hide(overlay);
    }
}

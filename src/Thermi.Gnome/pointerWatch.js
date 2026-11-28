import GLib from 'gi://GLib';

// While thermi watches, the pointer anywhere is read as often as thermi reads it on Windows, and told only when it moved,
// as a Pointer of kind near on no overlay.
const EVERY = 100;
const NO_OVERLAY = 0xffffffff;

export class PointerWatch {
    // tell sends a Pointer signal's values.
    constructor(tell) {
        this._tell = tell;
        this._source = 0;
    }

    set(on) {
        if (!on) {
            this.stop();
            return;
        }
        if (this._source)
            return;
        let last = '';
        this._source = GLib.timeout_add(GLib.PRIORITY_DEFAULT, EVERY, () => {
            const [x, y, mods] = global.get_pointer();
            if (`${x},${y}` !== last) {
                last = `${x},${y}`;
                this._tell([NO_OVERLAY, 'near', x, y, 0, mods, global.get_current_time(), 0, 0]);
            }
            return GLib.SOURCE_CONTINUE;
        });
    }

    stop() {
        if (this._source)
            GLib.source_remove(this._source);
        this._source = 0;
    }
}

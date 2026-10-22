// Adawat for GNOME Shell. Wayland lets no app see the pointer or the keyboard outside its own windows, so the Shell,
// which sees every drag, tells the Adawat app (over a socket in the user's runtime folder) when Shift, Ctrl or Alt goes
// down during a drag and where the pointer is. The app then opens its wheel there and receives the drop as usual.

import GLib from 'gi://GLib';
import Gio from 'gi://Gio';
import Clutter from 'gi://Clutter';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

const KEYS = [
    ['shift', Clutter.ModifierType.SHIFT_MASK],
    ['ctrl', Clutter.ModifierType.CONTROL_MASK],
    ['alt', Clutter.ModifierType.MOD1_MASK],
];

export default class AdawatExtension extends Extension {
    enable() {
        this._dnd = global.backend.get_dnd();
        this._signals = [
            this._dnd.connect('dnd-enter', () => this._begin()),
            this._dnd.connect('dnd-leave', () => this._end()),
        ];
    }

    disable() {
        for (const id of this._signals ?? [])
            this._dnd.disconnect(id);
        this._signals = null;
        this._dnd = null;
        this._stopPolling();
        this._closeConnection();
    }

    _begin() {
        this._debug('dnd-enter');
        if (this._poll)
            return;
        this._closeAfterFlush = false;
        // Keys held before the drag started do not count; only a press during the drag opens a wheel.
        const [, , mods] = global.get_pointer();
        this._held = mods;
        this._send('begin');
        this._poll = GLib.timeout_add(GLib.PRIORITY_HIGH, 16, () => {
            const [x, y, now] = global.get_pointer();
            for (const [name, mask] of KEYS) {
                if ((now & mask) && !(this._held & mask))
                    this._send(`key ${name} ${Math.round(x)} ${Math.round(y)}`);
            }
            this._held = now;
            return GLib.SOURCE_CONTINUE;
        });
    }

    _end() {
        this._debug('dnd-leave');
        if (!this._poll)
            return;
        this._stopPolling();
        // The connection closes once "end" has been written, even if it is still opening.
        this._closeAfterFlush = true;
        this._send('end');
    }

    // With ~/.config/Adawat/debug-drag present, drag events are written to the journal for bug reports.
    _debug(text) {
        if (GLib.file_test(GLib.build_filenamev([GLib.get_user_config_dir(), 'Adawat', 'debug-drag']), GLib.FileTest.EXISTS))
            console.log(`Adawat: ${text}`);
    }

    _stopPolling() {
        if (this._poll)
            GLib.source_remove(this._poll);
        this._poll = 0;
    }

    // One connection per drag keeps the lines in order; lines written before it opens wait in a queue.
    _send(line) {
        const path = GLib.build_filenamev([GLib.get_user_runtime_dir(), 'adawat', 'drag.sock']);
        if (!GLib.file_test(path, GLib.FileTest.EXISTS))
            return;
        this._queue = this._queue ?? [];
        this._queue.push(`${line}\n`);
        if (this._stream) {
            this._flush();
            return;
        }
        if (this._connecting)
            return;
        this._connecting = true;
        const client = new Gio.SocketClient();
        client.connect_async(Gio.UnixSocketAddress.new(path), null, (source, result) => {
            this._connecting = false;
            try {
                this._connection = source.connect_finish(result);
                this._stream = this._connection.get_output_stream();
                this._flush();
            } catch (e) {
                this._closeConnection();
            }
        });
    }

    _flush() {
        const encoder = new TextEncoder();
        try {
            while (this._queue.length > 0)
                this._stream.write_all(encoder.encode(this._queue.shift()), null);
        } catch (e) {
            this._closeConnection();
            return;
        }
        if (this._closeAfterFlush)
            this._closeConnection();
    }

    _closeConnection() {
        try {
            this._connection?.close(null);
        } catch (e) {
        }
        this._connection = null;
        this._stream = null;
        this._queue = [];
        this._closeAfterFlush = false;
    }
}

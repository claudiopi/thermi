# 0005. The settings window runs in its own process

**Status**: Proposed

## Context

ADR 0001 planned the settings window as WPF loaded inside the overlay's process, only when it opens. Once loaded, WPF stays: the overlay would keep tens of megabytes it no longer uses until the user signs out, and spec 001 (R9) promises an overlay under 40 MB. A crash in the window would also take the overlay down.

## Decision

- `thermi settings` starts the same `thermi.exe` as a second process that shows only the window, with WPF. If one is already open, it brings that one to the front and exits.
- The window reads the sensors from the pipe, like the overlay; the pipe already serves several clients.
- The window and the overlay share the settings file for everything that is a setting; the overlay already watches it. For the two things that are not, they use window messages posted to the overlay's hidden message window: "show", when the window opens or a second thermi starts, and "hotkey status", which answers which hotkeys the overlay could not register. Both stay inside the user's session.
- Both processes write it the same way. Holding a named mutex for the file, they re-read it, apply their own change, write it under a temporary name of their own, and move it into place. The work under the mutex is synchronous; waiting for it gives up after 2 s and the write is tried again, and a mutex left by a writer that died is acquired by the next one. Readers share read, write and delete, so a read never blocks a move into place. So a drag of the overlay and an edit in the window can never interleave or undo each other.
- A read that finds the file locked, empty, cut short or missing, as during an editor's save, is retried for up to 500 ms before it counts. The overlay's 300 ms debounce did this job before.
- Window writes and overlay reloads both pass through one throttle: at most one every 100 ms, and the last value always goes out. A trailing-only debounce would hold the overlay back for as long as a slider moves.
- The overlay opens the window by starting that process, from its menu or a double-click.

## What it replaces in earlier ADRs

- `docs/adr/0001-architecture.md` says the settings window is WPF loaded only when it opens, inside the overlay's process. Instead, it is WPF in a process of its own.

## Rejected

- WPF inside the overlay, as first planned: the memory is never given back, and the window's failures become the overlay's.
- A separate `thermi-settings.exe`: a second program to install, sign and keep in step, for nothing a mode of the same program does not give.
- Each process writing its whole config from memory: whichever writes last silently drops the other's change.
- A named pipe between the window and the overlay: a protocol to version and secure, for two messages that Windows already carries.

## Consequences

- The overlay's budget does not move while the window is open and idle. While a slider moves, the overlay repaints up to 10 times a second, which spec 002 bounds at 2 % of one core. Closing the window frees all of it.
- While a slider moves, the file is written up to ten times a second, a few hundred bytes each time. An editor showing the file may reload that often too.
- Undo lives in the window, as operations applied to the file as it is now, and each step, undo or redo, applies only if its target still holds what the window left there. So a hand edit or a drag made meanwhile is never reverted.

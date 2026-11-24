# 0009. New messages between the overlay and the window

**Status**: Accepted

## Context

Spec 002's redesign (`docs/specs/002-settings-window/spec.md`) ties the window to the real overlays. The chart is full from the window's first frame, though only the overlay runs all the time (R25). A click on a value selects its item in the window, and pointing at a value or a row marks the other side (R99). The overlay being edited marks the selected value (R104). A drag on the desktop is an undo step in the window (R150). A second thermi opens the window (R151).

`docs/adr/0005-settings-window-process.md` carries three messages, through hidden top-level windows found by class: "show" and "hotkey status" to `thermi-host` in the overlay, and `thermi-raise` to `thermi-settings` in the window. It rejected a pipe for so few. The new messages carry more: item ids, the settings file, ten minutes of history.

## Decision

- The same channel grows: hidden windows found by class, `SendMessageTimeout` waiting 500 ms, each kind's id from `RegisterWindowMessage`. A kind that carries only numbers is its registered message. A kind that carries data is a `WM_COPYDATA` whose `dwData` is the kind's registered id, so every kind keeps one name and the receiver dispatches on it.
- An item is named by its id, as UTF-8; an overlay by its place in the file, counted from 1. The two processes may hold different versions of the file for a moment, so an id the receiver does not know is ignored.
- One class in Core encodes and decodes every payload, with a version byte first and a size cap. A payload that fails is dropped and logged. The receiver checks that `WM_COPYDATA` came from the other side's hidden window: Windows cannot say who sent a message, so the window the sender names must be of the other side's class and of a process running the same `thermi.exe`, by its full path.

| Kind | From → to | Carries | When |
| --- | --- | --- | --- |
| `thermi-history` | window → overlay | nothing | as the window opens, before its first frame |
| history, `WM_COPYDATA` | overlay → window | per item: its id, the time of its newest slot on the monotonic clock both share, and its 600 one-second slots as 32-bit floats, NaN for a gap | in answer to `thermi-history` |
| `thermi-select` | overlay → window | the overlay, an item id or none for the overlay's edge, and click or point | a click on a value or an edge; the pointer entering or leaving a value |
| `thermi-edited` | window → overlay | the overlay being edited, the selected item and the item pointed at, each or none | when any of them changes |
| `thermi-gesture` | overlay → window | the gesture's kind, and the file as the overlay read it and as it wrote it | after a desktop gesture's write is moved into place, while the window is open |
| `thermi-raise` | second `thermi settings` → window | the overlay, and the item to select or none | as in ADR 0005 |
| `thermi-rest-hotkeys` | window → overlay | 1 to let its hotkeys go, 0 to take them again | while a hotkey's row records (R134); the overlay takes them again on its own once the window's mutex is gone |

- The overlay acknowledges `thermi-history` at once and sends the history from its own loop, so neither side sends from inside the other's handler. With no history in 500 ms, the chart starts with ten empty minutes, as gaps.
- "edited" is sent off the window's UI thread, through Core's throttle: at most one every 100 ms, the last always out. The overlay drops it once the window's mutex is gone, so a crashed window leaves no mark or fade.
- "gesture" carries the file before and after, so the window builds its step from the difference, with its editor's operations on ids, in whatever order the write and the message arrive. The window names the step in its own language from the kind.
- A second `thermi`, finding the overlay's mutex held, no longer sends "show": it runs as `thermi settings` does, raising the open window or becoming the window. The window still sends "show" as it opens.

## Rejected

- A named pipe or shared memory between the two: a server thread in the overlay, and a protocol and access rules to keep, for messages Windows already carries between two windows of one session.
- Global atoms for item ids, which spec 002's design gives `thermi-raise`: every program in the session shares the table, which holds a few thousand, and an atom whose receiver died is never freed.
- The window keeping the history itself, as before: its chart would start empty each time it opens. The service keeping it would cost its budget and change the pipe protocol (`docs/adr/0003-sensor-service.md`).
- "gesture" naming the write by a hash: the window may read the file only after a later write, and then cannot find the step.

## Consequences

- The overlay keeps 2.4 KB per item, fixed, so R158's memory bound holds; 20 items send the window 48 KB, once per opening.
- Both processes are the same `thermi.exe`, at the same integrity, so UIPI never stands between them, with `uiAccess` (`docs/adr/0004-above-the-taskbar.md`) or without.
- Core's encoding has its own tests; the Windows tests send each kind between two hidden windows.
- Another process of the same user can send these, as it could the old ones. The worst it does is select a row or add an undo step; no message writes the file by itself.

## What it replaces in earlier ADRs

- `docs/adr/0005-settings-window-process.md` sends "show" when the window opens or a second thermi starts. Instead, only the window sends it, and a second thermi opens the window.
- ADR 0005 has the window and the overlay share the settings file and two messages. Instead, they also share the kinds above, on the same channel; a pipe stays rejected.
- ADR 0005's `thermi-raise` carries only which overlay to edit. Instead, it is a `WM_COPYDATA` carrying the overlay and the item to select.

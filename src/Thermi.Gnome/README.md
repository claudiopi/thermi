# Thermi.Gnome

thermi's GNOME Shell extension, in JavaScript, for GNOME Shell 46 to 51 (`docs/adr/0019-linux.md`). GNOME gives no program a way above the top bar and every window on Wayland, nor click-through, so on GNOME the overlay shows from inside the Shell: the extension puts each picture `thermi` paints in the Shell's top layer, draws Core's menus as the Shell's own popup menus, and sends back the pointer and the monitors over the session's D-Bus. It decides and paints nothing itself: even over a full-screen window, `thermi` says whether an overlay shows.

`metadata.json` names the Shell versions it runs on and, as `thermi-protocol`, the version of the protocol it speaks, which `thermi` checks before it shows anything through it; `extension.js` is the extension and its overlays, `overlayMenu.js` an overlay's menu, `cursor.js` the pointer's shape over an overlay, `pointerWatch.js` its read of the pointer anywhere while an overlay fades, `desktop.js` its icons in the top bar and its hotkeys, on an interface of their own, and `protocol.js` the checks and drawing both share. A change to the protocol, once a version of it shipped, raises both that number and `ShellLink.Protocol` in `src/Thermi.Linux/Gnome/ShellLink.cs`.

| What | Who sends it |
| --- | --- |
| Each overlay's picture, premultiplied ARGB, its size, place and scale, and the boxes that take the pointer, where it draws, and how faded it shows | `thermi` (`Show`, `Move`, `Hide`, `Fade`) |
| A menu's rows, each under the row it opens from, one of them the default, which reads bold, a radio's choice with a dot, and the box and the side it opens against; the pointer's shape over an overlay; whether to tell the pointer anywhere, while an overlay fades | `thermi` (`Menu`, `Cursor`, `Watch`) |
| A picture of an overlay, as a PNG, for the clipboard | `thermi` (`Copy`) |
| The pointer on an overlay, or anywhere while watched, and a press another grab took, as a modal dialog's or the lock's, of kind `cancel`; the keys pressed while it holds one, the row chosen | the extension (`Pointer`, `Key`, `Chosen`) |
| The monitors, their scales and top bar with its clock and system menu, which no overlay covers, and which shows a full-screen window | the extension (`Monitors`, `MonitorsChanged`) |
| The Shell's token for a program `thermi` starts on a click, whose window then comes in front | the extension, asked by `thermi` (`Activation`) |
| That it came up, with its protocol's version | the extension (`Ready`) |
| thermi's icons, each by its key with its tooltip and a picture, or none to keep the one shown, an icon's menu, and the hotkeys to take as GTK accelerators; an icon out of the protocol is left out alone | `thermi` (`Icons`, `IconMenu`, `Hotkeys`) |
| A press on an icon, Enter, Space or the Menu key on the one the top bar's focus is on, the row chosen of its menu, a hotkey pressed; a hotkey another program holds is refused in `Hotkeys`' answer | the extension (`IconPressed`, `IconKey`, `IconChosen`, `Hotkey`) |

The overlay holds `ShellLink.Name` on the session's bus, apart from thermi's own id, which the settings window holds; when that name goes, the overlay is gone, and the extension takes down all it showed.

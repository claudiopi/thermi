# Thermi.Gnome

thermi's GNOME Shell extension, in JavaScript, for GNOME Shell 46 to 51 (`docs/adr/0019-linux.md`). GNOME gives no program a way above the top bar and every window on Wayland, nor click-through, so on GNOME the overlay shows from inside the Shell: the extension puts each picture `thermi` paints in the Shell's top layer, draws Core's menus as the Shell's own popup menus, and sends back the pointer and the monitors over the session's D-Bus. It decides and paints nothing itself.

`metadata.json` names the Shell versions it runs on and, as `thermi-protocol`, the version of the protocol it speaks, which `thermi` checks before it shows anything through it; `extension.js` is the extension. A change to the protocol raises both that number and `ShellLink.Protocol` in `src/Thermi.Linux/Gnome/ShellLink.cs`.

| What | Who sends it |
| --- | --- |
| Each overlay's picture, premultiplied ARGB, its size, place and scale, and the boxes that take the pointer, where it draws | `thermi` (`Show`, `Move`, `Hide`) |
| A menu's rows, each under the row it opens from | `thermi` (`Menu`) |
| The pointer on an overlay, the keys pressed while it holds one, the row chosen | the extension (`Pointer`, `Key`, `Chosen`) |
| The monitors, their scales and top bar, and which shows a full-screen window | the extension (`Monitors`, `MonitorsChanged`) |
| That it came up, with its protocol's version | the extension (`Ready`) |

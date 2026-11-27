# Thermi.Gnome

thermi's GNOME Shell extension, in JavaScript, for GNOME Shell 46 to 51 (`docs/adr/0019-linux.md`). GNOME gives no program a way above the top bar and every window on Wayland, nor click-through, so on GNOME the overlay shows from inside the Shell: the extension puts each picture `thermi` paints in the Shell's top layer, shows thermi's icons in the top bar, holds its hotkeys, draws Core's menus as the Shell's own popup menus, and sends back the pointer and the monitors over the session's D-Bus. It decides and paints nothing itself.

`metadata.json` names the Shell versions it runs on; `extension.js` is the extension.

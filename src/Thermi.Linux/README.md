# Thermi.Linux

`thermi` and `thermi-cli` on Linux, as the person (`docs/adr/0019-linux.md`). The overlay reads the service's socket, lets Core decide what each overlay shows, paints it with Cairo and Pango, and shows it through the desktop's surface: the GNOME Shell extension of `src/Thermi.Gnome` over D-Bus, a GTK 4 layer surface on KDE Plasma, Hyprland and Sway, or a GTK 4 window kept above on X11. `thermi settings` hosts the page of `src/Thermi.Settings.Web` in WebKitGTK, behind the same bridge as on Windows. `thermi-cli` runs `Thermi.Cli`'s commands over an `IAgentEnvironment` of Linux's.

| Folder | What lives there |
| --- | --- |
| `Agents` | What `thermi-cli`'s commands reach on Linux: the overlay's user unit for the start at sign-in, and every other answer of `IAgentEnvironment`. |
| `Desktop` | The session's D-Bus, and the monitors as GNOME lays them out. |
| `Sensors` | The service's socket: the user's own, of a `thermi-sensors --console`, else the system's, each trusted only from who should serve it. |

It targets plain `net10.0`, marked for Linux in `Program.cs`. Its tests, against compositors of their own, are in `tests/Thermi.Linux.Tests`.

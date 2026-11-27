# Thermi.Linux

`thermi` and `thermi-cli` on Linux, as the person (`docs/adr/0019-linux.md`). The overlay reads the service's socket, lets Core decide what each overlay shows, paints it with Cairo and Pango, and shows it through the desktop's surface: the GNOME Shell extension of `src/Thermi.Gnome` over D-Bus, a GTK 4 layer surface on KDE Plasma, Hyprland and Sway, or a GTK 4 window kept above on X11. `thermi settings` hosts the page of `src/Thermi.Settings.Web` in WebKitGTK, behind the same bridge as on Windows. `thermi-cli` runs `Thermi.Cli`'s commands over an `IAgentEnvironment` of Linux's. Under `--trace`, each program writes on its standard error the events the tests wait for, one per line, as `tests/Thermi.Linux.Tests/Traced.cs` names them.

It targets plain `net10.0`, marked for Linux in `Program.cs`. Its tests, against compositors of their own, are in `tests/Thermi.Linux.Tests`.

| Folder | What lives there |
| --- | --- |
| `Commands` | `thermi`'s commands on Linux beside the shared ones, as `thermi preview`. |
| `Desktop` | What the overlay reads from the desktop and the machine: high contrast, the panels, the CPU's base clock. |
| `Packaging` | What the packages install beside thermi for the desktop: the app launcher's entry. |
| `Painting` | The overlay painted with Cairo and Pango, from Core's look, layout and shapes, as Windows' GDI+ painter does. |
| `Window` | `thermi settings`: one GTK 4 window per session with the page in WebKitGTK, served from the install's `web` folder, and the bridge's host on Linux: the portal's look, the GNOME extension's and the service's fixes behind the desktop's prompts, and the overlay's user unit. |

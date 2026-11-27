# Thermi.Linux

`thermi` and `thermi-cli` on Linux, as the person (`docs/adr/0019-linux.md`). Its code is not written yet: `Program.cs` says it shows no overlay and ends with 1, and the tests that will prove it wait, skipped, in `tests/Thermi.Linux.Tests`.

What spec 006 asks of it (`docs/specs/006-linux/spec.md`): the overlay reads the service's socket, lets Core decide what each overlay shows, paints it with Cairo and Pango, and shows it through the desktop's surface: the GNOME Shell extension of `src/Thermi.Gnome` over D-Bus, a GTK 4 layer surface on KDE Plasma, Hyprland and Sway, or a GTK 4 window kept above on X11. `thermi settings` hosts the page of `src/Thermi.Settings.Web` in WebKitGTK, behind the same bridge as on Windows. `thermi-cli` runs `Thermi.Cli`'s commands over an `IAgentEnvironment` of Linux's. Under `--trace`, each program writes on its standard error the events the tests wait for, one per line, as `tests/Thermi.Linux.Tests/Traced.cs` names them.

It targets plain `net10.0`, marked for Linux in `Program.cs`.

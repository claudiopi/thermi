# Thermi.Linux

`thermi` and `thermi-cli` on Linux, as the person (`docs/adr/0019-linux.md`). Of it, `thermi-cli` answers today; `thermi` shows no overlay and ends with 1.

What spec 006 asks of it (`docs/specs/006-linux/spec.md`): the overlay reads the service's socket, lets Core decide what each overlay shows, paints it with Cairo and Pango, and shows it through the desktop's surface: the GNOME Shell extension of `src/Thermi.Gnome` over D-Bus, a GTK 4 layer surface on KDE Plasma, Hyprland and Sway, or a GTK 4 window kept above on X11. `thermi settings` hosts the page of `src/Thermi.Settings.Web` in WebKitGTK, behind the same bridge as on Windows. `thermi-cli` runs `Thermi.Cli`'s commands over an `IAgentEnvironment` of Linux's. Under `--trace`, each program writes on its standard error the events the tests wait for, one per line, as `tests/Thermi.Linux.Tests/Traced.cs` names them.

| Folder | What lives there |
| --- | --- |
| `Agents` | What `thermi-cli`'s commands reach on Linux: the overlay's user unit for the start at sign-in, and every other answer of `IAgentEnvironment`. |
| `Desktop` | The session's D-Bus, and the monitors as GNOME lays them out. |
| `Sensors` | The service's socket: the user's own, of a `thermi-sensors --console`, else the system's, each trusted only from who should serve it. |

Its packaging, beside the code in `Packaging`, is the overlay's user unit, its launcher's entry, the AUR's `PKGBUILD` for both packages, and each package's `nfpm.yaml` and scripts, as `Thermi.Sensors.Linux`'s are; `scripts/linux-packages.ps1` packs them with the overlay, its icons and the GNOME Shell extension.
 targets plain `net10.0`, marked for Linux in `Program.cs`.

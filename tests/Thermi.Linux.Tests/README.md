# Thermi.Linux.Tests

The tests that need Linux: `src/Thermi.Sensors.Linux` on recorded trees of `/sys` and `/proc`, its socket, `thermi-cli` from bash, zsh and fish, and `src/Thermi.Linux`'s overlay and window against a headless GNOME Shell, KWin, Sway and an X11 desktop. Core's behavior is tested in `tests/Thermi.Tests`; these check what Linux itself does to thermi.

They never touch the person's desktop: each compositor and process a test starts goes through `OwnSession`, which gives it a home, a runtime folder, Wayland and X11 displays and a D-Bus session bus of the test's own, over an environment cleared of the person's; a repo rule holds it. What needs other users, systemd or a package's install runs in a clean container of the distribution, through `Container`.

| Folder | What lives there |
| --- | --- |
| `Acceptance` | The tests spec 006's acceptance steps name. |
| `Desktops` | A headless desktop of the test's own and what a person does on it: its input for tests, its screenshots against `thermi preview`'s pictures, a window under the overlay, its accessibility tree, a tray, a notification server and a systemd user manager. |

Entry points worth knowing:

- `SensorFeed.cs` serves a recorded tree through the real `thermi-sensors`, as the person's own service.
- `Desktops/Scene.cs` shows thermi above a window and a panel, with the screen before and after.
- The contract's schema check is `tests/Thermi.Tests/Contract/SchemaCheck.cs`, shared rather than copied.

Every test waits for what it waits on to say so, a line, a bus name or a paint, never for a time.

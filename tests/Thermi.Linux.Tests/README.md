# Thermi.Linux.Tests

The tests that need Linux: `src/Thermi.Sensors.Linux` on recorded trees of `/sys` and `/proc`, its socket, `thermi-cli` from bash, zsh and fish, and `src/Thermi.Linux`'s overlay and window against a headless GNOME Shell, KWin, Sway and an X11 desktop. Core's behavior is tested in `tests/Thermi.Tests`; these check what Linux itself does to thermi.

They never touch the person's desktop: each compositor and process a test starts goes through `OwnSession`, which gives it a home, a runtime folder, Wayland and X11 displays and a D-Bus session bus of the test's own, over an environment cleared of the person's; a repo rule holds it. The one thing of theirs a test uses is their systemd user manager: `UserUnits` asks it for a scope, `thermi-tests-<pid>-<guid>`, to run a manager of the test's own in, which loads only the test's units, logs to the test's output, and stops with it; a run that died leaves its scope, which the next run stops. What needs other users, systemd or a package's install runs in a clean container of the distribution, through `Container`.

| Folder | What lives there |
| --- | --- |
| `Acceptance` | The tests spec 006's acceptance steps name. |
| `Desktops` | A headless desktop of the test's own, and what a person and the desktop do on it. |
| `Gnome` | What thermi's GNOME Shell extension takes from the session's bus and the pointer, with a stand-in for thermi. |
| `Readers` | The service's readers over trees made for hardware the recorded PCs lack. |
| `Window` | What the settings window asks of the person's session on its own: starting at sign-in, the desktop's look from its portal, and starting and ending the overlay. |

Entry points worth knowing:

- `SensorFeed.cs` serves a recorded tree through the real `thermi-sensors`, as the person's own service, or one second of it over and over, so a screen holds still.
- `Desktops/Scene.cs` shows thermi above a window and a panel, with the screen before and after.
- `Traced.cs` names the events thermi writes under `--trace`, which the tests wait for.
- The contract's schema check is `tests/Thermi.Tests/Contract/SchemaCheck.cs`, shared rather than copied.

Every test waits for what it waits on to say so, a line, a bus name or a traced event, never for a time: it arms the wait before what makes it happen. A wait that never ends would hang the run, so each is bounded (`Bound.cs`): a helper's wait runs out first and says what it waited for and the program's last lines on stderr, and each test's `Timeout` ends the rest.

A session's bus starts only the D-Bus services a test needs, the settings' store and the accessibility bus, and what a desktop asks with `OwnSession.Activates`, never the file monitors, calendars, accounts and portals a desktop's own starts: each holds inotify instances, of which a user has 128 by default, most taken by their own desktop, and several sessions run at once. For the same reason the Shell's `ibus-daemon` is a stand-in that ends at once.

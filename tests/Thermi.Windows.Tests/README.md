# Thermi.Windows.Tests

The tests that need Windows: real named pipes, real windows, the painter, the settings file shared by two processes, the tray and the settings window's C# side. Core's behavior is tested in `tests/Thermi.Tests`; these check what Windows itself does to thermi.

They never touch the user's PC. Windows go on a desktop of the test's own (`TestWindows.OnOwnDesktop`), and every process a test starts goes through `DesktopProcess` to such a desktop, so nothing shows on the user's screen; a repo rule holds both. Files go under the test's output (`SettingsFolder.cs`), tray icons to a fake notification area (`FakeTray.cs`), logs to a `ListLogger`. They need no admin rights and run on a fake clock, except `FullLoadTests`, which loads every core for real and so runs last, alone.

| Folder | What lives there |
| --- | --- |
| `Overlay` | The overlay host and its windows, drags, menus, the settings file and the sensor client, over a pipe of the test's own. |
| `Sensors` | `src/Thermi.Sensors.Windows`: the pipe server, the service, a device that stalls, the command line, on recorded sensors. |
| `Sensors/Frames` | The service's frames: the trace's life, and the processes that present, held while they run. |
| `OnTop` | Staying above the taskbar while another process raises its windows. |
| `Window` | The settings window's process, editor, undo and fan finder, and a window on a test's settings file. |
| `Updates` | The updater, the watchdog's switch and a machine install, on a folder of the test's own. |
| `Updates/Catalog` | A release's files and its `thermi.cat`, byte for byte, for the catalog's check. |
| `Driver` | Installing the driver from the window. |
| `Logging` | The overlay's and the window's logs, and a child process that ends on a failure, which they log. |
| `Processes` | `DesktopProcess`, which starts a process on the test's desktop, unelevated when asked: WebView2 in an elevated process, as on a GitHub runner, starts its browser on the user's desktop instead. |
| `Scripts` | The repo's own scripts' rules, as the ratchet's on a raise of its baseline, on a git repository of the test's own. |
| `Acceptance` | The tests a spec's acceptance steps name. |

Entry points worth knowing:

- `TestWindows.cs`: the test's own desktop, and the check that nothing reached the user's.
- `HostRun.cs`: runs an `OverlayHost` one test at a time, pumping its messages on a recorded snapshot.
- `TestRecordings.cs`: reads a recording, shared with `tests/Thermi.Tests`.

`scripts/check.ps1` runs them in a step of their own and shows how much of thermi and thermi-sensors they run, with no gate.

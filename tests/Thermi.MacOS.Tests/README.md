# Thermi.MacOS.Tests

The tests that need macOS: `src/Thermi.Sensors.MacOS` on recordings of what a Mac's IOKit answered, its socket and the person's LaunchAgents, `thermi-cli` from zsh, bash and SSH, and `src/Thermi.MacOS`'s overlay, menu bar items and window on the Mac's own screen. Core's behavior is tested in `tests/Thermi.Tests`; these check what macOS itself does to thermi.

Each process a test starts goes through `OwnSession`, which gives it a home and a temporary folder of the test's own over an environment cleared of the person's; a repo rule holds it. Each session's thermi and `thermi-cli` read a socket of the test's own, which `THERMI_SENSORS_SOCKET` names, at a short path: a Mac takes a socket's path of at most 104 bytes.

A Mac has one screen and one launchd for its user, which no session of a test's own holds apart, as a Linux compositor or a Windows desktop does. So what shows thermi, drives the pointer or loads an agent runs only in a session made for tests, `OwnSession.OnScreen()`: the CI's macOS runner and the test Mac's user of its own set `THERMI_TEST_SESSION` to their user's name, and anywhere else such a test skips. A plain `OwnSession` refuses to start thermi, `launchctl`, `osascript` and the Mac's other tools that show or load.

| Folder | What lives there |
| --- | --- |
| `Acceptance` | The tests spec 008's acceptance steps name. |
| `Desktop` | The Mac's screen, pointer, keyboard, menus, notifications and launchd, as a session made for tests drives and reads them. |

Entry points worth knowing:

- `SensorFeed.cs` serves a recording through the real `thermi-sensors`, and `Raw.cs` changes a copy of one as another Mac would differ.
- `Desktop/TestMac.cs` is a session made for tests with the service, thermi under `--trace`, `thermi preview` and the monitors as AppKit tells them.
- `tests/Shared/Traced.cs` names the events thermi writes under `--trace`, which the tests wait for, as on Linux.

The screen is read through `screencapture` and drawn on through osascript's JavaScript, which posts CoreGraphics' events and reads menus and notifications through System Events; a session made for tests lets osascript use the accessibility and the screen's recording. Every wait is for what it waits on to say so, never for a time, and bounded (`tests/Shared/Bound.cs`).

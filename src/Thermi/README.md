# Thermi

`thermi.exe`, the overlay. It runs as the user, without admin rights.

- `Program.cs` starts it: the single-instance mutex, the sensor client and the window, which loads the settings. It also runs `thermi preview <snapshot> <png> [scale]`.
- `SensorClient.cs` reads snapshots from the `thermi-sensors` pipe, after checking that SYSTEM or Administrators own it. While it is gone, each try waits up to 2 s to connect, then 2 s more before the next.
- `OverlayWindow.cs` is the layered Win32 window: painting, dragging, the menu, monitors and DPI. `Topmost.cs` keeps it above the taskbar (`docs/adr/0004-above-the-taskbar.md`).
- `Painter.cs` draws the pills with GDI+; the same code serves the screen and `thermi preview`.
- `Settings.cs` and `ConfigStore.cs` read and write `%APPDATA%\thermi\config.json`, never over a broken file.
- `ErrorLog.cs` writes what went wrong to `%LOCALAPPDATA%\thermi\thermi.log`.
- `Native.cs` holds every Windows call.

Its tests need Windows and live in `tests/Thermi.Windows.Tests`.

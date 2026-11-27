# Thermi.Linux.Tests

The tests that need Linux: `src/Thermi.Sensors.Linux` on recorded trees of `/sys` and `/proc`, its socket, `thermi-cli` from bash, zsh and fish, and `src/Thermi.Linux`'s overlay and window against a headless GNOME Shell, KWin, Sway and an X11 desktop. Core's behavior is tested in `tests/Thermi.Tests`; these check what Linux itself does to thermi.

They never touch the person's desktop: each compositor and process a test starts gets a session of its own, its own runtime folder, display and D-Bus session bus, never the person's, and a repo rule holds it.

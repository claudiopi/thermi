# Thermi.Linux.Tests

The tests that need Linux: `src/Thermi.Sensors.Linux` on recorded trees of `/sys` and `/proc`, its socket, `thermi-cli` from bash, zsh and fish, and `src/Thermi.Linux`'s overlay and window against a headless GNOME Shell, KWin, Sway and an X11 desktop. Core's behavior is tested in `tests/Thermi.Tests`; these check what Linux itself does to thermi.

They never touch the person's desktop: each compositor and process a test starts goes through `OwnSession`, which gives it a home, a runtime folder, Wayland and X11 displays and a D-Bus session bus of the test's own, over an environment cleared of the person's; a repo rule holds it.

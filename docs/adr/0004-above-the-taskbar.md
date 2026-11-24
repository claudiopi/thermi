# 0004. The overlay stays above the taskbar on its own

**Status**: Accepted

## Context

Sitting on the taskbar is the main use of thermi. Windows 11 lifts the taskbar over every topmost window when it is clicked or opened, so a plain topmost window ends up hidden behind it. Other tools leave users running scripts that re-raise them every few seconds. thermi must never need one.

## Decision

- Windows reports every change in the order of windows. When one leaves a taskbar above the overlay, the overlay goes back on top at once (`src/Thermi/Windows/Topmost.cs`). The once-per-second repaint does it too, as a safety net.
- It reacts only to taskbars, never to other windows: two always-on-top apps answering each other would fight forever.
- When Explorer restarts or the monitors change, the overlay goes back on top, and onto a screen if its monitor is gone.
- **For 1.0**: the signed build sets `uiAccess="true"` in its manifest. Windows then puts the overlay in a layer above the taskbar (the one the Magnifier uses) and the re-raising becomes a fallback. It requires a signed binary under Program Files, which the installer provides.

## Rejected

- **A separate keep-on-top process or scheduled task**: the workaround thermi exists to remove.
- **Re-raising many times per second**: costs CPU all day for what the reorder event already catches.
- **Hooks injected into Explorer**: fragile across Windows updates and looks like malware.

# FPS follows a maximized terminal instead of the game on the other monitor

**Labels**: `bug`, `severity: wrong value`, `area: fps`

| Field | |
| --- | --- |
| thermi version | 0.9.0 |
| Windows version | Windows 11 |
| Hardware | Two monitors, one at 164 Hz and one at 180 Hz. |

## What did you expect?

FPS shows the game's frames, Zwift's, while I type in a terminal on the other monitor, as spec 005's R2 says: "The game stays it while it presents, even when another window, as a terminal on another monitor, takes the focus".

## What did you see instead?

FPS read 180 and frame time 6.02 ms, while Zwift's own counter showed far less. thermi showed the terminal's frames, not the game's.

## Steps to reproduce

1. Ride in Zwift on one monitor; FPS shows its frames.
2. Maximize a terminal that repaints as it streams text, as Wave (an Electron app), on the 180 Hz monitor, and type in it.
3. After 3 s, FPS reads the terminal's 180.

## Cause

`FrontGame` let an app in front take over from the game when, for 3 s in a row, its window filled its monitor and it presented at 30 FPS or more and faster than the game. A maximized terminal that streams text presents at its monitor's refresh rate without a pause, so it met the rule, though the game was still in sight on the other monitor.

## Reproduction

`FpsFollowsTheGameNotTheFocus`, in `tests/Thermi.Tests/Acceptance/FrameTests.cs`: Zwift presenting at 75 on one monitor and the terminal in front, maximized on the other, presenting at 180, for 4 s.

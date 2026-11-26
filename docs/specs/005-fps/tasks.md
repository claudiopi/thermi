# 005-fps: Tasks

**Status**: built in v1; how it was built is in git's history.

## Open

By hand on the development PC, before publishing:

- **Compare thermi's FPS with Zwift's counter and PresentMon by hand**: on the installed build, ride Zwift windowed, borderless and in full screen, and run free Direct3D 9, 11 and 12 and Vulkan demos beside PresentMon; done when the window's FPS is within 2 % of each over a minute and stays on Zwift while a terminal on the other monitor has the focus (A6).
- **Accept ADR 0014 once the comparison holds**: `docs/adr/0014-frames-from-etw.md` is still proposed; done when A6 passes and the ADR says accepted.
- **Measure the service while a game presents at 144 FPS**: run `scripts/measure.ps1` for 60 s on the installed build; done when the service stays under 3 % of one core and 80 MB, and no `thermi-frames` session exists with no client connected (A7).

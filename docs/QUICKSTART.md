# thermi in ten minutes

You just arrived. This page tells you what thermi is and how it works, as a story. Read it once, top to bottom. It has no commands and few code names; those are in [ARCHITECTURE.md](ARCHITECTURE.md).

## Why thermi exists

People who care about their PC want to see its temperatures, loads and fans while they work or play, without opening a window. Today that takes a heavy tool, or a small one that hides behind the taskbar, plus a script that keeps pulling it back on top. thermi is one light overlay that sits anywhere, the taskbar included, and stays there on its own.

## The journey of a reading

1. **A service reads the sensors.** Reading CPU and motherboard sensors needs admin rights. A small Windows service, `thermi-sensors`, starts with Windows and has them, so the user is never asked. It reads only while someone is listening.
2. **It sends a snapshot every second.** A snapshot is every sensor of the kinds thermi shows (temperatures, loads, fans, power, clocks, memory), with its value, sent as one line of text down a named pipe. The pipe can only be read, so nothing can send the service commands.
3. **The overlay listens.** The overlay runs as the user, without admin rights. Before trusting the pipe, it checks that SYSTEM or an administrator made it, so a program without admin rights cannot feed it fake numbers.
4. **It picks what to show.** The settings say which metrics to show: the CPU temperature, the GPU load, the RAM used. Rules find the sensor behind each one, because sensor names differ between PCs.
5. **It paints.** Values are grouped into pills (FAN, CPU, GPU, RAM), with meters that turn yellow and red at thresholds. Each value keeps the room of its widest number, so the overlay never jitters.
6. **It stays on top.** Windows lifts the taskbar over everything when it is clicked. The overlay notices at once and goes back on top.

If anything is missing, thermi shows `–`, never a zero and never an old number that looks live: a value that cannot be read must look unread.

## The journey of a setting

The settings are a JSON file in the user's profile. When it changes, the overlay reloads it at once. If it is broken, the overlay keeps the last good settings, shows "CONFIG error", and never writes over the user's file. A settings window will edit the same file (spec 002).

## Five ideas you need

- **Two processes.** The service has admin rights and reads hardware; the overlay has none and draws. Each does one job, and a crash in one never takes the other down.
- **Core knows no Windows.** Everything that can be decided without Windows, which metric is which sensor, how a value reads, what the overlay shows, lives in Core and is tested without hardware.
- **Recordings, not mocks.** Tests replay snapshots recorded on real PCs, with and without admin rights. Fakes kinder than real hardware hide the bugs users hit.
- **Missing is not zero.** Without its driver, a CPU reports 0 °C. thermi treats that as missing, because no running chip is at 0 °C.
- **The user's file is sacred.** thermi reads it, never overwrites it while it is broken, and keeps what the user wrote.

## How work gets done

Every feature starts with a spec that a person approves (`docs/specs`). Its acceptance tests come first, then the code. An agent prunes the comments, `scripts/check.ps1` passes, another agent reviews, and a person tests it on real hardware before it merges. Why things are the way they are lives in `docs/adr`.

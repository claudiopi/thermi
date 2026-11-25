# thermi in ten minutes

You just arrived. This page tells you what thermi is and how it works, as a story. Read it once, top to bottom. It has no commands and few code names; those are in [ARCHITECTURE.md](ARCHITECTURE.md).

## Why thermi exists

People who care about their PC want to see its temperatures, loads and fans while they work or play, without opening a window. Today that takes a heavy tool, or a small one that hides behind the taskbar, plus a script that keeps pulling it back on top. thermi is one light overlay that sits anywhere, the taskbar included, and stays there on its own.

## The journey of a reading

1. **A service reads the sensors.** Reading CPU and motherboard sensors needs admin rights. A small Windows service, `thermi-sensors`, has them, so you are never asked. It reads only while someone listens, and a device that hangs never holds back the others.
2. **It sends a snapshot every second.** A snapshot is every sensor thermi can show, with its value, sent as one line of text down a named pipe. The pipe only goes one way, so nothing can send the service a command.
3. **The overlay listens.** The overlay runs as you, without admin rights. It trusts the pipe only when SYSTEM or an administrator made it, so a program without admin rights cannot feed it fake numbers.
4. **It picks what to show.** The settings say which metrics to show: the CPU temperature, the GPU load, the RAM used. Rules find the sensor behind each one, because sensor names differ between PCs.
5. **It paints.** Values are grouped into pills by their label: CPU, GPU, RAM. A value at its warning threshold is underlined in the warning color; at the critical one it is drawn reversed. Each value keeps the room of its widest number, so the overlay never jitters.
6. **It stays on top.** Windows lifts the taskbar over everything when it is clicked. The overlay notices at once and goes back on top.
7. **It stays where you put it.** Dropped on the taskbar, it stays where you let go. Near a corner it snaps. Two overlays on one taskbar never cover each other. Hold a pill still for half a second and you can drag it off into an overlay of its own. Anything you change can be undone.

If a value cannot be read, thermi shows `–`. Never a zero, and never an old number that looks live.

## The journey of a setting

The settings are a JSON file in your profile. When it changes, the overlay reloads it at once. If it is broken, the overlay keeps the last good settings, says so, and never writes over your file.

The settings window edits the same file, in a process of its own that closes with it. It is a web page shown in WebView2, the browser engine Windows ships. The page decides nothing: it hands each change to C#, which checks it with the same rules the overlay follows and keeps it for undo. Even the window's preview of an overlay is painted by the overlay's own painter, so the window never shows an overlay other than it is.

## Six ideas you need

| Idea | Why |
| --- | --- |
| **Separate processes.** The service reads hardware with admin rights; the overlay draws; the window edits; `thermi-cli` answers AI agents. | Each does one job, nothing an agent talks to is elevated, and a crash in one never takes another down. |
| **Core knows no Windows.** Which metric is which sensor, how a value reads, what the overlay shows: all in Core. | It is tested without hardware, and it would move to another OS unchanged. |
| **Recordings, not mocks.** Tests replay snapshots recorded on real PCs, with and without admin rights. | Fakes kinder than real hardware hide the bugs users hit. |
| **Missing is not zero.** Without its driver, a CPU reports 0 °C. thermi treats that as missing. | No running chip is at 0 °C. |
| **Your file is sacred.** thermi never overwrites it while it is broken, and keeps what you wrote. | Losing someone's settings loses their trust. |
| **Updates are invisible, and undone when they fail.** Each version has a folder of its own, and the service goes back to the one that worked. | An update must never leave a PC without its overlay. |

## How work gets done

Every feature starts with a spec that a person approves (`docs/specs`). Its acceptance tests come first, then the code. An agent prunes the comments, `scripts/check.ps1` passes, another agent reviews, and a person tests it on real hardware before it merges. Why things are the way they are lives in `docs/adr`.

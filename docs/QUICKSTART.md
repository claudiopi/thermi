# thermi in ten minutes

You just arrived. This page tells you what thermi is and how it works, as a story. Read it once, top to bottom. It has no commands and few code names; those are in [ARCHITECTURE.md](ARCHITECTURE.md).

## Why thermi exists

People who care about their PC want to see its temperatures, loads and fans while they work or play, without opening a window. Today that takes a heavy tool, or a small one that hides behind the taskbar, plus a script that keeps pulling it back on top. thermi is one light overlay that sits anywhere, the taskbar included, and stays there on its own.

## The journey of a reading

1. **A service reads the sensors.** Reading CPU and motherboard sensors needs admin rights. A small Windows service, `thermi-sensors`, starts with Windows and has them, so the user is never asked. It reads only while someone is listening, and a device that hangs never holds back the others.
2. **It sends a snapshot every second.** A snapshot is every sensor of the kinds thermi shows (temperatures, loads, fans, power, clocks, voltages, memory, and the frames each app presented), with its value, sent as one line of text down a named pipe. The pipe can only be read, so nothing can send the service commands.
3. **The overlay listens.** The overlay runs as the user, without admin rights. Before trusting the pipe, it checks that SYSTEM or an administrator made it, so a program without admin rights cannot feed it fake numbers.
4. **It picks what to show.** The settings say which metrics to show: the CPU temperature, the GPU load, the RAM used. Rules find the sensor behind each one, because sensor names differ between PCs.
5. **It paints.** Values are grouped into pills by their label (CPU, GPU, RAM by default). Beside a value it may draw bars or a line of its last minute. An item is judged by its own reading or by another form of it, as a fan by its duty: at a warning its bars or line take the warning color, or with neither the value is underlined in it, and at the critical one the value is drawn reversed on the critical color. Each value keeps the room of its widest number, so the overlay never jitters.
6. **It stays on top.** Windows lifts the taskbar over everything when it is clicked. The overlay notices at once and goes back on top.
7. **It stays where you put it.** Dropped on the taskbar, the overlay sits where it was let go along it, over the app buttons too, and moves only to stay on the taskbar when the taskbar or the screen changes. Near a corner or the taskbar's ends it snaps; Alt drops it freely. Overlays on one taskbar never cover each other: one that grows into the next pushes it toward the clock, and a drop where there is no room is refused. A pill held still for half a second by its label lifts, and can be dragged off into an overlay of its own or onto another; held by a value, only that item lifts, and can also move within its pill. A right-click opens a menu for the pill under the pointer, and anything it changes can be undone.

If anything is missing, thermi shows `–`, never a zero and never an old number that looks live: a value that cannot be read must look unread.

## The journey of a setting

The settings are a JSON file in the user's profile. When it changes, the overlay reloads it at once. If it is broken, the overlay keeps the last good settings, shows "CONFIG error", and never writes over the user's file.

The settings window edits the same file, in a process of its own that closes with it. It is a web page, the same on any PC, shown by a plain window through WebView2, the browser engine Windows ships. The page decides nothing: it asks C# what to show and hands each change back through one typed bridge, and C# checks it against the same rules the overlay follows, keeps it for undo, and never writes over a broken file. Even the preview of an overlay at the top of its page is a picture C# paints with the overlay's own painter, and the window's charts start from the history the overlay kept, so the window never shows an overlay other than it is.

## Five ideas you need

- **Separate processes.** The service has admin rights and reads hardware; the overlay has none and draws; the settings window, while open, edits. Each does one job, and a crash in one never takes another down.
- **Core knows no Windows.** Everything that can be decided without Windows, which metric is which sensor, how a value reads, what the overlay shows, lives in Core and is tested without hardware.
- **Recordings, not mocks.** Tests replay snapshots recorded on real PCs, with and without admin rights. Fakes kinder than real hardware hide the bugs users hit.
- **Missing is not zero.** Without its driver, a CPU reports 0 °C. thermi treats that as missing, because no running chip is at 0 °C.
- **The user's file is sacred.** thermi reads it, never overwrites it while it is broken, and keeps what the user wrote.

## How work gets done

Every feature starts with a spec that a person approves (`docs/specs`). Its acceptance tests come first, then the code. An agent prunes the comments, `scripts/check.ps1` passes, another agent reviews, and a person tests it on real hardware before it merges. Why things are the way they are lives in `docs/adr`.

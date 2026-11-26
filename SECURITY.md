# Security

thermi reads hardware sensors through a kernel driver, runs two Windows services as SYSTEM, updates itself over the internet without asking, and lets AI agents read and change its settings. Security reports matter a lot.

## Report a vulnerability

Report it privately: open the **Security** tab of this repository and choose **Report a vulnerability**. Do not open a public issue, pull request or discussion about it.

Say which version you found it in (About shows it), your Windows version, and whether thermi is installed for the whole PC or for one user. Say what an attacker starts with (a standard user, another user of the same PC, a program running as the user, a sandboxed process, an MCP client, someone on the network or between the PC and the update server) and what they end with, and the steps to reproduce it. A proof of concept helps but is not required.

You will get an answer within 7 days. Fixes go out as soon as they are ready, through thermi's own updates, and you are credited unless you prefer otherwise.

## Supported versions

Only the latest release gets security fixes.

## What runs, and with which rights

thermi keeps the parts with rights small and gives them nothing to parse. Everything a person or an agent talks to runs without them.

| Part | Runs as | What it trusts, and what reaches it |
| --- | --- | --- |
| `thermi-sensors` | LocalSystem, one privilege kept | Reads the sensors through LibreHardwareMonitor and the driver, and writes them to the pipe `thermi-sensors`, which it never reads: no client input to parse. Local users may read the pipe, the network may not, and each Windows session gets only its share of connections. For FPS it traces presents through ETW while a client listens, and sends process ids and numbers, never names. |
| The updater, in `thermi-sensors` | LocalSystem | Trusts neither TLS nor the server. The manifest must be signed by a key built into thermi, never go back in sequence, and not be expired; the zip must match the size and SHA-256 it signs; in a release, every file must be in a catalog signed by thermi's certificate. Each version lives in a folder only administrators, SYSTEM and Windows' installer may change, and one that does not serve is rolled back. Users reach it only through a few control codes that make it check or switch sooner. |
| `thermi-elevated` | LocalSystem, started only when asked | Only administrators and SYSTEM may start it, behind one UAC prompt. It runs the window's two fixes, start the sensors and install the driver from the one installer URL built into thermi, checked by size, SHA-256 and signature. It takes a verb and nothing else, with the machine's environment, not the user's. |
| The overlay, the settings window, `thermi-cli` | the user, never elevated | Started from an elevated terminal, the overlay and the window exit, and `thermi-cli` only reads. The window's page talks to C# only through the bridge. |
| `thermi-cli mcp` | the user, through the agent's own client | An MCP server on stdio. It opens no port, holds no connection between calls, never asks for admin rights, and takes no path, URL or file to load. Its edits follow the settings window's rules. |
| A per-user install | the user | No service. The sensors run as the user's own process, whose pipe only that user, SYSTEM and administrators may open; thermi checks its owner, its process and their integrity levels before reading it. **Install for everyone** downloads the machine MSI the signed manifest lists, checks it as an update, and runs it behind one UAC prompt. |

## Scope

In scope:

- Anything that lets a user, a program or a sandboxed process gain SYSTEM or administrator rights through thermi: either service, the elevated fixes, the installers for the PC or for one user, and `scripts/install.ps1`.
- Anything that makes a PC run code thermi did not sign and publish, run an older version than it has, or stop getting updates for good: through the manifest, the zip and its unpacking, the switch between versions or a rollback, the folders under Program Files, or the service's control codes.
- Posing as the sensor service or as a user's own sensor process, reading another user's readings beyond what the pipe shares with every user of the PC, keeping another session from its readings, or crashing the service.
- `thermi-cli` and its MCP server reading or writing anything beyond thermi's settings and saved looks, running anything elevated, or reaching another user's files.
- The settings window's page and its bridge, and how thermi reads its settings, look files and command line.
- Anything thermi sends off the PC beyond the anonymous update check and the downloads of updates, the machine installer and the driver's installer.

Out of scope:

- What an MCP client or agent the person connected does with thermi's tools: changing thermi's settings is what they are for.
- An agent that changes the settings file itself, with a shell or file tools of its own. Settings' "Let agents change settings" governs what agents ask `thermi-cli` and its MCP server for; it is no sandbox, and the file is the person's, as everything else they run may write it.
- An attack that needs administrator rights to begin with.
- Bugs in [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), the [PawnIO](https://github.com/namazso/PawnIO) driver and the [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk): please report them upstream.

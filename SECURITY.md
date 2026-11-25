# Security

thermi reads hardware sensors through a kernel driver, runs two Windows services as SYSTEM, updates itself over the internet without asking, and lets AI agents read and change its settings. Security reports matter a lot.

## Report a vulnerability

Report it privately: open the **Security** tab of this repository and choose **Report a vulnerability**. Do not open a public issue, pull request or discussion about it.

Say which version you found it in (About shows it), your Windows version, and whether thermi is installed for the whole PC or for one user. Say what an attacker starts with (a standard user, another user of the same PC, a program running as the user, a sandboxed process, an MCP client, someone on the network or between the PC and the update server) and what they end with, and the steps to reproduce it. A proof of concept helps but is not required.

You will get an answer within 7 days. Fixes go out as soon as they are ready, through thermi's own updates, and you are credited unless you prefer otherwise.

## Supported versions

Only the latest release gets security fixes.

## What runs, and with which rights

- **`thermi-sensors`**, a service running as LocalSystem with only one of its privileges kept, reads the sensors through LibreHardwareMonitor and the driver. It writes them to the named pipe `thermi-sensors` and never reads from it, so there is no client input to parse. Local interactive users may read the pipe, network clients are denied, and thermi's programs trust it only when SYSTEM or administrators own it. A Windows session may hold only its share of the pipe's connections. While a client is connected, the service also traces through ETW when programs present frames, for the FPS, and sends only process ids and numbers, never a process's name.
- **The updater** lives in the same service. It downloads the channel's manifest and a version's zip over HTTPS, but trusts neither TLS nor the server: the manifest must carry a signature by a key built into thermi, its sequence never goes back, and it expires; the zip must have the size and SHA-256 the manifest signs, and in a release every file in it must be listed in a catalog signed by thermi's code-signing certificate. Each version lives in a folder of its own under Program Files, which only administrators, SYSTEM and Windows' installer may change, and a version that does not serve is rolled back by itself. A check is one anonymous request with no identifier. Users reach the updater only through a few service control codes, which make it check or switch sooner.
- **`thermi-elevated`**, a second service as LocalSystem, starts only when asked, and only administrators and SYSTEM may start it, so the settings window or the overlay asks for one UAC prompt. It runs the window's two fixes: start the sensors, and install the driver from the one installer URL built into thermi, checked by size, SHA-256 and signature. It takes a verb and nothing else from whoever starts it, and runs with the machine's environment, not the user's.
- **The overlay, the settings window and `thermi-cli`** run as the user, never elevated. The window's page talks to its C# only through the bridge.
- **`thermi-cli mcp`** is an MCP server on stdio for AI agents, run as the user by the agent's own client. It opens no port, holds no connection to the sensors between calls, never asks for administrator rights, and takes no path, URL or file to load; its edits go through the same rules as the settings window's.
- **A per-user install** registers no service. The sensors then run as the user's own process, `thermi-sensors --user`, which serves a pipe only that user, SYSTEM and administrators may open, and thermi reads it only after checking its owner, the process that serves it and their integrity levels. Its **Install for everyone** downloads the machine installer the signed manifest lists, checks it as an update is checked, and runs it with one UAC prompt that names thermi.

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
- An attack that needs administrator rights to begin with.
- Bugs in [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), the [PawnIO](https://github.com/namazso/PawnIO) driver and the [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk): please report them upstream.

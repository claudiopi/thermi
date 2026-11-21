# 0002. Sensors through LibreHardwareMonitor and PawnIO

**Status**: Accepted

## Context

Reading CPU temperature, power, clocks and motherboard fans on Windows needs a kernel driver. The driver that most monitors shipped for years, WinRing0, has a known privilege escalation (CVE-2020-14979), and Microsoft Defender now flags it. A monitor that triggers antivirus alarms loses users' trust on day one.

## Decision

- Sensors come from **LibreHardwareMonitorLib** (MPL-2.0), used from NuGet and never modified. If we ever patch it, the patched files stay MPL-2.0 and are published. Only the elevated sensor host loads it (`docs/adr/0001-architecture.md`).
- Since version 0.9.5, LibreHardwareMonitor uses **PawnIO** instead of WinRing0. PawnIO is a signed driver (GPL-2.0) that only runs small verified modules. Programs that talk to it only through its device interface are covered by its license exception, and that is all thermi does. It installs with its own official installer; thermi never bundles the driver inside its binary. thermi detects whether PawnIO is installed and offers the official installer if not.
- Without PawnIO, thermi still shows what it can (GPU, RAM, disks) and says which sensors are missing and why.
- The sensor host talks to an `ISensorSource` interface. Only one class talks to LibreHardwareMonitor. A replay source plays back recorded, anonymized sensor data, so everything is developed and tested without hardware or admin rights.
- Sensor ids are our own and stable. LibreHardwareMonitor ids change between versions; we map them in one place.

## Rejected

- **WinRing0**: flagged by Defender and vulnerable.
- **HWiNFO shared memory**: proprietary, and the user must keep HWiNFO running.
- **Our own driver**: kernel code and driver signing are a burden this project should not carry.

## Consequences

- Full sensor coverage needs PawnIO installed, which needs admin rights once.
- GitHub's Windows runners run as admin, so the CI cannot catch a missing-rights bug by accident. The no-admin and no-driver paths are tested on purpose, through the replay source.
- The PR that adds LibreHardwareMonitorLib also adds `THIRD-PARTY-NOTICES.md` (with where to get its source, as MPL-2.0 asks) and a license line in the README.
- Before the first public release: verify how the main anti-cheat systems react to PawnIO being installed, and sign the binaries. A ban caused by thermi, or a SmartScreen warning on an admin tool, would be the worst first impression.

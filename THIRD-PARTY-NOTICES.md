# Third-party notices

thermi is MIT licensed. The sensor host (`thermi-sensors`) ships these libraries unmodified; their sources are at the links below.

| Library | License | Source |
| --- | --- | --- |
| LibreHardwareMonitorLib | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| BlackSharp.Core | MPL-2.0 | https://github.com/Blacktempel/BlackSharp |
| DiskInfoToolkit | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| HidSharp | Apache-2.0 | https://www.zer7.com/software/hidsharp |
| Mono.Posix.NETStandard | MIT | https://github.com/mono/mono |
| .NET libraries (System.*) | MIT | https://github.com/dotnet/runtime |

thermi does not ship the PawnIO driver that LibreHardwareMonitor uses: it is installed separately (see `docs/adr/0002-sensors.md`).

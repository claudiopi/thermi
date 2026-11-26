# 003-agents: Tasks

**Status**: built in v1; how it was built is in git's history.

## Open

- **A Node package and a Python package for the contract**: each its own spec, as thin as the .NET SDK in `src/Thermi.Client`, starting `thermi-cli` and reading its JSON.

Checks on hardware other than the development PC: [Hardware reports](https://github.com/claudiopi/thermi/issues/new?template=hardware-report.yml) (A20).

By hand on the development PC, before publishing:

- **Check thermi-cli with Claude Code and Claude Desktop by hand**: add thermi-cli with the README's one command, ask a new session how hot the CPU is and to set its warning at 70 °C; done when the answer matches the overlay and the overlay shows the warning, in both clients (A13).
- **Measure the installed build with agents' servers running**: run `scripts/measure.ps1` with and without `-Window` on the installed build; done when the overlay, the window and the service stay within their budgets (A15).
- **Update a sensors-only install from one signed beta to the next**: done when the sensors alone are installed at the new version and no overlay starts (A21).

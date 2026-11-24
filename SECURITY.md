# Security

thermi reads hardware sensors through a kernel driver, and part of it runs with admin rights. Security reports matter a lot.

## Report a vulnerability

Report it privately: open the **Security** tab of this repository and choose **Report a vulnerability**. Do not open a public issue.

You will get an answer within 7 days. Fixes go out as soon as they are ready, and you are credited unless you prefer otherwise.

## Supported versions

Only the latest release gets security fixes.

## Scope

In scope: anything that lets a user or a program gain admin rights through thermi, as through its elevated mode, which starts the sensor service or installs the driver; the pipe between the overlay and the sensor service; how thermi reads its settings, look files and command line; the window's page and its bridge; and `scripts/install.ps1`. thermi has no MCP server and no updater yet; they come into scope when they ship.

Out of scope, please report upstream: bugs in [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) and in the [PawnIO](https://github.com/namazso/PawnIO) driver.

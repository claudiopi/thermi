# Security

thermi reads hardware sensors through a kernel driver, and part of it runs with admin rights. Security reports matter a lot.

## Report a vulnerability

Report it privately: open the **Security** tab of this repository and choose **Report a vulnerability**. Do not open a public issue.

You will get an answer within 7 days. Fixes go out as soon as they are ready, and you are credited unless you prefer otherwise.

## Supported versions

Only the latest release gets security fixes.

## Scope

In scope: anything that lets a user, a program or an AI agent gain admin rights through thermi; the pipe between the overlay and the sensor host; how thermi reads its config, CLI arguments and MCP requests; the installer and updates.

Out of scope, please report upstream: bugs in [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) and in the [PawnIO](https://github.com/namazso/PawnIO) driver.

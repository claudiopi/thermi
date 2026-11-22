# Thermi.Core

Everything thermi decides without Windows: snapshots and their line format, metrics, the sensor map, formatting, thresholds, the settings file and the overlay's layout as plain data.

- **Depends on nothing**, Windows included. Its target framework is `net10.0`, and `RepoRulesTests` keeps it that way. If you need a Windows API, it belongs in `src/Thermi` or `src/Thermi.SensorHost`.
- **Keeps 90 % line and branch coverage**, checked by `scripts/check.ps1`.
- **Tested from recordings** in `tests/Thermi.Tests`: real snapshots in, what the overlay shows out.
- **Time** comes from an injected `TimeProvider`, as in `LatestSnapshot`.

Start with `SensorMap.cs` (which sensor feeds each metric) and `OverlayLayout.cs` (what the overlay shows).

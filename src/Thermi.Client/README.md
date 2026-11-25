# Thermi.Client

The .NET SDK of thermi's agents' contract (`docs/agents/README.md`): a thin library, published to NuGet as `Thermi.Client`, that reads a Windows PC's sensors through thermi. It starts `thermi-cli` with the contract's schema 1 and reads its JSON, so it has no dependency beyond .NET 10, no native code, and nothing of its own to install or update. MIT.

```csharp
var thermi = new ThermiClient();
var now = await thermi.ReadAsync(["cpu.temperature", "fan.cpu"]);
var minute = await thermi.ReadAsync(["cpu.temperature"], seconds: 60);
await foreach (var line in thermi.WatchAsync(["cpu.temperature"], stop)) { }
```

It needs thermi installed, whole or its sensors alone, for everyone or for one person. Without `thermi-cli`, on `PATH` or where thermi installs it, every call throws a `ThermiException` saying so and how to install thermi; with its service not installed, `StatusAsync` says so among its problems and every read throws why. It never installs or starts anything but `thermi-cli`.

- `ThermiClient.cs`: the calls, `status`, `read`, `sensors` and `watch`, where `thermi-cli` is found, and the errors.
- `ThermiAnswers.cs`: the contract's documents as records; a choice is the contract's word, so a word a later thermi adds still reads.
- `ThermiCli.cs`: `thermi-cli` run as a process without a window, to its end, or a watch line by line until its stdin closes.

`tests/Thermi.Tests` reads every answer of the recordings through these records, so they cannot drift from Core's.

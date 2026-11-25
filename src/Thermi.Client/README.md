# Thermi.Client

The .NET SDK of thermi's agents' contract (`docs/agents/README.md`), published to NuGet as `Thermi.Client`. It reads a Windows PC's sensors through thermi by starting `thermi-cli` and reading its JSON, so it depends on nothing beyond .NET 10, has no native code, and installs nothing. MIT.

```csharp
var thermi = new ThermiClient();
var now = await thermi.ReadAsync(["cpu.temperature", "fan.cpu"]);
var minute = await thermi.ReadAsync(["cpu.temperature"], seconds: 60);
await foreach (var line in thermi.WatchAsync(["cpu.temperature"], stop)) { }
```

It needs thermi installed, whole or its sensors alone. Without `thermi-cli`, every call throws a `ThermiException` that says how to install thermi; with no sensor service, `StatusAsync` says so and every read throws why.

- `ThermiClient.cs`: the calls, `status`, `read`, `sensors` and `watch`, and where `thermi-cli` is found.
- `ThermiAnswers.cs`: the contract's documents as records. A word a later thermi adds still reads.
- `ThermiCli.cs`: `thermi-cli` run as a process without a window.

`tests/Thermi.Tests` reads every recorded answer through these records, so they cannot drift from Core's.

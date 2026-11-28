namespace Thermi.Tests.Shared;

/// <summary>How a program run in a session ended: its exit code, and what it wrote to each stream.</summary>
internal sealed record Ran(int Exit, string Out, string Error);

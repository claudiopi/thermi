using System.CommandLine;
using Thermi.Cli.Commands;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// What <c>thermi</c> makes of its arguments on every OS, read before anything runs: arguments that make no sense are
/// null, which exits with 2.
/// </summary>
public sealed class CommandLineTests
{
    // A snapshot that exists, since a preview refuses one that does not.
    private static readonly string Snapshot = TestRecordings.Path(TestRecordings.Admin);

    [Theory]
    [InlineData(new string[0], 0)]
    [InlineData(new[] { "--overlay", "2" }, 2)]
    [InlineData(new[] { "--overlay", "0" }, null)]
    [InlineData(new[] { "--overlay", "two" }, null)]
    [InlineData(new[] { "--overlay" }, null)]
    [InlineData(new[] { "--other" }, null)]
    [InlineData(new[] { "--overlay", "2", "extra" }, null)]
    public void SettingsTakesAnOverlayCountedFromOne(string[] args, int? overlay)
    {
        var request = Parse(["settings", .. args], out var errors);

        Assert.Equal(overlay, (request as SettingsRequest)?.Overlay);
        Assert.Equal(overlay is null, request is null);
        Assert.Equal(overlay is null, errors.Count > 0);
    }

    // Without --trace the debug events are left out; the option goes before or after the command.
    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--trace" }, true)]
    public void TheOverlayRunsWithNoCommand(string[] args, bool trace)
    {
        Assert.Equal(new OverlayRequest(trace), Parse(args, out _));
        Assert.Equal(new SettingsRequest(2, trace, Pseudo: false), Parse(["settings", .. args, "--overlay", "2"], out _));
        Assert.Equal(new SettingsRequest(0, trace, Pseudo: false), Parse([.. args, "settings"], out _));
    }

    // The scale is read in the invariant culture, so 1.5 is the same in every language. The settings file may be missing:
    // the preview paints the defaults then, and creates none.
    [Theory]
    [InlineData(new[] { "o.png" }, 1f, null)]
    [InlineData(new[] { "o.png", "1.5" }, 1.5f, null)]
    [InlineData(new[] { "o.png", "0.25" }, 0.25f, null)]
    [InlineData(new[] { "o.png", "8" }, 8f, null)]
    [InlineData(new[] { "o.png", "2", "c.json" }, 2f, "c.json")]
    public void APreviewTakesASnapshotAPngAScaleAndSettings(string[] args, float scale, string? settings)
    {
        Assert.Equal(new PreviewRequest(Snapshot, "o.png", scale, settings), Parse(["preview", Snapshot, .. args], out _));
    }

    // A comma is no decimal point, and a scale past 8 or under a quarter is a typo, never a picture worth painting.
    [Theory]
    [InlineData("o.png", "1,5")]
    [InlineData("o.png", "NaN")]
    [InlineData("o.png", "0.2")]
    [InlineData("o.png", "8.5")]
    [InlineData("o.png", "1", "c.json", "more")]
    [InlineData]
    public void APreviewWithoutItsFilesOrWithABadScaleExitsWithTwo(params string[] args)
    {
        Assert.Null(Parse(["preview", Snapshot, .. args], out var errors));
        Assert.NotEmpty(errors);
    }

    // Every OS's command line, with no command of a host's own and none it refuses.
    private static IThermiRequest? Parse(IReadOnlyList<string> args, out IReadOnlyList<string> errors) =>
        ThermiCommandLine.Parse(args, new Dictionary<Command, Func<ParseResult, IThermiRequest>>(), _ => [], out errors);
}

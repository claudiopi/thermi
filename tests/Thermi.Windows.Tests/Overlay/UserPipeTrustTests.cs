using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Thermi.Core.Install;
using Thermi.Install;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>
/// Spec 004, A20: what the overlay reads of the process behind a user's pipe, from Windows itself: its image, its integrity,
/// and the pipe's own label, which a sandboxed process that made the pipe and left gives it and cannot raise.
/// </summary>
public sealed class UserPipeTrustTests
{
    // A pipe with no label reads as medium, one a low process made as low, so a host that later got its id is not read.
    [Theory]
    [InlineData("", UserHost.Medium)]
    [InlineData("S:(ML;;NW;;;LW)", 0x1000)]
    public async Task ThePipesServerIsReadAsWindowsHasIt(string label, int read)
    {
        var name = $"thermi-test-{Guid.NewGuid():N}";
        using var user = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm($"O:{user.User!.Value}D:(A;;GA;;;{user.User.Value}){label}");
        await using var server = NamedPipeServerStreamAcl.Create(
            name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
        var waiting = server.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
        await client.ConnectAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await waiting;

        var seen = SensorPipes.ServerOf(client, user.User.Value);

        using var self = Process.GetCurrentProcess();
        Assert.Equal((self.MainModule!.FileName, read), (seen?.Image, seen?.Label));
        Assert.True(seen!.Integrity >= UserHost.Medium);
    }
}

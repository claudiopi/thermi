using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Thermi.Core.Install;
using Thermi.Install;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Security.Authorization;

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
        await using var server = NamedPipeServerStreamAcl.Create(
            name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, null, HandleInheritability.None,
            PipeAccessRights.ChangePermissions | PipeAccessRights.TakeOwnership);
        if (label.Length > 0)
        {
            Label(server, label);
        }

        var waiting = server.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
        await client.ConnectAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await waiting;

        var seen = SensorPipes.ServerOf(client, user.User!.Value);

        using var self = Process.GetCurrentProcess();
        Assert.Equal((self.MainModule!.FileName, read), (seen?.Image, seen?.Label));
        Assert.True(seen!.Integrity >= UserHost.Medium);
    }

    // A label below the process's own, which its owner may set on the pipe after it is made.
    private static unsafe void Label(NamedPipeServerStream pipe, string sddl)
    {
        Assert.True(TestPInvoke.ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, TestPInvoke.SDDL_REVISION_1, out var descriptor, out _));
        try
        {
            BOOL present;
            BOOL defaulted;
            ACL* sacl;
            Assert.True(TestPInvoke.GetSecurityDescriptorSacl(descriptor, &present, &sacl, &defaulted));
#pragma warning disable S3869 // CsWin32's overload that takes the handle safely copies only the list's header, which Windows refuses.
            Assert.Equal(WIN32_ERROR.ERROR_SUCCESS, TestPInvoke.SetSecurityInfo(
                (HANDLE)pipe.SafePipeHandle.DangerousGetHandle(), SE_OBJECT_TYPE.SE_KERNEL_OBJECT, OBJECT_SECURITY_INFORMATION.LABEL_SECURITY_INFORMATION,
                default, default, null, sacl));
#pragma warning restore S3869
        }
        finally
        {
            TestPInvoke.LocalFree((HLOCAL)descriptor.Value);
        }
    }
}

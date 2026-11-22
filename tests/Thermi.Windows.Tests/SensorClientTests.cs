using System.Security.Principal;

namespace Thermi.Windows.Tests;

/// <summary>Who the overlay accepts as the sensor host: only a pipe the service or an administrator made.</summary>
public sealed class SensorClientTests
{
    [Theory]
    [InlineData(WellKnownSidType.LocalSystemSid)]
    [InlineData(WellKnownSidType.BuiltinAdministratorsSid)]
    public void TrustsAPipeOwnedByTheServiceOrAdministrators(WellKnownSidType owner)
    {
        Assert.True(SensorClient.IsTrustedOwner(new SecurityIdentifier(owner, null)));
    }

    [Fact]
    public void DoesNotTrustAPipeOwnedByAnOrdinaryUser()
    {
        using var me = WindowsIdentity.GetCurrent();

        Assert.False(SensorClient.IsTrustedOwner(me.User));
        Assert.False(SensorClient.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)));
        Assert.False(SensorClient.IsTrustedOwner(null));
    }
}

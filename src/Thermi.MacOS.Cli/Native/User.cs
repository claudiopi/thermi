using System.Runtime.InteropServices;

namespace Thermi.MacOS.Cli.Native;

/// <summary>
/// The user this process acts as, by libSystem's <c>geteuid</c>, which .NET does not wrap: the person, whose socket a
/// client trusts and whose launchd domains <c>thermi register</c> loads the agents into.
/// </summary>
internal static partial class User
{
    private const string Library = "/usr/lib/libSystem.B.dylib";

    public static uint Id => EffectiveUserId();

    [LibraryImport(Library, EntryPoint = "geteuid")]
    private static partial uint EffectiveUserId();
}

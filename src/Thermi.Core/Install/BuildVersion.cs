using System.Reflection;

namespace Thermi.Core.Install;

/// <summary>The version a build stamps on its assemblies, from Directory.Build.props, as About and the agents say it.</summary>
public static class BuildVersion
{
    // The SDK stamps every assembly the build makes.
    public static string Of(Assembly assembly) => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
}

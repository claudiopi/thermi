using System.Reflection;

namespace Thermi.Core.Install;

/// <summary>The version a build stamps on its assemblies, from Directory.Build.props, as About and the agents say it.</summary>
public static class BuildVersion
{
    public static string Of(Type type) => type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
}

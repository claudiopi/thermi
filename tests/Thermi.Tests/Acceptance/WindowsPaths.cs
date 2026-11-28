namespace Thermi.Tests.Acceptance;

/// <summary>
/// Windows' install paths, which the tests write as Windows does, in the separator of the OS they run on: Core reads them
/// with <see cref="Path"/>, which on Linux splits a path only at a slash, so the same layout holds on both.
/// </summary>
internal static class WindowsPaths
{
    public static string Native(string path) => path.Replace('\\', Path.DirectorySeparatorChar);
}

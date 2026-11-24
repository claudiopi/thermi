using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.LibraryLoader;

namespace Thermi.Windows;

/// <summary>
/// Windows' light or dark mode for apps, which the settings window's caption and thermi's menus take. Windows' own
/// popup menus turn dark only through uxtheme's preferred app mode, which it exports by ordinal alone, as Explorer and
/// Windows' inbox apps use it; where those ordinals are missing, the menus stay light.
/// </summary>
internal static class AppMode
{
    private const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // uxtheme's SetPreferredAppMode and FlushMenuThemes, Windows 10 1903 and later.
    private const int SetPreferredAppMode = 135;
    private const int FlushMenuThemes = 136;

    // PreferredAppMode's ForceDark and ForceLight; under high contrast, Default lets the menus take the system's colors.
    private const int Default = 0;
    private const int ForceDark = 2;
    private const int ForceLight = 3;

    public static bool LightApps() => Registry.GetValue(Personalize, "AppsUseLightTheme", 1) is not 0;

    /// <summary>The menus this process shows next take the apps' mode now, or the system's colors under high contrast.</summary>
    public static unsafe void FollowInMenus()
    {
        // The library stays loaded for the process, as every window that shows a themed menu keeps it anyway.
        HMODULE module;
        fixed (char* name = "uxtheme.dll")
        {
            module = PInvoke.LoadLibraryEx(name, HANDLE.Null, LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_SEARCH_SYSTEM32);
        }

        if (module.IsNull)
        {
            return;
        }

        var setMode = PInvoke.GetProcAddress(module, (PCSTR)(byte*)SetPreferredAppMode);
        var flush = PInvoke.GetProcAddress(module, (PCSTR)(byte*)FlushMenuThemes);
        if (setMode.IsNull || flush.IsNull)
        {
            return;
        }

        var mode = Desktop.HighContrast() is not null ? Default : LightApps() ? ForceLight : ForceDark;
        _ = ((delegate* unmanaged[Stdcall]<int, int>)setMode.Value)(mode);
        ((delegate* unmanaged[Stdcall]<void>)flush.Value)();
    }
}

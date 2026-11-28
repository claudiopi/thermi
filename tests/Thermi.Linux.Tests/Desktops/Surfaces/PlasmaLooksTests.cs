using Thermi.Linux.Desktop;

namespace Thermi.Linux.Tests.Desktops.Surfaces;

/// <summary>
/// "Match taskbar" follows the colour KDE Plasma paints its panel in, as Windows' follows its taskbar's: the Plasma
/// theme's own colours, or the colour scheme's for a theme that has none.
/// </summary>
public sealed class PlasmaLooksTests
{
    // Kubuntu 26.04's Plasma 6.6 as installed: the Breeze Light colour scheme, and Kubuntu's global theme, whose defaults
    // choose the Kubuntu Plasma theme, which paints the panel in its own dark colours.
    private const string Scheme = "[General]\nColorScheme=BreezeLight\n\n[Colors:Window]\nBackgroundAlternate=227,229,231\nBackgroundNormal=239,240,241\n";
    private const string Kubuntu = "[Colors:Window]\nBackgroundNormal=32,35,38\nForegroundNormal=252,252,252\n";

    // A panel light or dark: as installed, Kubuntu's theme's dark one; with Breeze Light chosen, its Plasma theme, Breeze,
    // has no colours of its own and takes the light scheme's.
    [Theory]
    [InlineData("", false)]
    [InlineData("default", true)]
    public void MatchTaskbarFollowsThePanelsColour(string chosen, bool light)
    {
        var root = Directory.CreateTempSubdirectory("thermi-plasma-").FullName;
        try
        {
            var (config, data) = (Path.Combine(root, "config"), Path.Combine(root, "data"));
            Write(Path.Combine(config, "kdeglobals"), Scheme);
            Write(Path.Combine(config, "kdedefaults", "plasmarc"), "[Theme]\nname=kubuntu\n");
            Write(Path.Combine(data, "plasma", "desktoptheme", "kubuntu", "colors"), Kubuntu);
            Write(Path.Combine(data, "plasma", "desktoptheme", "default", "metadata.json"), "{}");
            if (chosen.Length > 0)
            {
                Write(Path.Combine(config, "plasmarc"), $"[Theme]\nname={chosen}\n");
            }

            Assert.Equal(light, DesktopLooks.LightPlasmaPanels(config, [], [data]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }
}

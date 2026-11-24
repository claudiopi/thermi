using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Settings;

namespace Thermi.Core.Look;

/// <summary>
/// The saved styles: one style file each in a folder, which for the window is <c>%APPDATA%\thermi\styles</c>. The name
/// inside a file is what counts, unique whatever the case; its file name is made from it, so any name makes a valid file
/// name of its own. Saving, renaming and deleting change these files and never the look, so undo does not touch them;
/// a file that cannot be written throws its <see cref="IOException"/>.
/// </summary>
public sealed class SavedStyles(string folder)
{
    // What Windows refuses in a file name, besides the control characters.
    private const string Refused = "<>:\"/\\|?*";

    private static readonly string[] Devices = ["CON", "PRN", "AUX", "NUL"];

    private static readonly string[] Ports = ["COM", "LPT"];

    private const string PortNumbers = "0123456789¹²³";

    /// <summary>Every saved style that reads well and has a name, in name order; of two files with one name, the first.</summary>
    public IReadOnlyList<StyleConfig> List() =>
        [.. Files().Select(saved => saved.Style).OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Saves a copy of the style under the name, which is trimmed; the settings are not changed.</summary>
    public Refusal? Save(StyleConfig style, string name)
    {
        var trimmed = name.Trim();
        if ((NameRefusal(trimmed) ?? (Find(trimmed) is null ? null : Refusal.StyleNameTaken)) is { } refused)
        {
            return refused;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllText(FreePath(trimmed), StyleFile.Write(style with { Name = trimmed }));
        return null;
    }

    /// <summary>
    /// Renames a saved style, and in the settings and in every step of <paramref name="history"/> the overlays that chose
    /// it, so the settings keep naming it and neither undo nor redo brings the old name back.
    /// </summary>
    public EditResult Rename(ThermiConfig config, UndoHistory history, string from, string to)
    {
        var trimmed = to.Trim();
        if (Find(from) is not var (file, style))
        {
            return ConfigEdits.Refuse(config, Refusal.StyleNotFound);
        }

        var taken = Find(trimmed) is var (other, _) && other != file;
        if ((NameRefusal(trimmed) ?? (taken ? Refusal.StyleNameTaken : null)) is { } refused)
        {
            return ConfigEdits.Refuse(config, refused);
        }

        var path = string.Equals(Path.GetFileName(file), FileNameOf(trimmed), StringComparison.OrdinalIgnoreCase) ? file : FreePath(trimmed);
        File.WriteAllText(path, StyleFile.Write(style with { Name = trimmed }));
        if (path != file)
        {
            File.Delete(file);
        }

        history.RenameStyle(from, trimmed);
        return new EditResult(Renamed(config, from, trimmed));
    }

    /// <summary>Deletes a saved style. The overlays that chose it keep its look and name, and show as custom.</summary>
    public Refusal? Delete(string name)
    {
        if (Find(name) is not var (file, _))
        {
            return Refusal.StyleNotFound;
        }

        File.Delete(file);
        return null;
    }

    /// <summary>
    /// The saved style the window shows as chosen: the one the style names, when it also has its look, or null for custom.
    /// A style imported under a saved one's name, but of another look, is not that style.
    /// </summary>
    public static StyleConfig? Chosen(StyleConfig style, IEnumerable<StyleConfig> saved) =>
        style.Name is null ? null : saved.FirstOrDefault(each => Same(each.Name, style.Name) && each.WornOn(style) with { Name = null } == style with { Name = null });

    /// <summary>
    /// The file name a style's name makes before a number is added for a taken one: the characters Windows refuses and
    /// trailing dots and spaces replaced, and a device name like CON kept from meaning the device.
    /// </summary>
    public static string FileNameOf(string name)
    {
        var safe = string.Concat(name.Select(letter => letter < ' ' || Refused.Contains(letter, StringComparison.Ordinal) ? '_' : letter));
        var trimmed = safe.TrimEnd('.', ' ');
        safe = trimmed + new string('_', safe.Length - trimmed.Length);
        return (IsDevice(safe) ? "_" + safe : safe) + StyleFile.Extension;
    }

    internal static bool Same(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    /// <summary>The settings with every overlay that chose <paramref name="from"/> naming <paramref name="to"/> instead.</summary>
    internal static ThermiConfig Renamed(ThermiConfig config, string from, string to) => config with
    {
        Overlays = [.. config.Overlays.Select(overlay => overlay with { Style = Renamed(overlay.Style, from, to) })],
    };

    internal static StyleConfig Renamed(StyleConfig style, string from, string to) => Same(style.Name, from) ? style with { Name = to } : style;

    private static Refusal? NameRefusal(string name) =>
        name.Length == 0 ? Refusal.StyleNameEmpty : name.Length > StyleConfig.MaxNameLength ? Refusal.StyleNameTooLong : null;

    // Windows takes "CON", "COM1" or "nul.txt" for a device, whatever the case and the extension.
    private static bool IsDevice(string fileName)
    {
        var stem = fileName.Split('.')[0].TrimEnd();
        return Devices.Contains(stem, StringComparer.OrdinalIgnoreCase)
            || (stem.Length == 4 && Ports.Contains(stem[..3], StringComparer.OrdinalIgnoreCase) && PortNumbers.Contains(stem[3], StringComparison.Ordinal));
    }

    private (string File, StyleConfig Style)? Find(string name) =>
        Files().Where(saved => Same(saved.Style.Name, name)).Select(saved => ((string File, StyleConfig Style)?)saved).FirstOrDefault();

    private IEnumerable<(string File, StyleConfig Style)> Files()
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(folder, "*" + StyleFile.Extension).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (Text(file) is { } text && StyleFile.Read(text).Style is { Name: { } name } style && seen.Add(name))
            {
                yield return (file, style);
            }
        }
    }

    // A file being written or locked by another program is left out until the next look.
    private static string? Text(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string FreePath(string name)
    {
        var fileName = FileNameOf(name);
        var stem = fileName[..^StyleFile.Extension.Length];
        var path = Path.Combine(folder, fileName);
        var number = 1;
        while (File.Exists(path))
        {
            number++;
            path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{stem} {number}{StyleFile.Extension}"));
        }

        return path;
    }
}

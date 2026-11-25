using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Windows.Ui.Editor;
using Thermi.Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Windows.Ui.Views;

/// <summary>
/// The saved looks and the look files: saving, renaming and deleting change the files in the styles folder and
/// never the overlay, so undo does not touch them; wearing a look and importing one are each one step undo takes back. An
/// export or an import by file goes through Windows' own dialogs over the window. A file Windows refused to read or
/// write changes nothing, and says why. Used on the window's thread alone.
/// </summary>
/// <param name="editor">Where a look worn or imported goes, and whose undo steps a rename follows.</param>
/// <param name="saved">The styles folder's looks.</param>
/// <param name="changed">Tells the page that the saved looks changed.</param>
internal sealed class WindowLooks(WindowEditor editor, SavedStyles saved, Action changed)
{
    private bool? _anySaved;

    /// <summary>The window the dialogs show over.</summary>
    public HWND Owner { get; set; }

    /// <summary>Whether any look is saved, read from the folder again only once this window changed its files.</summary>
    public bool AnySaved => _anySaved ??= Looks().Count > 0;

    public IReadOnlyDictionary<string, StyleConfig> Looks() =>
        saved.List().Where(style => style.Name is not null).ToDictionary(style => style.Name!, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WornLook?> Worn()
    {
        var looks = saved.List();
        return [.. editor.Config.Overlays.Select(overlay => SavedStyles.Worn(overlay.Style, looks))];
    }

    public string? Save(int overlay, string name) => Files(() => saved.Save(StyleOf(overlay), name));

    public string? Rename(string name, string to) => Files(() => editor.RenameStyle(saved, name, to).Refused);

    public string? Delete(string name) => Files(() => saved.Delete(name));

    /// <summary>
    /// A saved look, or the Default look without a name, as the overlay's menu wears it, worn by one overlay, or by every
    /// overlay when none is named, as one step.
    /// </summary>
    public string? Wear(LookEdit look)
    {
        var style = look.Name is null
            ? new StyleConfig()
            : saved.Named(look.Name);
        if (style is null)
        {
            return Texts.Key(Refusal.StyleNotFound);
        }

        var result = editor.Edit("look", config => StyleEdits.Wear(config, look.Overlay, style));
        return result.Refused is { } refused ? Texts.Key(refused) : null;
    }

    /// <summary>
    /// Writes the overlay's look as a look file, named as the saved look it wears, else as the file, so an import knows
    /// what to call it.
    /// </summary>
    public string? Export(int overlay, Texts texts)
    {
        var style = StyleOf(overlay);
        return Files(() =>
        {
            if (FileDialogs.Save(Owner, texts.Of(UiText.StyleFiles), StyleFile.Extension, style.Name ?? "thermi") is not { } path)
            {
                return null;
            }

            var name = style.Name ?? Path.GetFileNameWithoutExtension(path).Trim();
            File.WriteAllText(path, StyleFile.Write(style with { Name = name.Length is > 0 and <= StyleConfig.MaxNameLength ? name : null }));
            return null;
        });
    }

    public string? ExportSaved(string name, Texts texts) => Files(() =>
    {
        if (saved.Named(name) is not { } style)
        {
            return Refusal.StyleNotFound;
        }

        if (FileDialogs.Save(Owner, texts.Of(UiText.StyleFiles), StyleFile.Extension, style.Name!) is { } path)
        {
            File.WriteAllText(path, StyleFile.Write(style));
        }

        return null;
    });

    /// <summary>A look file's text, dropped or pasted, or the file picked, worn by the overlay as one step.</summary>
    public string? Import(int overlay, string? text, Texts texts)
    {
        var read = text;
        if (read is null)
        {
            if (FileDialogs.Open(Owner, texts.Of(UiText.StyleFiles), StyleFile.Extension) is not { } path)
            {
                return null;
            }

            // A large file picked by mistake is never read whole.
            var failed = Files(() =>
            {
                var tooLarge = new FileInfo(path).Length > StyleFile.MaxLength;
                read = tooLarge ? null : File.ReadAllText(path);
                return tooLarge ? Refusal.StyleTooLarge : null;
            });
            if (failed is not null || read is null)
            {
                return failed;
            }
        }

        var result = editor.Edit("import look", config => StyleEdits.Import(config, overlay, read));
        return result.Refused is { } refusal ? Texts.Key(refusal) : null;
    }

    private StyleConfig StyleOf(int overlay) =>
        overlay >= 0 && overlay < editor.Config.Overlays.Count
            ? editor.Config.Overlays[overlay].Style
            : throw new ArgumentOutOfRangeException(nameof(overlay), overlay, "No such overlay.");

    // A file Windows refused to write, read or delete changes nothing, and says why; the page reads the looks again.
    private string? Files(Func<Refusal?> change)
    {
        Refusal? refused;
        try
        {
            refused = change();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            refused = Refusal.StyleFileFailed;
        }

        _anySaved = null;
        changed();
        return refused is { } why ? Texts.Key(why) : null;
    }
}

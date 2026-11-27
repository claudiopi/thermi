using System.Reflection;
using System.Text;
using Thermi.Core.Bridge;
using Thermi.Core.Translations;

namespace Thermi.Settings.Types;

/// <summary>
/// The key of every text the page may show, <c>Enum.Member</c> of Core's <see cref="TranslatedAttribute"/> enums, as one
/// TypeScript type. The texts themselves come through the bridge in the window's language, so a translation changes
/// without building the page again.
/// </summary>
internal static class TextKeys
{
    public static string Write()
    {
        var enums = typeof(Texts).Assembly.GetTypes()
            .Where(type => type.IsEnum && type.GetCustomAttribute<TranslatedAttribute>() is not null)
            .OrderBy(type => type.Name, StringComparer.Ordinal);
        var text = new StringBuilder(TypeScriptWriter.Header).Append("export type TextKey =\n");
        foreach (var type in enums)
        {
            text.Append("  | ").AppendJoin(" | ", Enum.GetNames(type).Select(name => $"'{type.Name}.{name}'")).Append('\n');
        }

        text.Append(";\n");
        // Each OS's own words, by the OS as the bridge names it, which the page reads every text through.
        text.Append("\nexport const ownTexts: Readonly<Record<string, Readonly<Partial<Record<TextKey, TextKey>>>>> = {\n");
        foreach (var (os, own) in HostDesktop.OwnTexts.OrderBy(each => each.Key))
        {
            text.Append("  ").Append(BridgeCalls.NameOf(os.ToString())).Append(": {\n");
            foreach (var (windows, words) in own.OrderBy(each => each.Key, StringComparer.Ordinal))
            {
                text.Append("    '").Append(windows).Append("': '").Append(words).Append("',\n");
            }

            text.Append("  },\n");
        }

        return text.Append("};\n").ToString().Replace("\n;\n", ";\n", StringComparison.Ordinal);
    }
}

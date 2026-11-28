namespace Thermi.Linux.Gnome;

internal static class ShellWire
{
    public static GLib.Variant RowsOf(IReadOnlyList<ShellRow> rows) =>
        GLib.Variant.NewArray(GLib.VariantType.New("(issbbbbb)"), [.. rows.Select(row => GLib.Variant.NewTuple(
        [
            GLib.Variant.NewInt32(row.Parent),
            GLib.Variant.NewString(row.Text),
            GLib.Variant.NewString(row.Note ?? ""),
            GLib.Variant.NewBoolean(row.Checked),
            GLib.Variant.NewBoolean(row.Grayed),
            GLib.Variant.NewBoolean(row.Starts),
            GLib.Variant.NewBoolean(row.Default),
            GLib.Variant.NewBoolean(row.Radio),
        ]))]);

    /// <summary>
    /// The monitors in the extension's <c>a(ddddddbbdddddd)</c>, as its <c>Monitors</c> answers and its signal sends them;
    /// one in an earlier protocol's twelve parts tells no box of Activities, read as none.
    /// </summary>
    public static List<ShellMonitor> MonitorsOf(GLib.Variant monitors) =>
        [.. Enumerable.Range(0, (int)monitors.NChildren()).Select(at => monitors.GetChildValue((nuint)at)).Select(each => new ShellMonitor(
            each.GetChildValue(0).GetDouble(),
            each.GetChildValue(1).GetDouble(),
            each.GetChildValue(2).GetDouble(),
            each.GetChildValue(3).GetDouble(),
            each.GetChildValue(4).GetDouble(),
            each.GetChildValue(5).GetDouble(),
            each.GetChildValue(6).GetBoolean(),
            each.GetChildValue(7).GetBoolean())
        {
            Centre = (each.GetChildValue(8).GetDouble(), each.GetChildValue(9).GetDouble()),
            End = (each.GetChildValue(10).GetDouble(), each.GetChildValue(11).GetDouble()),
            Start = each.NChildren() > ShellFace.FewestMonitorParts ? (each.GetChildValue(12).GetDouble(), each.GetChildValue(13).GetDouble()) : (0, 0),
        })];
}

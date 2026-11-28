using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Thermi.Linux.Desktop;

/// <summary>
/// GTK's objects die on the main loop, the one thread GTK runs on. GirCore gives a wrapper back there itself, but keeps a
/// copy of each signal's arguments, the object that sent it among them, that .NET's finalizer thread lets go of later: an
/// object thermi let go of before that copy is freed there, with what GTK shares with the main loop's own objects, its
/// styles among them, while the main loop uses them, and thermi crashes later on a double free. What is let go of here
/// is held until a collection on the main loop had every such copy let go, and only then freed, here; an object watched
/// that dies on another thread is logged.
/// </summary>
internal static partial class MainLoopRelease
{
    private static readonly List<nint> Held = [];

    // The watch of each object until it dies, which GLib calls with the object's pointer.
    private static readonly ConcurrentDictionary<nint, (ILogger Log, string Type, int Thread)> Watched = new();

    // Kept for as long as thermi runs: GLib calls it from whichever thread an object dies on.
    private static readonly GObject.Internal.WeakNotify Died = (_, where) => OnDied(where);

    /// <summary>Logs it to <paramref name="log"/> if one of <paramref name="watched"/> dies on another thread than this, the main loop's.</summary>
    public static void Watch(ILogger log, params IEnumerable<GObject.Object> watched)
    {
        foreach (var each in watched)
        {
#pragma warning disable S3869 // GirCore has no call that watches an object but on its pointer.
            var handle = each.Handle.DangerousGetHandle();
#pragma warning restore S3869
            if (Watched.TryAdd(handle, (log, each.GetType().Name, Environment.CurrentManagedThreadId)))
            {
                GObject.Internal.Object.WeakRef(handle, Died, 0);
            }
        }
    }

    /// <summary>
    /// Holds <paramref name="objects"/>, watched, which their owner lets go of now, until nothing of .NET's holds them, and
    /// frees them then on the main loop.
    /// </summary>
    public static void Hold(ILogger log, params IEnumerable<GObject.Object> objects)
    {
        var waiting = Held.Count > 0;
        foreach (var each in objects)
        {
            Watch(log, each);
#pragma warning disable S3869 // GirCore has no call that refs an object but on its pointer.
            Held.Add(GObject.Internal.Object.Ref(each.Handle.DangerousGetHandle()));
#pragma warning restore S3869
        }

        if (!waiting && Held.Count > 0)
        {
            _ = GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_LOW, Free);
        }
    }

    private static bool Free()
    {
        // The copies are garbage once their signals returned: one collection finds them all, and the main loop waits while
        // the finalizer lets them go, so nothing of GTK's runs on two threads at once.
#pragma warning disable S1215 // The one way to have .NET let go of GirCore's copies before thermi frees what they hold.
        GC.Collect();
#pragma warning restore S1215
        GC.WaitForPendingFinalizers();
        foreach (var each in Held)
        {
            GObject.Internal.Object.Unref(each);
        }

        Held.Clear();
        return false;
    }

    private static void OnDied(nint where)
    {
        if (Watched.TryRemove(where, out var watch) && watch.Thread != Environment.CurrentManagedThreadId)
        {
            DiedOffLoop(watch.Log, watch.Type, Environment.CurrentManagedThreadId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "GTK freed a {Type} on thread {Thread}, off its main loop: thermi may crash")]
    private static partial void DiedOffLoop(ILogger logger, string type, int thread);
}

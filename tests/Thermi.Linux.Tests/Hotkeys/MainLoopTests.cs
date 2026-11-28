using Thermi.Linux.Desktop;

namespace Thermi.Linux.Tests.Hotkeys;

/// <summary>
/// What a portal answers on another thread goes on on the GLib main loop's, where thermi lets its requests go
/// (<c>MainLoopRelease</c>), even where the main loop got there before the thread that awaits it did.
/// </summary>
public sealed class MainLoopTests
{
    [Fact(Timeout = Bound.Test)]
    public async Task WhatFollowsAnAnswerGoesOnOnTheMainLoop()
    {
        Gio.Module.Initialize();
        using var loop = GLib.MainLoop.New(null, false);
        var looping = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            looping.SetResult(Environment.CurrentManagedThreadId);
            loop.Run();
        })
        {
            IsBackground = true,
        };
        thread.Start();
        var on = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var main = await looping.Task;
            _ = Task.Run(async () =>
            {
                var back = Callbacks.OnMainLoop();
                // The main loop runs all it was given before the await, as it may while the thread that awaits is busy.
                await IdleAsync();
                await back;
                on.SetResult(Environment.CurrentManagedThreadId);
            }, TestContext.Current.CancellationToken);

            Assert.Equal(main, await on.Task.WaitAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            loop.Quit();
            thread.Join();
        }
    }

    private static Task IdleAsync()
    {
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GLib.Functions.IdleAdd(0, () =>
        {
            ran.SetResult();
            return false;
        });
        return ran.Task;
    }
}

using System.Collections.Concurrent;

namespace Haukcode.MidiDevice.Internal.macOS;

/// <summary>
/// The one thread per process that owns CoreMIDI's run loop.
///
/// A process only sees MIDI devices come and go while CoreMIDI can deliver
/// its notifications, and it delivers them on the run loop of the thread that
/// created the MIDI client. On a thread-pool thread (no run loop) nothing is
/// ever delivered: the process's device list stays frozen at its first
/// snapshot, a device plugged in later never appears, and no client's
/// notifyProc ever fires. So every MIDIClientCreate goes through
/// <see cref="Invoke{T}"/>, which runs it on this thread, and the thread keeps
/// its run loop running for the life of the process.
/// </summary>
internal static class CoreMidiRunLoop
{
    // Run loop slice: how long queued work can wait before it runs.
    private const double SliceSeconds = 0.05;

    private static readonly object Gate = new();
    private static readonly BlockingCollection<Action> Work = [];
    private static Thread? thread;

    /// <summary>Run <paramref name="work"/> on the CoreMIDI run-loop thread and return its result.</summary>
    public static T Invoke<T>(Func<T> work)
    {
        EnsureStarted();

        if (Thread.CurrentThread == thread)
            return work();

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Work.Add(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });

        return done.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Start the run-loop thread. Call before touching CoreMIDI at all, so the
    /// process's device list is kept current from the start.
    /// </summary>
    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (thread != null)
                return;

            var defaultMode = ReadDefaultRunLoopMode();
            thread = new Thread(() => Run(defaultMode))
            {
                IsBackground = true,
                Name = "CoreMIDI run loop",
            };
            thread.Start();
        }
    }

    private static void Run(nint defaultMode)
    {
        while (true)
        {
            // Deliver CoreMIDI notifications for up to one slice. With no
            // client yet the run loop has no sources and returns at once, so
            // wait on the queue for the slice instead of spinning.
            int result = CoreMidiNative.CFRunLoopRunInMode(defaultMode, SliceSeconds, false);
            int waitMs = result == CoreMidiNative.kCFRunLoopRunFinished ? (int)(SliceSeconds * 1000) : 0;

            while (Work.TryTake(out var item, waitMs))
            {
                try { item(); } catch { }
                waitMs = 0;
            }
        }
    }

    // kCFRunLoopDefaultMode is an exported CFStringRef variable, not a function.
    private static nint ReadDefaultRunLoopMode()
    {
        var lib = NativeLibrary.Load(CoreMidiNative.CoreFoundation);

        return Marshal.ReadIntPtr(NativeLibrary.GetExport(lib, "kCFRunLoopDefaultMode"));
    }
}

using Haukcode.MidiDevice.Internal;
using Haukcode.MidiDevice.Internal.macOS;

namespace Haukcode.MidiDevice;

/// <summary>
/// Watches for MIDI device additions and removals and raises
/// <see cref="DeviceListChanged"/> after a short settle delay.
///
/// On macOS, CoreMIDI's native notification callback is used so detection is
/// near-instant.  On Windows and Linux, the device list is polled on a
/// configurable interval (default 2 s).  On all platforms, detection is
/// followed by <see cref="SettleDelay"/> (default 1 s) to give the OS time
/// to finish registering the new device before callers re-enumerate.
/// </summary>
public sealed class MidiDeviceWatcher : IDisposable
{
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _cts = new();
    private readonly IPlatformDeviceNotifier? _platformNotifier;
    private Task? _loopTask;
    private IReadOnlyList<string>? _lastInputNames;
    private IReadOnlyList<string>? _lastOutputNames;
    // Set when a platform notification fires so the poll loop triggers a
    // settle+notify on its next tick without waiting a full poll interval.
    private volatile bool _notificationPending;

    /// <summary>
    /// How long to wait after detecting a change before raising
    /// <see cref="DeviceListChanged"/>. Gives the OS time to finish registering
    /// the device so the next enumeration returns a complete list.
    /// Defaults to 1 second.
    /// </summary>
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Raised on a thread-pool thread after a change is detected and the settle delay has elapsed.</summary>
    public event EventHandler? DeviceListChanged;

    /// <param name="pollInterval">
    /// How often to poll the device list (Windows / Linux fallback).
    /// On macOS, CoreMIDI push notifications are used instead; polling still
    /// runs as a safety net but is inexpensive because the list rarely changes
    /// between notification and poll tick.
    /// Defaults to 2 seconds.
    /// </param>
    public MidiDeviceWatcher(TimeSpan? pollInterval = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            _platformNotifier = new CoreMidiDeviceNotifier();
    }

    /// <summary>Start watching. Safe to call only once.</summary>
    public void Start()
    {
        // Snapshot the current state so we have a baseline for change detection.
        _lastInputNames  = GetInputNames();
        _lastOutputNames = GetOutputNames();

        if (_platformNotifier != null)
        {
            _platformNotifier.Changed += OnPlatformNotification;
            _platformNotifier.Start();
        }

        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    private void OnPlatformNotification()
    {
        // Mark a pending notification so the poll loop wakes up on its next
        // tick and immediately starts the settle delay. This avoids the full
        // poll interval delay on macOS.
        _notificationPending = true;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            bool changed = _notificationPending;
            _notificationPending = false;

            if (!changed)
            {
                var currentInputs  = GetInputNames();
                var currentOutputs = GetOutputNames();

                changed = !NamesEqual(currentInputs,  _lastInputNames!)
                       || !NamesEqual(currentOutputs, _lastOutputNames!);

                _lastInputNames  = currentInputs;
                _lastOutputNames = currentOutputs;
            }

            if (!changed) continue;

            // Settle delay: wait for the OS to finish registering the device.
            try
            {
                await Task.Delay(SettleDelay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            DeviceListChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Stop watching and release resources.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _platformNotifier?.Dispose();
        // _loopTask is fire-and-forget; we don't await it to avoid deadlocks on Dispose.
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyList<string> GetInputNames() =>
        MidiDeviceManager.GetInputDevices().Select(d => d.Name).ToList();

    private static IReadOnlyList<string> GetOutputNames() =>
        MidiDeviceManager.GetOutputDevices().Select(d => d.Name).ToList();

    private static bool NamesEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count) return false;
        // Order-sensitive — WinMM preserves insertion order and
        // index-based identity, so a reordering is itself a noteworthy change.
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }
}

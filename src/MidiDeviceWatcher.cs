namespace Haukcode.MidiDevice;

/// <summary>
/// Watches for MIDI device additions and removals by periodically polling
/// <see cref="MidiDeviceManager.GetInputDevices"/> and
/// <see cref="MidiDeviceManager.GetOutputDevices"/>.
///
/// When a change is detected the watcher waits for <see cref="SettleDelay"/>
/// before raising <see cref="DeviceListChanged"/>, giving the OS time to finish
/// registering the new device so the first post-change enumeration returns a
/// complete, stable list.
///
/// Default timing: poll every 2 s, settle for 1 s.
/// </summary>
public sealed class MidiDeviceWatcher : IDisposable
{
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private IReadOnlyList<string>? _lastInputNames;
    private IReadOnlyList<string>? _lastOutputNames;

    /// <summary>
    /// How long to wait after detecting a change before raising
    /// <see cref="DeviceListChanged"/>. Gives the OS time to finish registering
    /// the device so the next enumeration returns a complete list.
    /// Defaults to 1 second.
    /// </summary>
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Raised on a thread-pool thread after a change is detected and the settle delay has elapsed.</summary>
    public event EventHandler? DeviceListChanged;

    /// <param name="pollInterval">How often to check the device list. Defaults to 2 seconds.</param>
    public MidiDeviceWatcher(TimeSpan? pollInterval = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Start watching. Safe to call only once.</summary>
    public void Start()
    {
        // Snapshot the current state so we have a baseline for change detection.
        _lastInputNames  = GetInputNames();
        _lastOutputNames = GetOutputNames();

        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
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

            var currentInputs  = GetInputNames();
            var currentOutputs = GetOutputNames();

            bool changed = !NamesEqual(currentInputs,  _lastInputNames!)
                        || !NamesEqual(currentOutputs, _lastOutputNames!);

            _lastInputNames  = currentInputs;
            _lastOutputNames = currentOutputs;

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
        // Order-sensitive comparison — WinMM preserves insertion order and
        // index-based identity, so a reordering is itself a noteworthy change.
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }
}

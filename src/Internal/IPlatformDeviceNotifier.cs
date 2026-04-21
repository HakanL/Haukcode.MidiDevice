namespace Haukcode.MidiDevice.Internal;

/// <summary>
/// Platform-specific hook that raises <see cref="Changed"/> when the OS
/// signals a MIDI device topology change.  Implementations exist only for
/// platforms that provide a native push-notification API (macOS CoreMIDI).
/// On other platforms <see cref="MidiDeviceWatcher"/> falls back to polling.
/// </summary>
internal interface IPlatformDeviceNotifier : IDisposable
{
    /// <summary>
    /// Raised on an OS-managed thread when the device list may have changed.
    /// Callers must not block this handler.
    /// </summary>
    event Action? Changed;

    void Start();
}

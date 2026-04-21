namespace Haukcode.MidiDevice.Internal.macOS;

/// <summary>
/// macOS CoreMIDI implementation of <see cref="IPlatformDeviceNotifier"/>.
/// Creates a lightweight MIDIClient whose notifyProc is called by CoreMIDI on
/// its own internal thread whenever the device topology changes
/// (kMIDIMsgSetupChanged, messageID == 1).  No CFRunLoop is needed — CoreMIDI
/// delivers the notification directly to the callback.
/// </summary>
internal sealed class CoreMidiDeviceNotifier : IPlatformDeviceNotifier
{
    private nint _client;
    // Stored as a field to prevent GC collection while the client is alive.
    private CoreMidiNative.MIDINotifyProc? _notifyProc;

    public event Action? Changed;

    public void Start()
    {
        _notifyProc = OnNotify;

        var clientName = CoreMidiNative.CFStringCreateWithCString(
            nint.Zero, "HaukcodeDeviceWatcher", CoreMidiNative.kCFStringEncodingUTF8);
        try
        {
            // Ignore the return code — if it fails (e.g. sandboxed app without
            // the com.apple.security.device.audio-input entitlement), the watcher
            // just won't fire and the caller falls back to polling gracefully.
            CoreMidiNative.MIDIClientCreate(clientName, _notifyProc, nint.Zero, out _client);
        }
        finally
        {
            CoreMidiNative.CFRelease(clientName);
        }
    }

    private void OnNotify(nint message)
    {
        const int kMIDIMsgSetupChanged = 1;

        // message → MIDINotification { SInt32 messageID; UInt32 messageSize; }
        var messageId = Marshal.ReadInt32(message);
        if (messageId == kMIDIMsgSetupChanged)
            Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_client == nint.Zero) return;
        CoreMidiNative.MIDIClientDispose(_client);
        _client = nint.Zero;
    }
}

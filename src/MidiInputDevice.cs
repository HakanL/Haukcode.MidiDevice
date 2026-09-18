using Haukcode.MidiDevice.Internal;

namespace Haukcode.MidiDevice;

/// <summary>
/// An open MIDI input device. Exposes received messages as a hot <see cref="IObservable{T}"/> stream.
/// Messages are emitted on the backend receive thread — keep handlers non-blocking.
/// </summary>
public sealed class MidiInputDevice : IDisposable
{
    private readonly IMidiInputBackend _backend;
    private readonly Subject<MidiMessage> _subject = new();
    private readonly MidiStreamParser _parser = new();
    private readonly AsyncSubject<Unit> _disconnected = new();
    private volatile bool _disposed;

    private MidiInputDevice(IMidiInputBackend backend)
    {
        _backend = backend;
        _backend.Disconnect.Raised += OnDisconnected;
        _backend.StartReceiving(OnRawBytes);
    }

    /// <summary>Opens the device described by <paramref name="info"/> and begins receiving.</summary>
    public static MidiInputDevice Open(MidiInputDeviceInfo info)
        => new(MidiDeviceManager.CreateInputBackend(info));

    /// <summary>Human-readable device name.</summary>
    public string Name => _backend.Name;

    /// <summary>
    /// Hot observable of parsed MIDI messages. Emits on the backend receive thread.
    /// Completes when the device is disposed.
    /// </summary>
    public IObservable<MidiMessage> Messages => _subject.AsObservable();

    /// <summary>
    /// True once the OS has reported the device gone (unplugged). A disconnected
    /// device never recovers: dispose it and open the device again once it is
    /// enumerated. Disposing does not set this.
    /// </summary>
    public bool IsDisconnected => _backend.Disconnect.IsRaised;

    /// <summary>
    /// Emits once, then completes, when the OS reports the device gone. A late
    /// subscriber still receives it. Completes without a value when the device
    /// is disposed first. May emit on an OS callback thread — keep handlers
    /// non-blocking.
    /// </summary>
    public IObservable<Unit> Disconnected => _disconnected.AsObservable();

    private void OnDisconnected()
    {
        if (_disposed) return;
        _disconnected.OnNext(Unit.Default);
        _disconnected.OnCompleted();
    }

    private void OnRawBytes(ReadOnlyMemory<byte> bytes)
    {
        _parser.Process(bytes.Span, msg => _subject.OnNext(msg));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Disconnect.Raised -= OnDisconnected;
        _backend.StopReceiving();
        _backend.Dispose();
        _subject.OnCompleted();
        _subject.Dispose();
        _disconnected.OnCompleted();
    }
}

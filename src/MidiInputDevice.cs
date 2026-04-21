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
    private bool _disposed;

    private MidiInputDevice(IMidiInputBackend backend)
    {
        _backend = backend;
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

    private void OnRawBytes(ReadOnlyMemory<byte> bytes)
    {
        _parser.Process(bytes.Span, msg => _subject.OnNext(msg));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.StopReceiving();
        _backend.Dispose();
        _subject.OnCompleted();
        _subject.Dispose();
    }
}

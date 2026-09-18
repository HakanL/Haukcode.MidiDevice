using Haukcode.MidiDevice.Internal;

namespace Haukcode.MidiDevice;

/// <summary>
/// An open MIDI output device. Sends typed messages or raw bytes to the device.
/// </summary>
public sealed class MidiOutputDevice : IDisposable
{
    private readonly IMidiOutputBackend _backend;
    private readonly AsyncSubject<Unit> _disconnected = new();
    private volatile bool _disposed;

    private MidiOutputDevice(IMidiOutputBackend backend)
    {
        _backend = backend;
        _backend.Disconnect.Raised += OnDisconnected;

        // The backend starts watching when it opens the device, before we
        // subscribed above, so a removal in that window is only on the flag.
        if (_backend.Disconnect.IsRaised)
            OnDisconnected();
    }

    /// <summary>Opens the device described by <paramref name="info"/>.</summary>
    public static MidiOutputDevice Open(MidiOutputDeviceInfo info)
        => new(MidiDeviceManager.CreateOutputBackend(info));

    /// <summary>Human-readable device name.</summary>
    public string Name => _backend.Name;

    /// <summary>
    /// True once the OS has reported the device gone (unplugged), or a send
    /// failed because the device is no longer there. A disconnected device
    /// never recovers: dispose it and open the device again once it is
    /// enumerated. Disposing does not set this.
    /// </summary>
    public bool IsDisconnected => _backend.Disconnect.IsRaised;

    /// <summary>
    /// Emits once, then completes, when the device is reported gone. A late
    /// subscriber still receives it. Completes without a value when the device
    /// is disposed first. May emit on an OS callback thread or on the thread
    /// of the send that failed — keep handlers non-blocking.
    /// </summary>
    public IObservable<Unit> Disconnected => _disconnected.AsObservable();

    private void OnDisconnected()
    {
        if (_disposed) return;
        _disconnected.OnNext(Unit.Default);
        _disconnected.OnCompleted();
    }

    /// <summary>Sends a typed MIDI message.</summary>
    public void Send(MidiMessage message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Span<byte> buf = stackalloc byte[3];
        var len = MidiEncoder.Encode(message, buf);
        if (len > 0)
            _backend.Send(buf[..len]);
    }

    /// <summary>Sends raw bytes — use for SysEx or vendor-specific messages.</summary>
    public void SendRaw(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _backend.Send(data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Disconnect.Raised -= OnDisconnected;
        _backend.Dispose();
        _disconnected.OnCompleted();
    }
}

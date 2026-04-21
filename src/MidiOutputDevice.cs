using Haukcode.MidiDevice.Internal;

namespace Haukcode.MidiDevice;

/// <summary>
/// An open MIDI output device. Sends typed messages or raw bytes to the device.
/// </summary>
public sealed class MidiOutputDevice : IDisposable
{
    private readonly IMidiOutputBackend _backend;
    private bool _disposed;

    private MidiOutputDevice(IMidiOutputBackend backend)
    {
        _backend = backend;
    }

    /// <summary>Opens the device described by <paramref name="info"/>.</summary>
    public static MidiOutputDevice Open(MidiOutputDeviceInfo info)
        => new(MidiDeviceManager.CreateOutputBackend(info));

    /// <summary>Human-readable device name.</summary>
    public string Name => _backend.Name;

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
        _backend.Dispose();
    }
}

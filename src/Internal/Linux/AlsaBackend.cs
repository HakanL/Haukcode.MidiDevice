namespace Haukcode.MidiDevice.Internal.Linux;

/// <summary>
/// Linux ALSA rawmidi backend (libasound.so.2).
/// Devices are enumerated via snd_device_name_hint with the "rawmidi" interface.
/// Input uses a blocking snd_rawmidi_read loop on a background thread;
/// closing the handle unblocks the read and exits the thread cleanly.
/// </summary>
internal static class AlsaBackend
{
    public static IReadOnlyList<MidiInputDeviceInfo> GetInputDevices() =>
        EnumerateDevices(wantInput: true)
            .Select(d => new MidiInputDeviceInfo(d.Id, d.Name))
            .ToList();

    public static IReadOnlyList<MidiOutputDeviceInfo> GetOutputDevices() =>
        EnumerateDevices(wantInput: false)
            .Select(d => new MidiOutputDeviceInfo(d.Id, d.Name))
            .ToList();

    private readonly record struct RawDevice(string Id, string Name);

    private static IEnumerable<RawDevice> EnumerateDevices(bool wantInput)
    {
        if (AlsaNative.snd_device_name_hint(-1, "rawmidi", out nint hintsPtr) < 0)
            yield break;

        try
        {
            var ptr = hintsPtr;
            while (true)
            {
                var hint = Marshal.ReadIntPtr(ptr);
                if (hint == nint.Zero) break;
                ptr += nint.Size;

                var namePtr = AlsaNative.snd_device_name_get_hint(hint, "NAME");
                var descPtr = AlsaNative.snd_device_name_get_hint(hint, "DESC");
                var ioidPtr = AlsaNative.snd_device_name_get_hint(hint, "IOID");

                var id   = Marshal.PtrToStringAnsi(namePtr) ?? "";
                var desc = Marshal.PtrToStringAnsi(descPtr) ?? id;
                var ioid = Marshal.PtrToStringAnsi(ioidPtr); // null = both directions

                if (namePtr != nint.Zero) AlsaNative.free(namePtr);
                if (descPtr != nint.Zero) AlsaNative.free(descPtr);
                if (ioidPtr != nint.Zero) AlsaNative.free(ioidPtr);

                // IOID: "Input" = input-only, "Output" = output-only, null = both
                bool hasInput  = ioid == null || ioid == "Input";
                bool hasOutput = ioid == null || ioid == "Output";

                if (string.IsNullOrEmpty(id)) continue;
                if (wantInput && !hasInput)   continue;
                if (!wantInput && !hasOutput) continue;

                yield return new RawDevice(id, FirstLine(desc));
            }
        }
        finally
        {
            AlsaNative.snd_device_name_free_hint(hintsPtr);
        }
    }

    /// <summary>ALSA descriptions use newlines as separators; take the card-name line.</summary>
    private static string FirstLine(string s)
    {
        var nl = s.IndexOf('\n');
        return (nl >= 0 ? s[..nl] : s).Trim();
    }
}

// ---------------------------------------------------------------------------

internal sealed class AlsaInputBackend : IMidiInputBackend
{
    private const int ReadBufferSize = 512;
    private const int ErrnoEINTR    = -4; // interrupted by signal — retry

    private readonly MidiInputDeviceInfo _info;
    private nint _handle;
    private Action<ReadOnlyMemory<byte>>? _onData;
    private Thread? _readThread;
    private volatile bool _stopping;
    private readonly byte[] _readBuffer = new byte[ReadBufferSize];

    public AlsaInputBackend(MidiInputDeviceInfo info) => _info = info;

    public string Id   => _info.Id;
    public string Name => _info.Name;

    public void StartReceiving(Action<ReadOnlyMemory<byte>> onData)
    {
        _onData   = onData;
        _stopping = false;

        var rc = AlsaNative.snd_rawmidi_open_input(out _handle, nint.Zero, _info.Id, 0);
        if (rc < 0)
            throw new IOException($"Cannot open MIDI input '{_info.Name}' ({_info.Id}): {AlsaNative.GetError(rc)}");

        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = $"alsa-midi-in:{_info.Name}",
        };
        _readThread.Start();
    }

    private void ReadLoop()
    {
        while (!_stopping)
        {
            var n = AlsaNative.snd_rawmidi_read(_handle, _readBuffer, ReadBufferSize);

            if (n <= 0)
            {
                if (_stopping)           break; // expected: handle was closed by StopReceiving
                if (n == ErrnoEINTR)     continue; // signal interrupted the syscall, retry
                break; // real error (e.g. device removed — -ENODEV)
            }

            // Memory is only valid for the duration of this synchronous invocation.
            // MidiInputDevice.OnRawBytes processes it synchronously via MidiStreamParser
            // before we loop back to the next read, so reusing the buffer is safe.
            _onData?.Invoke(_readBuffer.AsMemory(0, (int)n));
        }
    }

    public void StopReceiving()
    {
        _stopping = true;

        // Closing the handle while snd_rawmidi_read is blocking will unblock it
        // and cause it to return a negative error code, which exits the loop above.
        if (_handle != nint.Zero)
        {
            AlsaNative.snd_rawmidi_close(_handle);
            _handle = nint.Zero;
        }

        _readThread?.Join(TimeSpan.FromSeconds(2));
        _readThread = null;
    }

    public void Dispose() => StopReceiving();
}

// ---------------------------------------------------------------------------

internal sealed class AlsaOutputBackend : IMidiOutputBackend
{
    private readonly MidiOutputDeviceInfo _info;
    private nint _handle;
    private bool _disposed;

    public AlsaOutputBackend(MidiOutputDeviceInfo info)
    {
        _info = info;
        var rc = AlsaNative.snd_rawmidi_open_output(nint.Zero, out _handle, info.Id, 0);
        if (rc < 0)
            throw new IOException($"Cannot open MIDI output '{info.Name}' ({info.Id}): {AlsaNative.GetError(rc)}");
    }

    public string Id   => _info.Id;
    public string Name => _info.Name;

    public void Send(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = data.ToArray(); // ToArray needed for P/Invoke byte[] parameter
        AlsaNative.snd_rawmidi_write(_handle, bytes, bytes.Length);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle == nint.Zero) return;
        AlsaNative.snd_rawmidi_drain(_handle);
        AlsaNative.snd_rawmidi_close(_handle);
        _handle = nint.Zero;
    }
}

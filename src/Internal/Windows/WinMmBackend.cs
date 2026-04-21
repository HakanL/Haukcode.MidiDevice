namespace Haukcode.MidiDevice.Internal.Windows;

/// <summary>
/// Windows WinMM MIDI backend (winmm.dll).
/// Enumeration uses midiInGetDevCaps / midiOutGetDevCaps.
/// Input uses a CALLBACK_FUNCTION delegate; WinMM delivers pre-parsed messages
/// (no running status) so data is trimmed to the correct length before
/// being fed to the shared MidiStreamParser.
/// Output uses midiOutShortMsg for short messages and midiOutLongMsg for SysEx.
/// </summary>
internal static class WinMmBackend
{
    public static IReadOnlyList<MidiInputDeviceInfo> GetInputDevices()
    {
        var count = WinMmNative.midiInGetNumDevs();
        var result = new List<MidiInputDeviceInfo>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var caps = new WinMmNative.MIDIINCAPS();
            if (WinMmNative.midiInGetDevCaps((nint)i, ref caps, (uint)Marshal.SizeOf<WinMmNative.MIDIINCAPS>())
                == WinMmNative.MMSYSERR_NOERROR)
            {
                result.Add(new MidiInputDeviceInfo(i.ToString(), caps.szPname));
            }
        }
        return result;
    }

    public static IReadOnlyList<MidiOutputDeviceInfo> GetOutputDevices()
    {
        var count = WinMmNative.midiOutGetNumDevs();
        var result = new List<MidiOutputDeviceInfo>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var caps = new WinMmNative.MIDIOUTCAPS();
            if (WinMmNative.midiOutGetDevCaps((nint)i, ref caps, (uint)Marshal.SizeOf<WinMmNative.MIDIOUTCAPS>())
                == WinMmNative.MMSYSERR_NOERROR)
            {
                result.Add(new MidiOutputDeviceInfo(i.ToString(), caps.szPname));
            }
        }
        return result;
    }
}

// ---------------------------------------------------------------------------

internal sealed class WinMmInputBackend : IMidiInputBackend
{
    // One SysEx receive buffer per device — 4 KB is large enough for any
    // LED-feedback or device-identity SysEx we'd receive from a control surface.
    private const int SysExBufferSize = 4096;

    private readonly MidiInputDeviceInfo _info;
    private nint _handle;
    private nint _sysExHeaderPtr;
    private nint _sysExDataPtr;
    private WinMmNative.MidiInProc? _callback; // keep alive — prevents GC collection
    private Action<ReadOnlyMemory<byte>>? _onData;

    public WinMmInputBackend(MidiInputDeviceInfo info) => _info = info;

    public string Id   => _info.Id;
    public string Name => _info.Name;

    public void StartReceiving(Action<ReadOnlyMemory<byte>> onData)
    {
        _onData   = onData;
        _callback = OnMidiInProc; // closure captures this; stored as field to pin delegate

        var deviceId = uint.Parse(_info.Id);
        var rc = WinMmNative.midiInOpen(out _handle, deviceId, _callback, nint.Zero,
            WinMmNative.CALLBACK_FUNCTION);
        if (rc != WinMmNative.MMSYSERR_NOERROR)
            throw new IOException($"midiInOpen failed for '{_info.Name}': error {rc}");

        PrepareSysExBuffer();

        WinMmNative.midiInStart(_handle);
    }

    /// <summary>
    /// WinMM callback — called on the WinMM driver thread.
    /// Keep this method non-blocking; heavy work is dispatched by the caller's subscriber.
    /// </summary>
    private void OnMidiInProc(nint hMidiIn, uint wMsg, nint dwInstance, nint dwParam1, nint dwParam2)
    {
        switch (wMsg)
        {
            case WinMmNative.MIM_DATA:
                // dwParam1 low 3 bytes: status, data1, data2 (zero-padded)
                var status = (byte)(dwParam1 & 0xFF);
                var data1  = (byte)((dwParam1 >> 8)  & 0xFF);
                var data2  = (byte)((dwParam1 >> 16) & 0xFF);
                var len    = 1 + WinMmNative.DataLength(status);

                // Emit only the bytes this message type actually uses.
                // WinMM never uses running status, so each callback is a complete message.
                Span<byte> buf = stackalloc byte[3];
                buf[0] = status;
                if (len > 1) buf[1] = data1;
                if (len > 2) buf[2] = data2;
                _onData?.Invoke(buf[..len].ToArray());
                break;

            case WinMmNative.MIM_LONGDATA:
                // dwParam1 is the MIDIHDR* we passed to midiInAddBuffer.
                OnSysExReceived(dwParam1);
                break;

            // MIM_OPEN, MIM_CLOSE, MIM_ERROR: nothing to do
        }
    }

    private void OnSysExReceived(nint headerPtr)
    {
        var header = Marshal.PtrToStructure<WinMmNative.MIDIHDR>(headerPtr);
        var recorded = (int)header.dwBytesRecorded;

        if (recorded > 0)
        {
            // WinMM includes F0 and F7 in the data. Pass directly to the parser.
            var bytes = new byte[recorded];
            Marshal.Copy(header.lpData, bytes, 0, recorded);
            _onData?.Invoke(bytes);
        }

        // Re-add the buffer so the next SysEx message can be captured.
        // Calling midiInPrepareHeader + midiInAddBuffer from the callback is
        // the standard pattern used in real-world WinMM applications.
        if (_handle != nint.Zero)
        {
            var headerSize = (uint)Marshal.SizeOf<WinMmNative.MIDIHDR>();
            WinMmNative.midiInPrepareHeader(_handle, headerPtr, headerSize);
            WinMmNative.midiInAddBuffer(_handle, headerPtr, headerSize);
        }
    }

    private void PrepareSysExBuffer()
    {
        var headerSize = (uint)Marshal.SizeOf<WinMmNative.MIDIHDR>();

        _sysExDataPtr   = Marshal.AllocHGlobal(SysExBufferSize);
        _sysExHeaderPtr = Marshal.AllocHGlobal((int)headerSize);

        // Zero the header, then set the data pointer and buffer length.
        for (int i = 0; i < (int)headerSize; i++)
            Marshal.WriteByte(_sysExHeaderPtr, i, 0);

        Marshal.WriteIntPtr(_sysExHeaderPtr, 0,              _sysExDataPtr);    // lpData
        Marshal.WriteInt32(_sysExHeaderPtr, nint.Size,       SysExBufferSize);  // dwBufferLength

        WinMmNative.midiInPrepareHeader(_handle, _sysExHeaderPtr, headerSize);
        WinMmNative.midiInAddBuffer(_handle, _sysExHeaderPtr, headerSize);
    }

    public void StopReceiving()
    {
        if (_handle == nint.Zero) return;

        // midiInStop + midiInReset flushes pending buffers and fires MIM_LONGDATA
        // for any in-flight SysEx, then we can safely close and unprepare.
        WinMmNative.midiInStop(_handle);
        WinMmNative.midiInReset(_handle); // returns all pending buffers to app

        if (_sysExHeaderPtr != nint.Zero)
        {
            WinMmNative.midiInUnprepareHeader(_handle, _sysExHeaderPtr,
                (uint)Marshal.SizeOf<WinMmNative.MIDIHDR>());
            Marshal.FreeHGlobal(_sysExHeaderPtr);
            _sysExHeaderPtr = nint.Zero;
        }
        if (_sysExDataPtr != nint.Zero)
        {
            Marshal.FreeHGlobal(_sysExDataPtr);
            _sysExDataPtr = nint.Zero;
        }

        WinMmNative.midiInClose(_handle);
        _handle = nint.Zero;
    }

    public void Dispose() => StopReceiving();
}

// ---------------------------------------------------------------------------

internal sealed class WinMmOutputBackend : IMidiOutputBackend
{
    private readonly MidiOutputDeviceInfo _info;
    private nint _handle;
    private bool _disposed;

    public WinMmOutputBackend(MidiOutputDeviceInfo info)
    {
        _info = info;
        var deviceId = uint.Parse(info.Id);
        var rc = WinMmNative.midiOutOpen(out _handle, deviceId,
            nint.Zero, nint.Zero, WinMmNative.CALLBACK_NULL);
        if (rc != WinMmNative.MMSYSERR_NOERROR)
            throw new IOException($"midiOutOpen failed for '{info.Name}': error {rc}");
    }

    public string Id   => _info.Id;
    public string Name => _info.Name;

    public void Send(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (data.IsEmpty) return;

        if (data[0] == 0xF0)
            SendSysEx(data);
        else
            SendShortMessage(data);
    }

    private void SendShortMessage(ReadOnlySpan<byte> data)
    {
        // Pack up to 3 bytes into a DWORD: status | (d1 << 8) | (d2 << 16)
        uint msg = 0;
        for (int i = 0; i < Math.Min(data.Length, 3); i++)
            msg |= (uint)data[i] << (i * 8);
        WinMmNative.midiOutShortMsg(_handle, msg);
    }

    private void SendSysEx(ReadOnlySpan<byte> data)
    {
        var headerSize = (uint)Marshal.SizeOf<WinMmNative.MIDIHDR>();
        var dataPtr    = Marshal.AllocHGlobal(data.Length);
        var headerPtr  = Marshal.AllocHGlobal((int)headerSize);

        try
        {
            // Copy SysEx bytes to unmanaged memory.
            for (int i = 0; i < data.Length; i++)
                Marshal.WriteByte(dataPtr, i, data[i]);

            // Zero header and fill required fields.
            for (int i = 0; i < (int)headerSize; i++)
                Marshal.WriteByte(headerPtr, i, 0);
            Marshal.WriteIntPtr(headerPtr, 0,        dataPtr);         // lpData
            Marshal.WriteInt32(headerPtr, nint.Size, data.Length);     // dwBufferLength

            WinMmNative.midiOutPrepareHeader(_handle, headerPtr, headerSize);
            WinMmNative.midiOutLongMsg(_handle, headerPtr, headerSize);

            // Spin until WinMM sets MHDR_DONE — typically microseconds for small SysEx.
            while (true)
            {
                var header = Marshal.PtrToStructure<WinMmNative.MIDIHDR>(headerPtr);
                if ((header.dwFlags & WinMmNative.MHDR_DONE) != 0) break;
                Thread.Sleep(1);
            }

            WinMmNative.midiOutUnprepareHeader(_handle, headerPtr, headerSize);
        }
        finally
        {
            Marshal.FreeHGlobal(headerPtr);
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle == nint.Zero) return;
        WinMmNative.midiOutClose(_handle);
        _handle = nint.Zero;
    }
}

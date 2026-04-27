namespace Haukcode.MidiDevice.Internal.macOS;

/// <summary>
/// macOS CoreMIDI (CoreMIDI.framework) backend.
/// Enumeration uses MIDIGetNumberOfSources / MIDIGetNumberOfDestinations with the
/// "name" and "uniqueID" properties.
/// Input creates a MIDIClient + MIDIInputPort and registers a MIDIReadProc callback.
/// CoreMIDI dispatches callbacks on a private thread; no CFRunLoop management
/// is needed for the read callback (MIDIInputPortCreate handles scheduling).
/// Output creates a MIDIOutputPort and calls MIDISend with a heap-allocated
/// MIDIPacketList built for each call.
/// </summary>
internal static class CoreMidiBackend
{
    public static IReadOnlyList<MidiInputDeviceInfo> GetInputDevices()
    {
        var count  = CoreMidiNative.MIDIGetNumberOfSources();
        var result = new List<MidiInputDeviceInfo>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var ep = CoreMidiNative.MIDIGetSource(i);
            if (ep == nint.Zero) continue;
            if (CoreMidiNative.GetIntegerProperty(ep, CoreMidiNative.kMIDIPropertyOffline) != 0) continue;

            var name = CoreMidiNative.GetStringProperty(ep, CoreMidiNative.kMIDIPropertyName);
            var uid  = CoreMidiNative.GetStringProperty(ep, CoreMidiNative.kMIDIPropertyUniqueID);
            var mfr  = CoreMidiNative.GetStringProperty(ep, CoreMidiNative.kMIDIPropertyManufacturer);
            if (string.IsNullOrEmpty(name)) name = $"MIDI Source {i}";
            if (string.IsNullOrEmpty(uid))  uid  = i.ToString();

            result.Add(new MidiInputDeviceInfo(uid, name, string.IsNullOrEmpty(mfr) ? null : mfr));
        }
        return result;
    }

    public static IReadOnlyList<MidiOutputDeviceInfo> GetOutputDevices()
    {
        var count  = CoreMidiNative.MIDIGetNumberOfDestinations();
        var result = new List<MidiOutputDeviceInfo>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var ep = CoreMidiNative.MIDIGetDestination(i);
            if (ep == nint.Zero) continue;
            if (CoreMidiNative.GetIntegerProperty(ep, CoreMidiNative.kMIDIPropertyOffline) != 0) continue;

            var name = CoreMidiNative.GetStringProperty(ep, CoreMidiNative.kMIDIPropertyName);
            var uid  = CoreMidiNative.GetStringProperty(ep, CoreMidiNative.kMIDIPropertyUniqueID);
            var mfr  = CoreMidiNative.GetStringProperty(ep, CoreMidiNative.kMIDIPropertyManufacturer);
            if (string.IsNullOrEmpty(name)) name = $"MIDI Destination {i}";
            if (string.IsNullOrEmpty(uid))  uid  = i.ToString();

            result.Add(new MidiOutputDeviceInfo(uid, name, string.IsNullOrEmpty(mfr) ? null : mfr));
        }
        return result;
    }

    // -----------------------------------------------------------------------
    // Internal helpers — endpoint lookup by uniqueID
    // -----------------------------------------------------------------------

    /// <summary>Find a source endpoint by the uniqueID stored in the device info.</summary>
    internal static nint FindSource(string uid)
    {
        var count = CoreMidiNative.MIDIGetNumberOfSources();
        for (uint i = 0; i < count; i++)
        {
            var ep    = CoreMidiNative.MIDIGetSource(i);
            var epUid = CoreMidiNative.GetStringProperty(ep, CoreMidiNative.kMIDIPropertyUniqueID);
            if (epUid == uid || i.ToString() == uid) return ep;
        }
        return nint.Zero;
    }

    /// <summary>Find a destination endpoint by the uniqueID stored in the device info.</summary>
    internal static nint FindDestination(string uid)
    {
        var count = CoreMidiNative.MIDIGetNumberOfDestinations();
        for (uint i = 0; i < count; i++)
        {
            var ep    = CoreMidiNative.MIDIGetDestination(i);
            var epUid = CoreMidiNative.GetStringProperty(ep, CoreMidiNative.kMIDIPropertyUniqueID);
            if (epUid == uid || i.ToString() == uid) return ep;
        }
        return nint.Zero;
    }
}

// ---------------------------------------------------------------------------

internal sealed class CoreMidiInputBackend : IMidiInputBackend
{
    private readonly MidiInputDeviceInfo _info;
    private nint _client;
    private nint _port;
    private nint _source;
    private Action<ReadOnlyMemory<byte>>? _onData;

    // Keep delegate alive — prevents GC collection while the port is open.
    private CoreMidiNative.MIDIReadProc? _readProc;

    public CoreMidiInputBackend(MidiInputDeviceInfo info) => _info = info;

    public string Id   => _info.Id;
    public string Name => _info.Name;

    public void StartReceiving(Action<ReadOnlyMemory<byte>> onData)
    {
        _onData   = onData;
        _readProc = OnMidiRead;

        _source = CoreMidiBackend.FindSource(_info.Id);
        if (_source == nint.Zero)
            throw new IOException($"CoreMIDI source not found for '{_info.Name}' (id={_info.Id})");

        var clientName = CoreMidiNative.CFStringCreateWithCString(
            nint.Zero, $"HaukcodeIn:{_info.Name}", CoreMidiNative.kCFStringEncodingUTF8);
        var portName   = CoreMidiNative.CFStringCreateWithCString(
            nint.Zero, "MidiInputPort", CoreMidiNative.kCFStringEncodingUTF8);
        try
        {
            var rc = CoreMidiNative.MIDIClientCreate(clientName, null, nint.Zero, out _client);
            if (rc != CoreMidiNative.NoErr)
                throw new IOException($"MIDIClientCreate failed for '{_info.Name}': {rc}");

            rc = CoreMidiNative.MIDIInputPortCreate(_client, portName, _readProc, nint.Zero, out _port);
            if (rc != CoreMidiNative.NoErr)
                throw new IOException($"MIDIInputPortCreate failed for '{_info.Name}': {rc}");

            rc = CoreMidiNative.MIDIPortConnectSource(_port, _source, nint.Zero);
            if (rc != CoreMidiNative.NoErr)
                throw new IOException($"MIDIPortConnectSource failed for '{_info.Name}': {rc}");
        }
        finally
        {
            CoreMidiNative.CFRelease(clientName);
            CoreMidiNative.CFRelease(portName);
        }
    }

    /// <summary>
    /// CoreMIDI read callback — called on a CoreMIDI-managed thread.
    /// Iterates the MIDIPacketList and forwards each packet's bytes to the parser.
    /// </summary>
    private void OnMidiRead(nint pktlist, nint readProcRefCon, nint srcConnRefCon)
    {
        if (_onData == null) return;

        // MIDIPacketList layout (packed):
        //   UInt32 numPackets        @ offset 0
        //   MIDIPacket[]             @ offset 4
        //
        // MIDIPacket layout (packed):
        //   UInt64 timeStamp         @ offset 0
        //   UInt16 length            @ offset 8
        //   Byte   data[length]      @ offset 10
        //
        // Packets are tightly packed (no padding between packets).

        var numPackets = (uint)Marshal.ReadInt32(pktlist);
        var packetPtr  = pktlist + CoreMidiNative.PacketListHeaderSize;

        for (uint p = 0; p < numPackets; p++)
        {
            // length is at offset 8 inside the packet
            var length = (int)(ushort)Marshal.ReadInt16(packetPtr, 8);
            if (length > 0)
            {
                var bytes = new byte[length];
                Marshal.Copy(packetPtr + CoreMidiNative.PacketHeaderSize, bytes, 0, length);
                _onData.Invoke(bytes);
            }

            // Advance to the next packet: header(10) + data bytes, rounded up to 4-byte alignment.
            var stride = CoreMidiNative.PacketHeaderSize + length;
            // MIDIPacket data is aligned to 4 bytes on Apple Silicon / x86-64.
            // The OS guarantees the list is contiguous; stride must be 4-byte aligned.
            if (stride % 4 != 0) stride += 4 - (stride % 4);
            packetPtr += stride;
        }
    }

    public void StopReceiving()
    {
        if (_port != nint.Zero && _source != nint.Zero)
        {
            CoreMidiNative.MIDIPortDisconnectSource(_port, _source);
            _source = nint.Zero;
        }
        if (_port != nint.Zero)
        {
            CoreMidiNative.MIDIPortDispose(_port);
            _port = nint.Zero;
        }
        if (_client != nint.Zero)
        {
            CoreMidiNative.MIDIClientDispose(_client);
            _client = nint.Zero;
        }
    }

    public void Dispose() => StopReceiving();
}

// ---------------------------------------------------------------------------

internal sealed class CoreMidiOutputBackend : IMidiOutputBackend
{
    private readonly MidiOutputDeviceInfo _info;
    private nint _client;
    private nint _port;
    private nint _dest;
    private bool _disposed;

    public CoreMidiOutputBackend(MidiOutputDeviceInfo info)
    {
        _info = info;
        _dest = CoreMidiBackend.FindDestination(info.Id);
        if (_dest == nint.Zero)
            throw new IOException($"CoreMIDI destination not found for '{info.Name}' (id={info.Id})");

        var clientName = CoreMidiNative.CFStringCreateWithCString(
            nint.Zero, $"HaukcodeOut:{info.Name}", CoreMidiNative.kCFStringEncodingUTF8);
        var portName   = CoreMidiNative.CFStringCreateWithCString(
            nint.Zero, "MidiOutputPort", CoreMidiNative.kCFStringEncodingUTF8);
        try
        {
            var rc = CoreMidiNative.MIDIClientCreate(clientName, null, nint.Zero, out _client);
            if (rc != CoreMidiNative.NoErr)
                throw new IOException($"MIDIClientCreate failed for '{info.Name}': {rc}");

            rc = CoreMidiNative.MIDIOutputPortCreate(_client, portName, out _port);
            if (rc != CoreMidiNative.NoErr)
                throw new IOException($"MIDIOutputPortCreate failed for '{info.Name}': {rc}");
        }
        finally
        {
            CoreMidiNative.CFRelease(clientName);
            CoreMidiNative.CFRelease(portName);
        }
    }

    public string Id   => _info.Id;
    public string Name => _info.Name;

    public void Send(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (data.IsEmpty) return;

        // Build a MIDIPacketList with a single packet in unmanaged memory.
        //
        // Layout:
        //   [0]      UInt32 numPackets = 1
        //   [4]      UInt64 timeStamp  = 0  (send immediately)
        //   [12]     UInt16 length
        //   [14]     Byte   data[length]
        //
        // Total = PacketListHeaderSize(4) + PacketHeaderSize(10) + data.Length

        var totalSize = CoreMidiNative.PacketListHeaderSize
                      + CoreMidiNative.PacketHeaderSize
                      + data.Length;

        var pktlist = Marshal.AllocHGlobal(totalSize);
        try
        {
            // Zero the header area
            for (int i = 0; i < CoreMidiNative.PacketListHeaderSize + CoreMidiNative.PacketHeaderSize; i++)
                Marshal.WriteByte(pktlist, i, 0);

            // numPackets = 1
            Marshal.WriteInt32(pktlist, 0, 1);

            // timeStamp = 0 (offset 4, 8 bytes — already zeroed)

            // length (offset 4+8 = 12)
            Marshal.WriteInt16(pktlist, CoreMidiNative.PacketListHeaderSize + 8, (short)(ushort)data.Length);

            // data bytes (offset 4+10 = 14)
            var dataOffset = CoreMidiNative.PacketListHeaderSize + CoreMidiNative.PacketHeaderSize;
            for (int i = 0; i < data.Length; i++)
                Marshal.WriteByte(pktlist, dataOffset + i, data[i]);

            CoreMidiNative.MIDISend(_port, _dest, pktlist);
        }
        finally
        {
            Marshal.FreeHGlobal(pktlist);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_port != nint.Zero)
        {
            CoreMidiNative.MIDIPortDispose(_port);
            _port = nint.Zero;
        }
        if (_client != nint.Zero)
        {
            CoreMidiNative.MIDIClientDispose(_client);
            _client = nint.Zero;
        }
    }
}

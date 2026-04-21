namespace Haukcode.MidiDevice.Internal;

/// <summary>
/// Stateful MIDI 1.0 byte stream parser. Handles:
/// <list type="bullet">
///   <item>Running status — voice messages (0x80–0xEF) retain their status byte for subsequent data-only messages</item>
///   <item>SysEx framing (F0…F7) with real-time bytes interleaved mid-SysEx</item>
///   <item>Split reads — a message may arrive across multiple <see cref="Process"/> calls</item>
///   <item>Note On velocity 0 normalised to <see cref="NoteOffMessage"/></item>
/// </list>
/// Not thread-safe — use one parser instance per device.
/// </summary>
internal sealed class MidiStreamParser
{
    // Current status byte. Retained across messages for running status (voice messages only).
    // 0 means no active status.
    private byte _currentStatus;

    // Data bytes accumulated for the current message.
    private readonly byte[] _data = new byte[2];
    private int _dataCount;
    private int _dataNeeded;

    // SysEx accumulation.
    private bool _inSysEx;
    private readonly List<byte> _sysExData = new();

    /// <summary>
    /// Process a chunk of raw bytes, invoking <paramref name="onMessage"/> for each complete message.
    /// </summary>
    public void Process(ReadOnlySpan<byte> bytes, Action<MidiMessage> onMessage)
    {
        foreach (var b in bytes)
            ProcessByte(b, onMessage);
    }

    /// <summary>Reset parser state, e.g. after a device reconnect.</summary>
    public void Reset()
    {
        _currentStatus = 0;
        _dataCount = 0;
        _dataNeeded = 0;
        _inSysEx = false;
        _sysExData.Clear();
    }

    private void ProcessByte(byte b, Action<MidiMessage> onMessage)
    {
        // Real-time messages (0xF8–0xFF): single byte, may appear anywhere including
        // mid-SysEx, and do NOT affect running status. We ignore them (clock, active
        // sensing, reset) — they are not relevant for control surface use.
        if (b >= 0xF8)
            return;

        // SysEx End (F7)
        if (b == 0xF7)
        {
            if (_inSysEx)
            {
                onMessage(new SysExMessage(_sysExData.ToArray()));
                _sysExData.Clear();
                _inSysEx = false;
            }
            return;
        }

        // Any non-real-time status byte while inside SysEx aborts the SysEx (invalid
        // MIDI, but be defensive).
        if (b >= 0x80 && _inSysEx)
        {
            _inSysEx = false;
            _sysExData.Clear();
        }

        // SysEx Start (F0): clears running status and pending message state.
        if (b == 0xF0)
        {
            _currentStatus = 0;
            _dataCount = 0;
            _inSysEx = true;
            return;
        }

        // Accumulate SysEx data bytes.
        if (_inSysEx)
        {
            if (b < 0x80)
                _sysExData.Add(b);
            return;
        }

        // --- Non-SysEx status byte (0x80–0xF7, but F0/F7 handled above) ---
        if (b >= 0x80)
        {
            // System Common (0xF1–0xF6) clears running status; voice messages set it.
            _currentStatus = b >= 0xF0 ? (byte)0 : b;
            _dataCount = 0;
            _dataNeeded = DataBytesNeeded(b);

            // Single-byte system messages (Tune Request F6, etc.) produce no MidiMessage.
            if (_dataNeeded == 0)
                _currentStatus = 0;

            return;
        }

        // --- Data byte (0x00–0x7F) ---

        // No active status and no running status: discard.
        if (_currentStatus == 0)
            return;

        if (_dataCount < 2)
            _data[_dataCount] = b;
        _dataCount++;

        if (_dataCount < _dataNeeded)
            return;

        // Message complete — build and emit.
        var msg = BuildMessage(_currentStatus, _data);
        if (msg != null)
            onMessage(msg);

        // Running status: voice messages keep _currentStatus so the next data byte
        // starts a new message with the same status. System Common clears it (handled above).
        _dataCount = 0;

        // System messages do not participate in running status (already cleared above
        // when the status byte arrived, but guard here too).
        if (_currentStatus >= 0xF0)
            _currentStatus = 0;
    }

    private static int DataBytesNeeded(byte status)
    {
        // For voice messages (0x80–0xEF) mask off the channel nibble.
        return (status & 0xF0) switch
        {
            0x80 => 2, // Note Off
            0x90 => 2, // Note On
            0xA0 => 2, // Poly Key Pressure
            0xB0 => 2, // Control Change
            0xC0 => 1, // Program Change
            0xD0 => 1, // Channel Pressure
            0xE0 => 2, // Pitch Bend
            // System Common — match on full byte.
            0xF0 => status switch
            {
                0xF1 => 1, // MTC Quarter Frame
                0xF2 => 2, // Song Position Pointer
                0xF3 => 1, // Song Select
                _ => 0,    // F6 Tune Request, undefined
            },
            _ => 0,
        };
    }

    private static MidiMessage? BuildMessage(byte status, byte[] data)
    {
        var channel = (byte)(status & 0x0F);
        return (status & 0xF0) switch
        {
            0x80 => new NoteOffMessage(channel, data[0], data[1]),
            0x90 => data[1] == 0
                ? new NoteOffMessage(channel, data[0], 0) // Note On vel=0 → Note Off
                : new NoteOnMessage(channel, data[0], data[1]),
            0xA0 => new PolyKeyPressureMessage(channel, data[0], data[1]),
            0xB0 => new ControlChangeMessage(channel, data[0], data[1]),
            0xC0 => new ProgramChangeMessage(channel, data[0]),
            0xD0 => new ChannelPressureMessage(channel, data[0]),
            0xE0 => new PitchBendMessage(channel, (short)(((data[1] << 7) | data[0]) - 8192)),
            _ => null,
        };
    }
}

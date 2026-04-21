namespace Haukcode.MidiDevice;

/// <summary>Encodes typed <see cref="MidiMessage"/> instances to raw MIDI bytes.</summary>
internal static class MidiEncoder
{
    /// <summary>
    /// Encodes <paramref name="message"/> into <paramref name="buf"/> (must be at least 3 bytes).
    /// Returns the number of bytes written, or 0 for messages that require a different send path
    /// (e.g. <see cref="SysExMessage"/> — use <see cref="MidiOutputDevice.SendRaw"/> instead).
    /// </summary>
    public static int Encode(MidiMessage message, Span<byte> buf)
    {
        switch (message)
        {
            case NoteOffMessage m:
                buf[0] = (byte)(0x80 | (m.Channel & 0x0F));
                buf[1] = m.NoteNumber;
                buf[2] = m.Velocity;
                return 3;

            case NoteOnMessage m:
                buf[0] = (byte)(0x90 | (m.Channel & 0x0F));
                buf[1] = m.NoteNumber;
                buf[2] = m.Velocity;
                return 3;

            case PolyKeyPressureMessage m:
                buf[0] = (byte)(0xA0 | (m.Channel & 0x0F));
                buf[1] = m.NoteNumber;
                buf[2] = m.Pressure;
                return 3;

            case ControlChangeMessage m:
                buf[0] = (byte)(0xB0 | (m.Channel & 0x0F));
                buf[1] = m.ControlNumber;
                buf[2] = m.Value;
                return 3;

            case ProgramChangeMessage m:
                buf[0] = (byte)(0xC0 | (m.Channel & 0x0F));
                buf[1] = m.ProgramNumber;
                return 2;

            case ChannelPressureMessage m:
                buf[0] = (byte)(0xD0 | (m.Channel & 0x0F));
                buf[1] = m.Pressure;
                return 2;

            case PitchBendMessage m:
                var raw = m.Value + 8192; // shift to 0–16383
                buf[0] = (byte)(0xE0 | (m.Channel & 0x0F));
                buf[1] = (byte)(raw & 0x7F);        // LSB
                buf[2] = (byte)((raw >> 7) & 0x7F); // MSB
                return 3;

            case SysExMessage:
                // Caller must use SendRaw with the full F0...F7 byte sequence.
                return 0;

            default:
                return 0;
        }
    }
}

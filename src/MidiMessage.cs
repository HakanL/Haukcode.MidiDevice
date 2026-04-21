namespace Haukcode.MidiDevice;

public abstract record MidiMessage;

/// <summary>Note Off (status 0x8n). Also produced by Note On with velocity 0.</summary>
public sealed record NoteOffMessage(byte Channel, byte NoteNumber, byte Velocity) : MidiMessage;

/// <summary>Note On with non-zero velocity (status 0x9n).</summary>
public sealed record NoteOnMessage(byte Channel, byte NoteNumber, byte Velocity) : MidiMessage;

/// <summary>Polyphonic Key Pressure / Aftertouch (status 0xAn).</summary>
public sealed record PolyKeyPressureMessage(byte Channel, byte NoteNumber, byte Pressure) : MidiMessage;

/// <summary>Control Change (status 0xBn).</summary>
public sealed record ControlChangeMessage(byte Channel, byte ControlNumber, byte Value) : MidiMessage;

/// <summary>Program Change (status 0xCn).</summary>
public sealed record ProgramChangeMessage(byte Channel, byte ProgramNumber) : MidiMessage;

/// <summary>Channel Pressure / Aftertouch (status 0xDn).</summary>
public sealed record ChannelPressureMessage(byte Channel, byte Pressure) : MidiMessage;

/// <summary>Pitch Bend Change (status 0xEn). Value is −8192 to 8191.</summary>
public sealed record PitchBendMessage(byte Channel, short Value) : MidiMessage;

/// <summary>System Exclusive. Data contains the bytes between F0 and F7 (exclusive).</summary>
public sealed record SysExMessage(byte[] Data) : MidiMessage;

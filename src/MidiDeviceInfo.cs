namespace Haukcode.MidiDevice;

/// <summary>Immutable descriptor for an enumerated MIDI input device. Pass to <see cref="MidiInputDevice.Open"/>.</summary>
public sealed record MidiInputDeviceInfo(string Id, string Name, string? Manufacturer = null);

/// <summary>Immutable descriptor for an enumerated MIDI output device. Pass to <see cref="MidiOutputDevice.Open"/>.</summary>
public sealed record MidiOutputDeviceInfo(string Id, string Name, string? Manufacturer = null);

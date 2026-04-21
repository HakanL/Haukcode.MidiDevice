# Haukcode.MidiDevice

Cross-platform USB MIDI device I/O for .NET — no native library dependencies.

## Key Features

- Enumerate, open, send, and receive on **Windows**, **macOS**, and **Linux ARM64** (Raspberry Pi)
- `IObservable<MidiMessage>` stream via **System.Reactive**
- Strongly-typed message records: `NoteOnMessage`, `ControlChangeMessage`, `SysExMessage`, and more
- Full MIDI byte stream parser — running status, SysEx framing, interleaved real-time messages
- Pure managed C# with P/Invoke only — WinMM / CoreMIDI / ALSA rawmidi

## Installation

```
dotnet add package Haukcode.MidiDevice
```

On Linux, `libasound2` must be present (`apt-get install libasound2`).

## Quick Start

```csharp
// Enumerate
var inputs = MidiDeviceManager.GetInputDevices();

// Open and subscribe
using var device = MidiInputDevice.Open(inputs.First(d => d.Name.Contains("LPD8")));

device.Messages.Subscribe(msg =>
{
    if (msg is ControlChangeMessage cc)
        Console.WriteLine($"CC ch={cc.Channel} #{cc.ControlNumber} = {cc.Value}");
});

// Send
var outputs = MidiDeviceManager.GetOutputDevices();
using var output = MidiOutputDevice.Open(outputs.First());
output.Send(new NoteOnMessage(Channel: 0, NoteNumber: 36, Velocity: 127));
output.SendRaw([0xF0, 0x47, 0x7F, 0x30, 0xF7]); // SysEx
```

## Platform Support

| Platform | Backend | Architecture |
|----------|---------|-------------|
| Windows | WinMM (`winmm.dll`) | x64, x86, ARM64 |
| macOS | CoreMIDI | x64, Apple Silicon |
| Linux | ALSA rawmidi (`libasound.so.2`) | x64, **ARM64** |

## Links

- [GitHub](https://github.com/HakanL/Haukcode.MidiDevice)

# Haukcode.MidiDevice

Cross-platform USB MIDI device I/O for .NET — enumerate, open, send, and receive on Windows, macOS, and Linux ARM64 with no native library dependencies.

[![NuGet](https://img.shields.io/nuget/v/Haukcode.MidiDevice.svg)](https://www.nuget.org/packages/Haukcode.MidiDevice)
[![Build](https://github.com/HakanL/Haukcode.MidiDevice/actions/workflows/main.yml/badge.svg)](https://github.com/HakanL/Haukcode.MidiDevice/actions)

---

## Features

- Enumerate and open MIDI input and output devices
- `IObservable<MidiMessage>` stream for received messages — **System.Reactive**
- Strongly-typed message records: `NoteOnMessage`, `NoteOffMessage`, `ControlChangeMessage`, `ProgramChangeMessage`, `PitchBendMessage`, `SysExMessage`, and more
- Full MIDI byte stream parser with running status and interleaved real-time message support
- Cross-platform — pure managed C#, P/Invoke only, no compiled native shared library:
  - **Windows** — WinMM (`winmm.dll`)
  - **macOS** — CoreMIDI (`CoreMIDI.framework`)
  - **Linux** — ALSA rawmidi (`libasound.so.2`), including ARM64 (Raspberry Pi)

---

## Installation

```
dotnet add package Haukcode.MidiDevice
```

On Linux, `libasound2` must be present (`apt-get install libasound2`).

---

## Quick Start

### Enumerate and receive

```csharp
var inputs = MidiDeviceManager.GetInputDevices();
foreach (var info in inputs)
    Console.WriteLine($"{info.Name}");

var info = inputs.First(d => d.Name.Contains("LPD8"));
using var device = MidiInputDevice.Open(info);

device.Messages.Subscribe(msg =>
{
    switch (msg)
    {
        case NoteOnMessage noteOn:
            Console.WriteLine($"Note On  ch={noteOn.Channel} note={noteOn.NoteNumber} vel={noteOn.Velocity}");
            break;
        case ControlChangeMessage cc:
            Console.WriteLine($"CC       ch={cc.Channel} cc={cc.ControlNumber} val={cc.Value}");
            break;
        case ProgramChangeMessage pc:
            Console.WriteLine($"Program  ch={pc.Channel} program={pc.ProgramNumber}");
            break;
    }
});

Console.ReadLine();
```

### Send

```csharp
var outputs = MidiDeviceManager.GetOutputDevices();
using var device = MidiOutputDevice.Open(outputs.First(d => d.Name.Contains("LPD8")));

// Typed message
device.Send(new NoteOnMessage(Channel: 0, NoteNumber: 36, Velocity: 127));

// Raw bytes (SysEx, vendor-specific)
device.SendRaw([0xF0, 0x47, 0x7F, 0x30, 0x2C, 0x01, 0x00, 0xF7]);
```

---

## Message Types

| Record | Properties | Notes |
|--------|-----------|-------|
| `NoteOnMessage` | `Channel`, `NoteNumber`, `Velocity` | Note On with velocity 0 is normalised to `NoteOffMessage` by the parser |
| `NoteOffMessage` | `Channel`, `NoteNumber`, `Velocity` | |
| `ControlChangeMessage` | `Channel`, `ControlNumber`, `Value` | |
| `ProgramChangeMessage` | `Channel`, `ProgramNumber` | |
| `PolyKeyPressureMessage` | `Channel`, `NoteNumber`, `Pressure` | |
| `ChannelPressureMessage` | `Channel`, `Pressure` | |
| `PitchBendMessage` | `Channel`, `Value` | −8192 to 8191 |
| `SysExMessage` | `Data` | Raw bytes between F0 and F7 (exclusive) |

---

## Threading

`MidiInputDevice.Messages` is a hot observable that emits on the backend receive thread. Keep subscription handlers non-blocking — dispatch heavy work via `Task.Run()`.

---

## Platform Notes

### Linux
ALSA rawmidi (`/dev/snd/midiC*D*`) is used. Device paths are enumerated via `snd_device_name_hint`. The `libasound2` package is required at runtime.

### Windows
WinMM (`winmm.dll`) is used. Short messages (≤ 3 bytes) are sent via `midiOutShortMsg`; SysEx via `midiOutLongMsg`.

### macOS
CoreMIDI (`CoreMIDI.framework`) is used. Both sources (input) and destinations (output) are enumerated.

---

## Contributing

Pull requests welcome. When adding a new platform backend, implement `IMidiInputBackend` and `IMidiOutputBackend` under `Internal/<Platform>/` and wire up `MidiDeviceManager`.

## Links

- [GitHub](https://github.com/HakanL/Haukcode.MidiDevice)
- [MIDI 1.0 Specification](https://www.midi.org/specifications)

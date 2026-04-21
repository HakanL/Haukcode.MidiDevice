using Haukcode.MidiDevice.Internal;
using Haukcode.MidiDevice.Internal.Linux;
using Haukcode.MidiDevice.Internal.macOS;
using Haukcode.MidiDevice.Internal.Windows;

namespace Haukcode.MidiDevice;

/// <summary>
/// Enumerates available MIDI devices and creates platform backends for opening them.
/// Platform selection happens at runtime via <see cref="RuntimeInformation"/>.
/// </summary>
public static class MidiDeviceManager
{
    public static IReadOnlyList<MidiInputDeviceInfo> GetInputDevices()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WinMmBackend.GetInputDevices();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return CoreMidiBackend.GetInputDevices();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return AlsaBackend.GetInputDevices();

        throw new PlatformNotSupportedException("MIDI device enumeration is not supported on this platform.");
    }

    public static IReadOnlyList<MidiOutputDeviceInfo> GetOutputDevices()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WinMmBackend.GetOutputDevices();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return CoreMidiBackend.GetOutputDevices();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return AlsaBackend.GetOutputDevices();

        throw new PlatformNotSupportedException("MIDI device enumeration is not supported on this platform.");
    }

    internal static IMidiInputBackend CreateInputBackend(MidiInputDeviceInfo info)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WinMmInputBackend(info);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new CoreMidiInputBackend(info);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new AlsaInputBackend(info);

        throw new PlatformNotSupportedException("MIDI device I/O is not supported on this platform.");
    }

    internal static IMidiOutputBackend CreateOutputBackend(MidiOutputDeviceInfo info)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WinMmOutputBackend(info);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new CoreMidiOutputBackend(info);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new AlsaOutputBackend(info);

        throw new PlatformNotSupportedException("MIDI device I/O is not supported on this platform.");
    }
}

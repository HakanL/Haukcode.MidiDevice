namespace Haukcode.MidiDevice.Internal.macOS;

/// <summary>macOS CoreMIDI (CoreMIDI.framework) backend. Implemented in Phase 4.</summary>
internal static class CoreMidiBackend
{
    public static IReadOnlyList<MidiInputDeviceInfo> GetInputDevices()
        => throw new NotImplementedException("macOS CoreMIDI backend not yet implemented.");

    public static IReadOnlyList<MidiOutputDeviceInfo> GetOutputDevices()
        => throw new NotImplementedException("macOS CoreMIDI backend not yet implemented.");
}

internal sealed class CoreMidiInputBackend(MidiInputDeviceInfo info) : IMidiInputBackend
{
    public string Id => info.Id;
    public string Name => info.Name;

    public void StartReceiving(Action<ReadOnlyMemory<byte>> onData)
        => throw new NotImplementedException("macOS CoreMIDI backend not yet implemented.");

    public void StopReceiving() { }
    public void Dispose() { }
}

internal sealed class CoreMidiOutputBackend(MidiOutputDeviceInfo info) : IMidiOutputBackend
{
    public string Id => info.Id;
    public string Name => info.Name;

    public void Send(ReadOnlySpan<byte> data)
        => throw new NotImplementedException("macOS CoreMIDI backend not yet implemented.");

    public void Dispose() { }
}

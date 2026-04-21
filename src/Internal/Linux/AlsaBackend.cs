namespace Haukcode.MidiDevice.Internal.Linux;

/// <summary>Linux ALSA rawmidi (libasound.so.2) backend. Implemented in Phase 2.</summary>
internal static class AlsaBackend
{
    public static IReadOnlyList<MidiInputDeviceInfo> GetInputDevices()
        => throw new NotImplementedException("Linux ALSA backend not yet implemented.");

    public static IReadOnlyList<MidiOutputDeviceInfo> GetOutputDevices()
        => throw new NotImplementedException("Linux ALSA backend not yet implemented.");
}

internal sealed class AlsaInputBackend(MidiInputDeviceInfo info) : IMidiInputBackend
{
    public string Id => info.Id;
    public string Name => info.Name;

    public void StartReceiving(Action<ReadOnlyMemory<byte>> onData)
        => throw new NotImplementedException("Linux ALSA backend not yet implemented.");

    public void StopReceiving() { }
    public void Dispose() { }
}

internal sealed class AlsaOutputBackend(MidiOutputDeviceInfo info) : IMidiOutputBackend
{
    public string Id => info.Id;
    public string Name => info.Name;

    public void Send(ReadOnlySpan<byte> data)
        => throw new NotImplementedException("Linux ALSA backend not yet implemented.");

    public void Dispose() { }
}

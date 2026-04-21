namespace Haukcode.MidiDevice.Internal.Windows;

/// <summary>Windows WinMM (winmm.dll) MIDI backend. Implemented in Phase 3.</summary>
internal static class WinMmBackend
{
    public static IReadOnlyList<MidiInputDeviceInfo> GetInputDevices()
        => throw new NotImplementedException("Windows WinMM backend not yet implemented.");

    public static IReadOnlyList<MidiOutputDeviceInfo> GetOutputDevices()
        => throw new NotImplementedException("Windows WinMM backend not yet implemented.");
}

internal sealed class WinMmInputBackend(MidiInputDeviceInfo info) : IMidiInputBackend
{
    public string Id => info.Id;
    public string Name => info.Name;

    public void StartReceiving(Action<ReadOnlyMemory<byte>> onData)
        => throw new NotImplementedException("Windows WinMM backend not yet implemented.");

    public void StopReceiving() { }
    public void Dispose() { }
}

internal sealed class WinMmOutputBackend(MidiOutputDeviceInfo info) : IMidiOutputBackend
{
    public string Id => info.Id;
    public string Name => info.Name;

    public void Send(ReadOnlySpan<byte> data)
        => throw new NotImplementedException("Windows WinMM backend not yet implemented.");

    public void Dispose() { }
}

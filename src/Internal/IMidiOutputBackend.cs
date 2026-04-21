namespace Haukcode.MidiDevice.Internal;

/// <summary>
/// Platform-specific MIDI output implementation.
/// Receives raw bytes from the public layer (already encoded from typed messages).
/// Implementations handle the short-message vs SysEx split required by WinMM.
/// </summary>
internal interface IMidiOutputBackend : IDisposable
{
    string Id { get; }
    string Name { get; }

    /// <summary>Send <paramref name="data"/> to the device. May be called from any thread.</summary>
    void Send(ReadOnlySpan<byte> data);
}

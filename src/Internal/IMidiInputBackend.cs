namespace Haukcode.MidiDevice.Internal;

/// <summary>
/// Platform-specific MIDI input implementation.
/// Delivers raw byte chunks to the registered callback; the public layer runs
/// them through <see cref="MidiStreamParser"/> to produce typed messages.
/// Chunks may span message boundaries — the parser handles accumulation.
/// </summary>
internal interface IMidiInputBackend : IDisposable
{
    string Id { get; }
    string Name { get; }

    /// <summary>Begin receiving. <paramref name="onData"/> is called on the backend thread.</summary>
    void StartReceiving(Action<ReadOnlyMemory<byte>> onData);

    /// <summary>Stop receiving. Safe to call multiple times.</summary>
    void StopReceiving();
}
